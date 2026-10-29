using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Regression coverage for two 2026-10-29 audit findings on TasksController.Create:
// Priority had no server-side allow-list check (any non-empty string was
// accepted and persisted — confirmed live with "ultra-mega-urgent"), and
// DueDate had no past-date check at all, for any role.
public class TasksControllerCreateValidationTests
{
    private static (AppDbContext Context, User Admin) SeedAdmin()
    {
        var context = InMemoryDbContextFactory.Create();
        var admin = new User
        {
            Username = "admin@dti.gov.ph",
            Email = "admin@dti.gov.ph",
            FullName = "Test Admin",
            Role = "Admin",
            Department = TargetDepartments.BDD,
            IsActive = true,
            EmailConfirmed = true,
            SecurityStamp = "stamp",
        };
        context.Users.Add(admin);
        context.SaveChanges();
        return (context, admin);
    }

    private static TasksController BuildController(AppDbContext context)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", "SuperAdmin");
        httpContext.Session.SetInt32("UserId", 99);

        var config = new ConfigurationBuilder().Build();
        var notifications = new NotificationService(context, config, NullLogger<NotificationService>.Instance);
        var taskAssignments = new TaskAssignmentService(context);
        var opdTasks = new OpdTaskService(context, taskAssignments, notifications);

        return new TasksController(context, notifications, taskAssignments, opdTasks, NullLogger<TasksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
    }

    private static TaskCreateViewModel ValidModel(User admin) => new()
    {
        TaskName = "Quarterly Report",
        Description = "Prepare the quarterly report.",
        TaskType = TaskTypes.DirectAdmin,
        OwningDepartment = TargetDepartments.BDD,
        ResponsibleAdminUserId = admin.Id,
        DueDate = DateTime.UtcNow.AddDays(5),
        Priority = "medium",
    };

    [Fact]
    public async Task Create_InvalidPriority_IsRejectedAndNothingSaved()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);
        model.Priority = "ultra-mega-urgent";

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Fact]
    public async Task Create_PastDueDate_IsRejectedAndNothingSaved()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);
        model.DueDate = DateTime.UtcNow.AddDays(-5);

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Fact]
    public async Task Create_ValidPriorityAndFutureDueDate_Succeeds()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(await context.TaskItems.AnyAsync());
    }
}
