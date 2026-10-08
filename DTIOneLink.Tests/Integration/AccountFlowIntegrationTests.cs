using System.Net;
using DTIOneLink.Security;
using Microsoft.AspNetCore.Mvc.Testing;

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
            ["Username"] = "test.user@dti.gov.ph",
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
            ["Username"] = "test.user@dti.gov.ph",
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
            ["Username"] = "nobody@dti.gov.ph",
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
                ["Username"] = "test.user@dti.gov.ph",
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
            ["Username"] = "test.user@dti.gov.ph",
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
            ["Username"] = "test.user@dti.gov.ph",
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
            ["Username"] = "test.user@dti.gov.ph",
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
}
