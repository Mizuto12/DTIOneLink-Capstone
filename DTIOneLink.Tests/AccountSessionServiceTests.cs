using DTIOneLink.Controllers;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace DTIOneLink.Tests;

// Covers AccountSessionService's two jobs: EstablishSession's session keys,
// and the trusted-device cookie used to skip the login-OTP step. Both run
// entirely off a User entity already in memory (plus, for the cookie, plain
// tracked-entity SaveChangesAsync) — no ExecuteUpdateAsync anywhere in this
// service, so unlike OneTimeCodeServiceTests there is no InMemory-provider
// limitation to document here.
public class AccountSessionServiceTests
{
    private static AccountSessionService BuildService(DTIOneLink.Data.AppDbContext db, string environmentName = "Development") =>
        new(db, new FakeWebHostEnvironment { EnvironmentName = environmentName });

    private static HttpContext BuildHttpContext(string? trustedDeviceCookie = null, bool isHttps = true)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Request.Scheme = isHttps ? "https" : "http";
        if (trustedDeviceCookie != null)
        {
            httpContext.Request.Headers.Append("Cookie", $"{AccountSessionService.CookieName}={trustedDeviceCookie}");
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
    public async Task IssueTrustedDeviceCookieAsync_WritesAHashedTokenAndACookieNamingTheUser()
    {
        var db = InMemoryDbContextFactory.Create();
        db.Users.Add(NewActiveUser());
        await db.SaveChangesAsync();
        var user = db.Users.Single();
        var service = BuildService(db);
        var httpContext = BuildHttpContext();

        await service.IssueTrustedDeviceCookieAsync(httpContext, user);

        Assert.NotNull(user.RememberTokenHash);
        Assert.NotEmpty(user.RememberTokenHash);
        Assert.NotNull(user.RememberTokenExpiresUtc);
        Assert.True(user.RememberTokenExpiresUtc > DateTime.UtcNow.AddHours(11));
        Assert.True(user.RememberTokenExpiresUtc <= DateTime.UtcNow.AddHours(12));

        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains($"{AccountSessionService.CookieName}={user.Id}.", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasTrustedDevice_NoCookiePresent_ReturnsFalse()
    {
        var service = BuildService(InMemoryDbContextFactory.Create());
        var httpContext = BuildHttpContext(trustedDeviceCookie: null);
        var user = NewActiveUser();
        user.Id = 1;

        Assert.False(service.HasTrustedDevice(httpContext, user));
    }

    [Fact]
    public void HasTrustedDevice_MalformedCookieValue_ReturnsFalse()
    {
        var service = BuildService(InMemoryDbContextFactory.Create());
        var httpContext = BuildHttpContext(trustedDeviceCookie: "not-a-valid-format");
        var user = NewActiveUser();
        user.Id = 1;

        Assert.False(service.HasTrustedDevice(httpContext, user));
    }

    [Fact]
    public async Task HasTrustedDevice_CookieNamesADifferentUser_ReturnsFalse()
    {
        var db = InMemoryDbContextFactory.Create();
        db.Users.Add(NewActiveUser());
        await db.SaveChangesAsync();
        var user = db.Users.Single();
        var service = BuildService(db);

        // A real, valid cookie for this user ...
        var issuingContext = BuildHttpContext();
        await service.IssueTrustedDeviceCookieAsync(issuingContext, user);
        var setCookie = issuingContext.Response.Headers.SetCookie.ToString();
        var rawToken = setCookie.Split($"{AccountSessionService.CookieName}={user.Id}.")[1].Split(';')[0];

        // ... presented while checking a DIFFERENT user's account.
        var otherUser = NewActiveUser();
        otherUser.Id = user.Id + 1;
        var checkContext = BuildHttpContext(trustedDeviceCookie: $"{user.Id}.{rawToken}");

        Assert.False(service.HasTrustedDevice(checkContext, otherUser));
    }

    [Fact]
    public async Task HasTrustedDevice_ValidFreshlyIssuedToken_ReturnsTrue()
    {
        var db = InMemoryDbContextFactory.Create();
        db.Users.Add(NewActiveUser());
        await db.SaveChangesAsync();
        var user = db.Users.Single();
        var service = BuildService(db);

        var issuingContext = BuildHttpContext();
        await service.IssueTrustedDeviceCookieAsync(issuingContext, user);
        var setCookie = issuingContext.Response.Headers.SetCookie.ToString();
        var rawToken = setCookie.Split($"{AccountSessionService.CookieName}={user.Id}.")[1].Split(';')[0];

        var checkContext = BuildHttpContext(trustedDeviceCookie: $"{user.Id}.{rawToken}");

        Assert.True(service.HasTrustedDevice(checkContext, user));
    }

    [Fact]
    public void HasTrustedDevice_WrongToken_ReturnsFalse()
    {
        var user = NewActiveUser();
        user.Id = 1;
        user.RememberTokenHash = "irrelevant-since-it-will-never-match";
        user.RememberTokenExpiresUtc = DateTime.UtcNow.AddHours(1);
        var service = BuildService(InMemoryDbContextFactory.Create());
        var httpContext = BuildHttpContext(trustedDeviceCookie: $"{user.Id}.wrong-token-entirely");

        Assert.False(service.HasTrustedDevice(httpContext, user));
    }

    [Fact]
    public void HasTrustedDevice_ExpiredToken_ReturnsFalse()
    {
        var user = NewActiveUser();
        user.Id = 1;
        user.RememberTokenHash = "irrelevant-since-expiry-is-checked-first";
        user.RememberTokenExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        var service = BuildService(InMemoryDbContextFactory.Create());
        var httpContext = BuildHttpContext(trustedDeviceCookie: $"{user.Id}.anything");

        Assert.False(service.HasTrustedDevice(httpContext, user));
    }

    [Fact]
    public void HasTrustedDevice_NoStoredToken_ReturnsFalse()
    {
        var user = NewActiveUser();
        user.Id = 1;
        var service = BuildService(InMemoryDbContextFactory.Create());
        var httpContext = BuildHttpContext(trustedDeviceCookie: $"{user.Id}.anything");

        Assert.False(service.HasTrustedDevice(httpContext, user));
    }
}
