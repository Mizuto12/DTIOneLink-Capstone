using System.Text.RegularExpressions;
using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services;
using DTIOneLink.Services.Email;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests.Integration;

// Regression test for a real production bug: an established account that
// was never forced through the first-sign-in VerifyEmail step (MustChangePassword
// already false, e.g. every account that predates the login-OTP feature)
// stayed EmailConfirmed=false forever despite logging in normally every day
// - silently blocking every EmailOutbox-gated notification email
// (NotificationService.CreateAsync checks EmailConfirmed) while OTP emails
// themselves kept working, since OtpEmailQueue never checks that flag.
// Needs SQLite (a real relational provider), not EF's InMemory provider:
// OneTimeCodeService.VerifyAsync relies on ExecuteUpdateAsync, which
// InMemory can't translate (see OneTimeCodeServiceTests).
public class LoginOtpEmailConfirmedTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;

    public LoginOtpEmailConfirmedTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static AccountController BuildController(AppDbContext db, OneTimeCodeService codes, out HttpContext httpContext)
    {
        var sessions = new AccountSessionService(db, new FakeWebHostEnvironment());

        httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());

        var controller = new AccountController(NullLogger<AccountController>.Instance, db, codes, sessions, new FakeWebHostEnvironment())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
            Url = new FakeUrlHelper(),
        };
        return controller;
    }

    [Fact]
    public async Task VerifyLoginCode_CorrectCode_SetsEmailConfirmedTrueForAnAlreadyEstablishedAccount()
    {
        var queue = new OtpEmailQueue();
        var user = new User
        {
            Username = "established.admin@dti.gov.ph",
            Email = "established.admin@dti.gov.ph",
            FullName = "Established Admin",
            Role = "Admin",
            Department = "Business Development Division",
            IsActive = true,
            // The exact real-world state found in production: logs in fine,
            // but was never routed through VerifyEmail, so this is stuck false.
            EmailConfirmed = false,
            MustChangePassword = false,
            SecurityStamp = "stamp-1",
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var codes = new OneTimeCodeService(_db, new OneTimeCodeHasher(), queue, NullLogger<OneTimeCodeService>.Instance);
        var controller = BuildController(_db, codes, out var httpContext);

        // Simulate having already passed the password check in Login POST.
        httpContext.Session.SetInt32("PendingLoginUserId", user.Id);
        httpContext.Session.SetString("PendingLoginStamp", user.SecurityStamp);

        var issueResult = await codes.IssueAsync(user, OneTimeCodePurpose.Login);
        Assert.Equal(CodeIssueStatus.Sent, issueResult.Status);

        queue.Reader.TryRead(out var message);
        var code = Regex.Match(message!.TextBody, @"Your code: (\d{6})").Groups[1].Value;
        Assert.Equal(6, code.Length);

        var result = await controller.VerifyLoginCode(new EnterCodeViewModel { Code = code });

        Assert.IsType<RedirectResult>(result);
        var saved = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.True(saved.EmailConfirmed);
    }
}
