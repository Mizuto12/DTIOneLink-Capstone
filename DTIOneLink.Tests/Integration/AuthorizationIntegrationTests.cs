using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DTIOneLink.Tests.Integration;

// Real-HTTP regression coverage for RequirePermissionAttribute: an
// Employee must be refused by the filter itself, before the action body
// (and in UserManagementController's case, before any database call) ever
// runs. Complements the direct-call filter unit tests by proving the same
// rule holds through the actual middleware pipeline and routing.
public class AuthorizationIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public AuthorizationIntegrationTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task LogInAsAsync(string role, string department)
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser(role, department));
            await db.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        var loginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "test.user@dti.gov.ph",
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode); // sanity: login actually succeeded
    }

    [Fact]
    public async Task Employee_RequestingUserManagement_Gets403NotTheAdminPage()
    {
        await LogInAsAsync("Employee", "Business Development Division");

        var response = await _client.GetAsync("/UserManagement");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_RequestingUserManagement_PassesTheGate()
    {
        // The gate (RequirePermissionAttribute) is satisfied; what happens
        // next is raw ADO.NET against a deliberately unreachable
        // connection string (see CustomWebApplicationFactory). TestServer
        // may surface that failure either as a thrown exception or as an
        // HTTP 500 response - both mean "got past authorization, failed at
        // the database", which is what this test wants to prove. Only a
        // 403/Forbidden would mean the gate wrongly blocked an Admin; that
        // is the regression this guards against.
        await LogInAsAsync("Admin", "Business Development Division");

        HttpResponseMessage? response = null;
        var thrown = await Record.ExceptionAsync(async () => response = await _client.GetAsync("/UserManagement"));

        if (thrown == null)
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, response!.StatusCode);
        }
    }

    [Fact]
    public async Task Employee_PostingUserManagementCreate_Gets403BeforeAnyRoleCanBeGranted()
    {
        await LogInAsAsync("Employee", "Business Development Division");

        // Even with a well-formed antiforgery token, the permission filter
        // runs first and must refuse this before the privilege-escalation
        // guard inside Create() - or the database - is ever reached.
        var somePage = await _client.GetAsync("/Dashboard/AdminDashboard"); // any page on this session to harvest a token from
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(somePage);

        var response = await _client.PostAsync("/UserManagement/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "New Guy",
            ["Email"] = "new.guy@dti.gov.ph",
            ["Department"] = "Business Development Division",
            ["Role"] = "SuperAdmin",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NotLoggedIn_RequestingUserManagement_RedirectsToLoginNot403()
    {
        // No session at all: RequirePermissionAttribute's first check
        // (logged in?) must fire before its second (has the permission?),
        // so this is a redirect, not a 403.
        var response = await _client.GetAsync("/UserManagement");

        IntegrationTestHelpers.AssertRedirectsToLogin(response);
    }
}
