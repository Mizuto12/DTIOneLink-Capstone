using System.Net;
using System.Text.RegularExpressions;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DTIOneLink.Tests.Integration;

// End-to-end tests for the account-activation-link flow that replaced the
// shared default password (see docs/handoff/CHECKLIST.md Part B, "Account
// activation"). A brand-new account has no password anyone was ever told -
// ActivatedAtUtc stays null until the owner clicks their emailed link and
// chooses their own password.
public class AccountActivationIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public AccountActivationIntegrationTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static string ExtractLink(string textBody)
    {
        var match = Regex.Match(textBody, @"https?://\S+");
        Assert.True(match.Success, $"No link found in email body: {textBody}");
        return match.Value;
    }

    // Pulls a hidden <input name="..." value="..."> field's value out of
    // ConfirmLink's rendered HTML (uid/token are round-tripped through it).
    private static string ExtractHiddenField(string html, string name)
    {
        var match = Regex.Match(html, $@"name=""{name}""\s+value=""([^""]*)""");
        Assert.True(match.Success, $"No hidden field named \"{name}\" found in response.");
        return match.Groups[1].Value;
    }

    // Clicking an activation link only peeks at the token; this does the
    // full round trip (GET the confirmation page, then POST it) and
    // returns the final response, mirroring what a real browser session
    // submitting ConfirmLink.cshtml's form would produce.
    private async Task<HttpResponseMessage> ClickActivationLinkAsync(string link)
    {
        var clickResponse = await _client.GetAsync(new Uri(link).PathAndQuery);
        if (clickResponse.StatusCode != HttpStatusCode.OK)
        {
            return clickResponse;
        }

        var html = await clickResponse.Content.ReadAsStringAsync();
        var confirmToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(clickResponse);
        return await _client.PostAsync("/Account/ActivateConfirm", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["uid"] = ExtractHiddenField(html, "uid"),
            ["token"] = ExtractHiddenField(html, "token"),
            ["__RequestVerificationToken"] = confirmToken,
        }));
    }

    // A brand-new, never-activated account: PasswordHash is a random value
    // nobody was ever told, mirroring UserManagementController.Create.
    private static User NewPendingUser(string username = "new.hire@dti.gov.ph")
    {
        var user = new User
        {
            Username = username,
            Email = username,
            FullName = "New Hire",
            Role = "Employee",
            Department = "Business Development Division",
            IsActive = true,
            EmailConfirmed = false,
            MustChangePassword = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ActivatedAtUtc = null,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Guid.NewGuid().ToString("N"));
        return user;
    }

    [Fact]
    public async Task Login_PendingActivationAccount_ShowsActivationMessageAndDoesNotSignIn()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(NewPendingUser());
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);

        var postResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "new.hire@dti.gov.ph",
            ["Password"] = "whatever-they-guess",
            ["__RequestVerificationToken"] = token,
        }));

        // Re-renders the login form with an error, not a sign-in redirect.
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var html = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("activated yet", html);
    }

    [Fact]
    public async Task Activate_ValidLink_LetsThemChooseAPasswordAndThenLogIn()
    {
        int userId;
        using (var db = _factory.CreateDbContext())
        {
            var user = NewPendingUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        // Stand-in for UserManagementController.Create issuing the link.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var codes = scope.ServiceProvider.GetRequiredService<OneTimeCodeService>();
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            await codes.IssueLinkAsync(user, OneTimeCodePurpose.AccountActivation,
                t => $"http://localhost/Account/Activate?uid={userId}&token={t}");
        }

        var queue = _factory.Services.GetRequiredService<DTIOneLink.Services.Email.OtpEmailQueue>();
        Assert.True(queue.Reader.TryRead(out var message), "Expected an activation link to have been queued.");
        var link = ExtractLink(message!.TextBody);

        // Clicking the link only shows a confirmation page (the token is
        // peeked at, not consumed, so an email scanner pre-fetching this URL
        // couldn't burn it); confirming it then redirects to the clean
        // ActivatePassword URL ...
        var confirmResponse = await ClickActivationLinkAsync(link);
        Assert.Equal(HttpStatusCode.Redirect, confirmResponse.StatusCode);
        Assert.Contains("ActivatePassword", confirmResponse.Headers.Location!.ToString());

        // ... which now actually renders the set-password form.
        var formResponse = await _client.GetAsync(confirmResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, formResponse.StatusCode);
        var formToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(formResponse);

        var activateResponse = await _client.PostAsync("/Account/ActivatePassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["NewPassword"] = "Brand-New-Pass2",
            ["ConfirmPassword"] = "Brand-New-Pass2",
            ["__RequestVerificationToken"] = formToken,
        }));
        IntegrationTestHelpers.AssertRedirectsToLogin(activateResponse);

        using (var db = _factory.CreateDbContext())
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            Assert.NotNull(user.ActivatedAtUtc);
            Assert.True(user.EmailConfirmed);
            Assert.False(user.MustChangePassword);
        }

        // The chosen password actually works now.
        var loginGet = await _client.GetAsync("/Account/Login");
        var loginToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(loginGet);
        var loginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "new.hire@dti.gov.ph",
            ["Password"] = "Brand-New-Pass2",
            ["__RequestVerificationToken"] = loginToken,
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("/Employee", loginResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Activate_GetRequest_DoesNotConsumeTheToken()
    {
        // Regression test for the email-scanner problem: some mail security
        // gateways automatically visit a link to scan it before the person
        // clicks it. If GET consumed the token, that automated visit alone
        // would burn a genuine activation link before its owner ever saw
        // it. GET must only ever show the confirmation page.
        int userId;
        using (var db = _factory.CreateDbContext())
        {
            var user = NewPendingUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var codes = scope.ServiceProvider.GetRequiredService<OneTimeCodeService>();
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            var captured = "";
            await codes.IssueLinkAsync(user, OneTimeCodePurpose.AccountActivation, t => { captured = t; return captured; });
            token = captured;
        }

        // Simulate an automated scanner visiting the link several times.
        for (var i = 0; i < 3; i++)
        {
            var scanVisit = await _client.GetAsync($"/Account/Activate?uid={userId}&token={token}");
            Assert.Equal(HttpStatusCode.OK, scanVisit.StatusCode);
        }

        // The real person clicking through afterward still succeeds.
        var confirmResponse = await ClickActivationLinkAsync($"http://localhost/Account/Activate?uid={userId}&token={token}");
        Assert.Equal(HttpStatusCode.Redirect, confirmResponse.StatusCode);
        Assert.Contains("ActivatePassword", confirmResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Activate_WrongToken_RedirectsToLoginWithoutActivatingAnything()
    {
        int userId;
        using (var db = _factory.CreateDbContext())
        {
            var user = NewPendingUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var response = await _client.GetAsync($"/Account/Activate?uid={userId}&token=not-the-real-token");

        IntegrationTestHelpers.AssertRedirectsToLogin(response);

        using var db2 = _factory.CreateDbContext();
        var reloaded = await db2.Users.FirstAsync(u => u.Id == userId);
        Assert.Null(reloaded.ActivatedAtUtc);
    }

    [Fact]
    public async Task SelfServiceResend_PendingAccount_ActuallySendsAWorkingLink()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(NewPendingUser());
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/ResendActivationLink");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        await _client.PostAsync("/Account/ResendActivationLink", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "new.hire@dti.gov.ph",
            ["__RequestVerificationToken"] = token,
        }));

        var queue = _factory.Services.GetRequiredService<DTIOneLink.Services.Email.OtpEmailQueue>();
        Assert.True(queue.Reader.TryRead(out var message), "Expected a self-requested activation link to have been queued.");
        var link = ExtractLink(message!.TextBody);

        var confirmResponse = await ClickActivationLinkAsync(link);
        Assert.Equal(HttpStatusCode.Redirect, confirmResponse.StatusCode);
        Assert.Contains("ActivatePassword", confirmResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task SelfServiceResend_UnknownOrAlreadyActivatedEmail_GivesTheSameGenericMessage()
    {
        // Enumeration safety: an email that doesn't exist, and one that
        // belongs to an already-activated account, must both produce the
        // identical response as a genuinely pending account — otherwise this
        // page could be used to discover who has an account and who doesn't.
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division",
                username: "already.active@dti.gov.ph"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/ResendActivationLink");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);

        var unknownResponse = await _client.PostAsync("/Account/ResendActivationLink", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "nobody.here@dti.gov.ph",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, unknownResponse.StatusCode);

        var getResponse2 = await _client.GetAsync("/Account/ResendActivationLink");
        var token2 = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse2);
        var activeResponse = await _client.PostAsync("/Account/ResendActivationLink", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "already.active@dti.gov.ph",
            ["__RequestVerificationToken"] = token2,
        }));
        Assert.Equal(HttpStatusCode.Redirect, activeResponse.StatusCode);

        // Neither request queued an email.
        var queue = _factory.Services.GetRequiredService<DTIOneLink.Services.Email.OtpEmailQueue>();
        Assert.False(queue.Reader.TryRead(out _));

        var finalGet = await _client.GetAsync("/Account/ResendActivationLink");
        var finalHtml = await finalGet.Content.ReadAsStringAsync();
        Assert.Contains("If that email has an account awaiting activation", finalHtml);
    }

    [Fact]
    public async Task ResendingTheActivationLink_InvalidatesThePreviousOne()
    {
        int userId;
        using (var db = _factory.CreateDbContext())
        {
            var user = NewPendingUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        string firstToken, secondToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var codes = scope.ServiceProvider.GetRequiredService<OneTimeCodeService>();
            var user = await db.Users.FirstAsync(u => u.Id == userId);

            var captured = "";
            await codes.IssueLinkAsync(user, OneTimeCodePurpose.AccountActivation, t => { captured = t; return captured; });
            firstToken = captured;

            // Back-date the code just issued so the resend below isn't
            // rejected by the same too-soon throttle a real admin clicking
            // "Resend Link" twice in a row would also have to wait out —
            // this test is about invalidation, not the resend cooldown.
            var backdatedTo = DateTime.UtcNow - TimeSpan.FromMinutes(5);
            await db.OneTimeCodes
                .Where(c => c.UserId == userId && c.Purpose == OneTimeCodePurpose.AccountActivation)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAtUtc, backdatedTo));

            await codes.IssueLinkAsync(user, OneTimeCodePurpose.AccountActivation, t => { captured = t; return captured; });
            secondToken = captured;
        }

        var staleAttempt = await _client.GetAsync($"/Account/Activate?uid={userId}&token={firstToken}");
        IntegrationTestHelpers.AssertRedirectsToLogin(staleAttempt);
        using (var db = _factory.CreateDbContext())
        {
            Assert.Null((await db.Users.FirstAsync(u => u.Id == userId)).ActivatedAtUtc);
        }

        var freshAttempt = await ClickActivationLinkAsync($"http://localhost/Account/Activate?uid={userId}&token={secondToken}");
        Assert.Equal(HttpStatusCode.Redirect, freshAttempt.StatusCode);
        Assert.Contains("ActivatePassword", freshAttempt.Headers.Location!.ToString());
    }
}
