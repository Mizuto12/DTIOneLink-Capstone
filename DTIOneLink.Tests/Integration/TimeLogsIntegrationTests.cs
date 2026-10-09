using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DTIOneLink.Tests.Integration;

// Real-HTTP, real-EF-database coverage for TimeLogsController - this one
// never touches raw ADO.NET, so unlike the UserManagement tests, this can
// exercise a genuine end-to-end success path, not just "the guard fired
// before the database".
public class TimeLogsIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public TimeLogsIntegrationTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<int> SeedUserAndLogInAsync(string username)
    {
        int userId;
        using (var db = _factory.CreateDbContext())
        {
            var user = IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division", username: username);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var getResponse = await _client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        var loginResponse = await _client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        return userId;
    }

    [Fact]
    public async Task TimeLogs_NewlySignedInUser_ShowsTheSignInItJustCreated()
    {
        // AccountController.SignIn() writes a TimeLog row with no
        // TimeOutUtc for every successful login - this proves that row
        // really lands in the database and really shows up on the page.
        await SeedUserAndLogInAsync("alice@dti.gov.ph");

        var page = await _client.GetAsync("/TimeLogs");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var body = await page.Content.ReadAsStringAsync();
        Assert.Contains("Still signed in", body);
        Assert.DoesNotContain("No time logs yet.", body);
    }

    [Fact]
    public async Task TimeLogs_OnlyShowsTheCallersOwnLogs_NeverAnotherUsers()
    {
        await SeedUserAndLogInAsync("alice@dti.gov.ph");

        // Bob's log gets a deliberately distinctive, unmistakable date far
        // from "today" - if it ever leaked into Alice's page, this date
        // string would be the tell.
        var bobsDistinctiveDate = new DateTime(2020, 1, 15, 3, 0, 0, DateTimeKind.Utc);
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(IntegrationTestHelpers.NewActiveUser("Employee", "Business Development Division", username: "bob@dti.gov.ph"));
            await db.SaveChangesAsync();
            var bob = db.Users.First(u => u.Username == "bob@dti.gov.ph");
            db.TimeLogs.Add(new DTIOneLink.Models.TimeLog { UserId = bob.Id, TimeInUtc = bobsDistinctiveDate, TimeOutUtc = bobsDistinctiveDate.AddHours(8) });
            await db.SaveChangesAsync();
        }

        var page = await _client.GetAsync("/TimeLogs");
        var body = await page.Content.ReadAsStringAsync();

        // Confirm Bob's row genuinely exists (so a false pass can't be
        // explained by the seed silently failing), then confirm it's
        // nowhere on Alice's page.
        using (var db = _factory.CreateDbContext())
        {
            Assert.Contains(db.TimeLogs, t => t.TimeInUtc == bobsDistinctiveDate);
        }
        Assert.DoesNotContain("Jan 15, 2020", body);
    }

    [Fact]
    public async Task TimeLogs_NoLogsYet_ShowsTheEmptyState()
    {
        // A user who hasn't signed in via AccountController.SignIn at all
        // (can't happen through the real login flow, but TimeLogsController
        // itself must still handle zero rows cleanly) - verified by reading
        // the page immediately after seeding a user directly into the DB
        // and signing in through a stub session instead of full login.
        await SeedUserAndLogInAsync("carol@dti.gov.ph");

        using (var db = _factory.CreateDbContext())
        {
            // Remove the TimeLog row that the real login just created, to
            // reach the genuinely-empty case.
            var logs = db.TimeLogs.ToList();
            db.TimeLogs.RemoveRange(logs);
            await db.SaveChangesAsync();
        }

        var page = await _client.GetAsync("/TimeLogs");
        var body = await page.Content.ReadAsStringAsync();

        Assert.Contains("No time logs yet.", body);
    }
}
