using DTIOneLink.Controllers;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Regression tests for the privilege-escalation fix: Create() must refuse to
// grant Admin/SuperAdmin to anyone unless the caller is already a
// SuperAdmin. Before the fix, any Admin could post Role=SuperAdmin and
// create an office-wide account for themselves (see ChangeStanding, which
// already had the equivalent guard that Create was missing).
public class UserManagementControllerTests
{
    // Deliberately unreachable: proves the guard rejects the request before
    // ever touching the database. If the guard regressed and let the call
    // through, the test would fail with a connection error instead of the
    // expected redirect+message, not pass by accident.
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,1;Database=unreachable;Connect Timeout=1;TrustServerCertificate=True;";

    private static UserManagementController BuildController(string? callerRole, string? callerDepartment = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = UnreachableConnectionString
            })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (callerRole != null)
        {
            httpContext.Session.SetString("UserRole", callerRole);
        }
        if (callerDepartment != null)
        {
            httpContext.Session.SetString("UserDepartment", callerDepartment);
        }

        var controller = new UserManagementController(new DatabaseHelper(config), NullLogger<UserManagementController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
        return controller;
    }

    private static UserItem ValidNewUser(string role) => new()
    {
        FullName = "Test User",
        Email = "test.user@dti.gov.ph",
        Department = UserManagementController.Divisions[0],
        Role = role,
    };

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")] // case-insensitive caller-role check
    [InlineData("Employee")]
    [InlineData(null)] // no session role at all
    public async Task Create_NonSuperAdminRequestingSuperAdmin_IsRejectedBeforeTouchingTheDatabase(string? callerRole)
    {
        var controller = BuildController(callerRole);

        var result = await controller.Create(ValidNewUser("SuperAdmin"));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UserManagementController.Index), redirect.ActionName);
        Assert.Equal("Only the OPD can create Admin or SuperAdmin accounts.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Create_AdminRequestingAdminRole_IsAlsoRejected()
    {
        // The bug wasn't SuperAdmin-only: an Admin could also just as easily
        // have granted *another* Admin account they didn't own.
        var controller = BuildController("Admin");

        var result = await controller.Create(ValidNewUser("Admin"));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UserManagementController.Index), redirect.ActionName);
        Assert.Equal("Only the OPD can create Admin or SuperAdmin accounts.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Create_AdminRequestingEmployeeRole_PassesTheGuard()
    {
        // Admins creating plain Employee accounts is the one case that must
        // keep working - this is the controller's everyday job. Department
        // must match the new employee's (Admins only add within their own
        // division - see the division guard added alongside this test).
        var controller = BuildController("Admin", UserManagementController.Divisions[0]);

        // No real DB is reachable, so a successful pass through the guard
        // shows up as a DB connection failure, not as the "Only the OPD..."
        // rejection - i.e. the method got past authorization and started
        // doing real work.
        await Assert.ThrowsAsync<SqlException>(() => controller.Create(ValidNewUser("Employee")));
        Assert.False(controller.TempData.ContainsKey("ErrorMessage")
            && Equals(controller.TempData["ErrorMessage"], "Only the OPD can create Admin or SuperAdmin accounts."));
    }

    [Fact]
    public async Task Create_SuperAdminRequestingSuperAdminRole_PassesTheGuard()
    {
        var controller = BuildController("SuperAdmin");

        await Assert.ThrowsAsync<SqlException>(() => controller.Create(ValidNewUser("SuperAdmin")));
    }

    [Fact]
    public async Task Create_AdminRequestingDifferentDivision_IsRejectedBeforeTouchingTheDatabase()
    {
        // An Admin only manages their own division - posting any other
        // division's name (even for a plain Employee) must be refused, not
        // silently honored.
        var controller = BuildController("Admin", UserManagementController.Divisions[0]);
        var newUser = ValidNewUser("Employee");
        newUser.Department = UserManagementController.Divisions[1];

        var result = await controller.Create(newUser);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UserManagementController.Index), redirect.ActionName);
        Assert.Equal("You can only add employees to your own division.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Create_SuperAdminRequestingAnyDivision_PassesTheGuard()
    {
        // SuperAdmin (the OPD) is office-wide and must not be restricted to
        // one division the way a plain Admin is.
        var controller = BuildController("SuperAdmin", UserManagementController.Divisions[0]);
        var newUser = ValidNewUser("Employee");
        newUser.Department = UserManagementController.Divisions[1];

        await Assert.ThrowsAsync<SqlException>(() => controller.Create(newUser));
    }
}
