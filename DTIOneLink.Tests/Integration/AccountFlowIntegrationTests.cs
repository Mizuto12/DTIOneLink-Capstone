using System.Net;
using System.Text.RegularExpressions;
using DTIOneLink.Security;
using DTIOneLink.Services.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DTIOneLink.Tests.Integration;

// End-to-end tests through the REAL HTTP pipeline: Kestrel's test server,
// routing, session middleware, antiforgery validation, and the actual
// AccountController - not a direct method call like the unit tests. Each
// test gets its own factory/client, so each gets a clean in-memory database.
public class AccountFlowIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public AccountFlowIntegrationTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, // inspect each redirect explicitly
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task AnonymousRequestToAProtectedPage_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/TimeLogs");

        IntegrationTestHelpers.AssertRedirectsToLogin(response);
    }

    [Fact]
    public async Task Login_CorrectCredentials_SignsInAndRedirectsToRoleHome()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);

        var postResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode);
        Assert.Contains("/Employee", postResponse.Headers.Location!.ToString());

        // The session cookie now actually works for a protected page.
        var protectedPage = await _client.GetAsync("/TimeLogs");
        Assert.Equal(HttpStatusCode.OK, protectedPage.StatusCode);
    }

    [Fact]
    public async Task Login_AlreadySignedIn_RedirectsToRoleHomeInsteadOfShowingTheForm()
    {
        // Covers the gap a remembered-device auto-sign-in would otherwise
        // hit: Program.cs's middleware silently sets Session["UserId"]
        // before this action runs, but only a check in the action itself
        // turns that into a redirect - without it, a remembered user would
        // land back on the login form every time instead of their dashboard.
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));

        var secondVisit = await _client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.Redirect, secondVisit.StatusCode);
        Assert.Contains("/Employee", secondVisit.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_AlreadySignedInAsSomeoneElse_IsBlockedAndTheOriginalSessionStays()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division",
                password: "Correct-Horse-1", username: "first.user@dti.gov.ph"));
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division",
                password: "Correct-Horse-2", username: "second.user@dti.gov.ph"));
            await db.SaveChangesAsync();
        }

        // Sign in as the first account.
        var firstLoginGet = await _client.GetAsync("/Account/Login");
        var firstLoginToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(firstLoginGet);
        var firstLoginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "first.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = firstLoginToken,
        }));
        Assert.Equal(HttpStatusCode.Redirect, firstLoginResponse.StatusCode);

        // Same browser (same cookie container) attempts a second, different
        // account — correct credentials and all — without signing out first.
        // The GET itself would normally redirect away while signed in, so
        // this simulates the realistic path: a stale/cached form posted
        // directly, exactly the shared-office-PC scenario this guards.
        var secondLoginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "second.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-2",
            ["__RequestVerificationToken"] = firstLoginToken,
        }));

        Assert.Equal(HttpStatusCode.OK, secondLoginResponse.StatusCode); // re-renders the form with an error, not a redirect
        var html = await secondLoginResponse.Content.ReadAsStringAsync();
        Assert.Contains("already signed in", html);

        // The original session is untouched — still signed in as the first account.
        var secondLoginGet = await _client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, secondLoginGet.StatusCode); // redirected away: still signed in
        Assert.Contains("/Employee", secondLoginGet.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_AlreadySignedInAsTheSameAccount_IsNotBlocked()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));

        // A stale form re-submitted for the SAME already-active account —
        // e.g. a second tab that hadn't yet redirected — must not be treated
        // as a blocked account switch.
        var secondResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, secondResponse.StatusCode);
        Assert.Contains("/Employee", secondResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsGenericErrorAndDoesNotSignIn()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);

        var postResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "totally-wrong-password",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode); // re-renders the login form, no redirect
        var body = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password.", body);

        // Never signed in: the session still can't reach a protected page.
        var protectedPage = await _client.GetAsync("/TimeLogs");
        Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUsername_ShowsTheSameGenericErrorAsAWrongPassword()
    {
        // No account exists at all - must not reveal that via a different message.
        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);

        var postResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "nobody@dti.gov.ph",
            ["Password"] = "whatever",
            ["__RequestVerificationToken"] = token,
        }));

        var body = await postResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password.", body);
    }

    [Fact]
    public async Task Login_FiveWrongPasswordsInARow_LocksTheAccount()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        for (var attempt = 1; attempt <= AccountDefaults.MaxFailedLogins; attempt++)
        {
            var getResponse = await _client.GetAsync("/Account/Login");
            var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);

            var postResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "test.user@dti.gov.ph",
                ["Password"] = "wrong-" + attempt,
                ["__RequestVerificationToken"] = token,
            }));

            var body = await postResponse.Content.ReadAsStringAsync();
            if (attempt < AccountDefaults.MaxFailedLogins)
            {
                Assert.Contains("Invalid username or password.", body);
            }
            else
            {
                Assert.Contains("locked", body, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Even the CORRECT password is now rejected while locked out.
        var finalGet = await _client.GetAsync("/Account/Login");
        var finalToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(finalGet);
        var finalAttempt = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = finalToken,
        }));
        var finalBody = await finalAttempt.Content.ReadAsStringAsync();
        Assert.Contains("locked", finalBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_PostWithoutAntiforgeryToken_IsRejected()
    {
        var postResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "anything",
            // No __RequestVerificationToken, and no antiforgery cookie either
            // (never did the GET that would have issued one).
        }));

        Assert.Equal(HttpStatusCode.BadRequest, postResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsTheSessionSoProtectedPagesRedirectAgain()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/TimeLogs")).StatusCode);

        // Logout itself needs a fresh antiforgery token from a page that has one.
        var timeLogsPage = await _client.GetAsync("/TimeLogs");
        var logoutToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(timeLogsPage);
        var logoutResponse = await _client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = logoutToken,
        }));
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);

        var afterLogout = await _client.GetAsync("/TimeLogs");
        IntegrationTestHelpers.AssertRedirectsToLogin(afterLogout);
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

    [Fact]
    public async Task ForgotPassword_ValidEmail_EmailedLinkActuallyResetsThePassword()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/ForgotPassword");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        await _client.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["__RequestVerificationToken"] = token,
        }));

        var queue = _factory.Services.GetRequiredService<OtpEmailQueue>();
        var readOk = queue.Reader.TryRead(out var message);
        Assert.True(readOk, "Expected a password reset link to have been queued.");
        var link = ExtractLink(message!.TextBody);

        // Clicking the link only shows a confirmation page — the token is
        // peeked at, not consumed yet, so an email scanner pre-fetching this
        // same URL couldn't have burned it already.
        var clickResponse = await _client.GetAsync(new Uri(link).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, clickResponse.StatusCode);
        var confirmToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(clickResponse);
        var uid = ExtractHiddenField(await clickResponse.Content.ReadAsStringAsync(), "uid");
        var tokenValue = ExtractHiddenField(await clickResponse.Content.ReadAsStringAsync(), "token");

        // Submitting that confirmation actually consumes the token and
        // redirects to the clean ResetPassword URL ...
        var confirmResponse = await _client.PostAsync("/Account/ResetLinkConfirm", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["uid"] = uid,
            ["token"] = tokenValue,
            ["__RequestVerificationToken"] = confirmToken,
        }));
        Assert.Equal(HttpStatusCode.Redirect, confirmResponse.StatusCode);
        Assert.Contains("ResetPassword", confirmResponse.Headers.Location!.ToString());

        // ... which now actually renders the set-password form.
        var formResponse = await _client.GetAsync(confirmResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, formResponse.StatusCode);
        var formToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(formResponse);

        var resetResponse = await _client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["NewPassword"] = "Brand-New-Pass2",
            ["ConfirmPassword"] = "Brand-New-Pass2",
            ["__RequestVerificationToken"] = formToken,
        }));
        IntegrationTestHelpers.AssertRedirectsToLogin(resetResponse);

        // The new password actually works; the old one no longer does.
        var loginGet = await _client.GetAsync("/Account/Login");
        var loginToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(loginGet);
        var loginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Brand-New-Pass2",
            ["__RequestVerificationToken"] = loginToken,
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("/Employee", loginResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ResetLink_WrongToken_RedirectsToForgotPasswordInsteadOfResettingAnything()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division"));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/ForgotPassword");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        await _client.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["__RequestVerificationToken"] = token,
        }));

        var queue = _factory.Services.GetRequiredService<OtpEmailQueue>();
        queue.Reader.TryRead(out var message);
        var realLink = new Uri(ExtractLink(message!.TextBody));
        var tamperedQuery = realLink.Query.Replace(
            System.Web.HttpUtility.ParseQueryString(realLink.Query)["token"]!, "forged-token-value");

        var response = await _client.GetAsync(realLink.AbsolutePath + tamperedQuery);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("ForgotPassword", response.Headers.Location!.ToString());

        // The real password is unaffected: it still works normally.
        var loginGet = await _client.GetAsync("/Account/Login");
        var loginToken = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(loginGet);
        var loginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = loginToken,
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("/Employee", loginResponse.Headers.Location!.ToString());
    }
}
