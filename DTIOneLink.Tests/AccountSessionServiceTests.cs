using DTIOneLink.Controllers;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Covers AccountSessionService's cookie-parsing/validation branches, all of
// which return before touching the database — IssueRememberMeCookieAsync and
// every TryAutoSignInAsync rejection path use plain tracked-entity
// SaveChangesAsync, which EF Core's InMemory provider supports fine. The one
// path InMemory can't run is TryAutoSignInAsync's *success* case, which also
// closes out a stale TimeLog row via ExecuteUpdateAsync — the same
// InMemory/ExecuteUpdateAsync limitation OneTimeCodeServiceTests documents
// for IssueAsync/VerifyAsync, so it isn't covered here either.
public class AccountSessionServiceTests
{
    private static AccountSessionService BuildService(DTIOneLink.Data.AppDbContext db, string environmentName = "Development") =>
        new(db, new FakeWebHostEnvironment { EnvironmentName = environmentName }, NullLogger<AccountSessionService>.Instance);

    private static HttpContext BuildHttpContext(string? rememberMeCookie = null, bool isHttps = true)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Request.Scheme = isHttps ? "https" : "http";
        if (rememberMeCookie != null)
        {
            httpContext.Request.Headers.Append("Cookie", $"{AccountSessionService.CookieName}={rememberMeCookie}");
        }
        return httpContext;
    }

    private static User NewActiveUser(string securityStamp = "stamp-1") => new()
    {
        Username = "test.user@dti.gov.ph",
        Email = "test.user@dti.gov.ph",
        FullName = "Test User",
        Role = "Employee",
        Department = "BDD",
        IsActive = true,
        EmailConfirmed = true,
        SecurityStamp = securityStamp,
    };

    [Fact]
    public void EstablishSession_SetsEveryKeyOtherControllersAndViewsRelyOn()
    {
        var db = InMemoryDbContextFactory.Create();
        var service = BuildService(db);
        var httpContext = BuildHttpContext();
        var user = NewActiveUser();
        user.Id = 42;

        service.EstablishSession(httpContext, user);

        Assert.Equal(42, httpContext.Session.GetInt32("UserId"));
        Assert.Equal("Employee", httpContext.Session.GetString("UserRole"));
        Assert.Equal("Test User", httpContext.Session.GetString("Username"));
        Assert.Equal("Test User", httpContext.Session.GetString("FullName"));
        Assert.Equal("test.user@dti.gov.ph", httpContext.Session.GetString("LoginUsername"));
        Assert.Equal("test.user@dti.gov.ph", httpContext.Session.GetString("UserEmail"));
        Assert.Equal("BDD", httpContext.Session.GetString("UserDepartment"));
        Assert.Equal("stamp-1", httpContext.Session.GetString(AccountController.SecurityStampKey));
    }

    [Fact]
    public void EstablishSession_NullDepartment_StoresEmptyStringNotNull()
    {
        // Program.cs's per-request refresh compares against (current.Department
        // ?? string.Empty) - storing a literal null here would make that
        // comparison always look "changed" every single request.
        var db = InMemoryDbContextFactory.Create();
        var service = BuildService(db);
        var httpContext = BuildHttpContext();
        var user = NewActiveUser();
        user.Department = null!;

        service.EstablishSession(httpContext, user);

        Assert.Equal(string.Empty, httpContext.Session.GetString("UserDepartment"));
    }

    [Fact]
    public async Task IssueRememberMeCookieAsync_WritesAHashedTokenAndACookieNamingTheUser()
    {
        var db = InMemoryDbContextFactory.Create();
        db.Users.Add(NewActiveUser());
        await db.SaveChangesAsync();
        var user = db.Users.Single();
        var service = BuildService(db);
        var httpContext = BuildHttpContext();

        await service.IssueRememberMeCookieAsync(httpContext, user);

        Assert.NotNull(user.RememberTokenHash);
        Assert.NotEmpty(user.RememberTokenHash);
        Assert.NotNull(user.RememberTokenExpiresUtc);
        Assert.True(user.RememberTokenExpiresUtc > DateTime.UtcNow.AddDays(29));

        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains($"{AccountSessionService.CookieName}={user.Id}.", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryAutoSignInAsync_NoCookiePresent_ReturnsFalse()
    {
        var db = InMemoryDbContextFactory.Create();
        var service = BuildService(db);
        var httpContext = BuildHttpContext(rememberMeCookie: null);

        var result = await service.TryAutoSignInAsync(httpContext);

        Assert.False(result);
        Assert.Null(httpContext.Session.GetInt32("UserId"));
    }

    [Fact]
    public async Task TryAutoSignInAsync_MalformedCookieValue_ReturnsFalseAndDeletesCookie()
    {
        var db = InMemoryDbContextFactory.Create();
        var service = BuildService(db);
        var httpContext = BuildHttpContext(rememberMeCookie: "not-a-valid-format");

        var result = await service.TryAutoSignInAsync(httpContext);

        Assert.False(result);
        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains($"{AccountSessionService.CookieName}=", setCookie);
        // A deleted cookie is re-sent with an already-expired date.
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryAutoSignInAsync_UnknownUserId_ReturnsFalse()
    {
        var db = InMemoryDbContextFactory.Create();
        var service = BuildService(db);
        var httpContext = BuildHttpContext(rememberMeCookie: "999.someRawTokenValue");

        var result = await service.TryAutoSignInAsync(httpContext);

        Assert.False(result);
    }

    [Fact]
    public async Task TryAutoSignInAsync_WrongToken_ReturnsFalseAndClearsStoredHash()
    {
        var db = InMemoryDbContextFactory.Create();
        db.Users.Add(NewActiveUser());
        await db.SaveChangesAsync();
        var user = db.Users.Single();
        var service = BuildService(db);

        // Issue a real token for this user, then present a different one.
        await service.IssueRememberMeCookieAsync(BuildHttpContext(), user);
        var httpContext = BuildHttpContext(rememberMeCookie: $"{user.Id}.wrong-token-entirely");

        var result = await service.TryAutoSignInAsync(httpContext);

        Assert.False(result);
        Assert.Null(user.RememberTokenHash);
        Assert.Null(user.RememberTokenExpiresUtc);
    }

    [Fact]
    public async Task TryAutoSignInAsync_ExpiredToken_ReturnsFalse()
    {
        var db = InMemoryDbContextFactory.Create();
        var user = NewActiveUser();
        user.RememberTokenHash = "irrelevant-since-expiry-is-checked-first";
        user.RememberTokenExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var httpContext = BuildHttpContext(rememberMeCookie: $"{user.Id}.anything");

        var result = await service.TryAutoSignInAsync(httpContext);

        Assert.False(result);
    }

    [Fact]
    public async Task TryAutoSignInAsync_InactiveAccount_ReturnsFalse()
    {
        var db = InMemoryDbContextFactory.Create();
        var user = NewActiveUser();
        user.IsActive = false;
        user.RememberTokenHash = "irrelevant";
        user.RememberTokenExpiresUtc = DateTime.UtcNow.AddDays(10);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var httpContext = BuildHttpContext(rememberMeCookie: $"{user.Id}.anything");

        var result = await service.TryAutoSignInAsync(httpContext);

        Assert.False(result);
    }

    [Fact]
    public async Task RevokeRememberMeAsync_DeletesTheCookie()
    {
        var db = InMemoryDbContextFactory.Create();
        db.Users.Add(NewActiveUser());
        await db.SaveChangesAsync();
        var user = db.Users.Single();
        var service = BuildService(db);
        var httpContext = BuildHttpContext();

        // RevokeRememberMeAsync's DB write uses ExecuteUpdateAsync (not
        // supported by the InMemory provider), but the cookie deletion itself
        // happens unconditionally and is what this test checks; a real SQL
        // Server is what actually exercises the DB half, same limitation as
        // TryAutoSignInAsync's success path.
        try { await service.RevokeRememberMeAsync(httpContext, user.Id); } catch (InvalidOperationException) { }

        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains($"{AccountSessionService.CookieName}=", setCookie);
    }
}
