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

// Diagnoses a reported bug: an Admin gets no in-app notification (and no
// email) when OPD gives them a Direct Admin Task or a Department Directive.
// Drives the real TasksController.Create action end-to-end against an
// in-memory database, the same way a real HTTP POST would, to see whether
// the Notification row actually gets created — proving or disproving the
// code path without needing production access.
public class TasksControllerCreateNotificationTests
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
        var notifications = new NotificationService(context, config);
        var taskAssignments = new TaskAssignmentService(context);
        var opdTasks = new OpdTaskService(context, taskAssignments, notifications);

        return new TasksController(context, notifications, taskAssignments, opdTasks, NullLogger<TasksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
    }

    [Fact]
    public async Task Create_DirectAdminTask_NotifiesTheResponsibleAdmin()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);

        var model = new TaskCreateViewModel
        {
            TaskName = "Quarterly Report",
            Description = "Prepare the quarterly report.",
            TaskType = TaskTypes.DirectAdmin,
            OwningDepartment = TargetDepartments.BDD,
            ResponsibleAdminUserId = admin.Id,
            DueDate = DateTime.UtcNow.AddDays(5),
            Priority = "medium",
        };

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        var notif = await context.Notifications.SingleOrDefaultAsync(n => n.RecipientUserId == admin.Id);
        Assert.NotNull(notif);
        Assert.Equal(NotificationType.Task, notif!.Type);
        Assert.Contains("Quarterly Report", notif.Message);
    }

    [Fact]
    public async Task Create_DepartmentDirective_NotifiesTheResponsibleAdmin()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);

        var model = new TaskCreateViewModel
        {
            TaskName = "Monthly Compliance Directive",
            Description = "Distribute to staff.",
            TaskType = TaskTypes.DepartmentDirective,
            OwningDepartment = TargetDepartments.BDD,
            ResponsibleAdminUserId = admin.Id,
            SubtaskNames = new List<string> { "Part A", "Part B" },
            DueDate = DateTime.UtcNow.AddDays(5),
            Priority = "medium",
        };

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        var notif = await context.Notifications.SingleOrDefaultAsync(n => n.RecipientUserId == admin.Id);
        Assert.NotNull(notif);
        Assert.Equal(NotificationType.Task, notif!.Type);
        Assert.Contains("Monthly Compliance Directive", notif.Message);
    }
}
