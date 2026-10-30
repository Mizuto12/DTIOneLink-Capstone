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
        var notifications = new NotificationService(context, config, NullLogger<NotificationService>.Instance);
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
            DirectAdminUserIds = new List<int> { admin.Id },
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

    // T4 (capstone revision list): a SuperAdmin must be able to hand a
    // Direct Admin Task to more than one Admin at once, including Admins
    // from different departments — that cross-department reach is the
    // actual point of letting more than one be picked, so this deliberately
    // seeds the two Admins in different departments rather than the same
    // one.
    [Fact]
    public async Task Create_DirectAdminTask_MultipleAdmins_EachGetsOwnAssignmentAndNotification()
    {
        var (context, firstAdmin) = SeedAdmin();
        var secondAdmin = new User
        {
            Username = "admin2@dti.gov.ph",
            Email = "admin2@dti.gov.ph",
            FullName = "Second Admin",
            Role = "Admin",
            Department = TargetDepartments.CPD,
            IsActive = true,
            EmailConfirmed = true,
            SecurityStamp = "stamp2",
        };
        context.Users.Add(secondAdmin);
        context.SaveChanges();

        var controller = BuildController(context);
        var model = new TaskCreateViewModel
        {
            TaskName = "Multi-Admin Directive",
            Description = "Given to more than one Admin at once.",
            TaskType = TaskTypes.DirectAdmin,
            DirectAdminUserIds = new List<int> { firstAdmin.Id, secondAdmin.Id },
            DueDate = DateTime.UtcNow.AddDays(5),
            Priority = "medium",
        };

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        var task = await context.TaskItems.SingleAsync();
        var assignedUserIds = await context.TaskAssignments
            .Where(a => a.TaskId == task.Id).Select(a => a.UserId).Distinct().ToListAsync();
        Assert.Equal(2, assignedUserIds.Count);
        Assert.Contains(firstAdmin.Id, assignedUserIds);
        Assert.Contains(secondAdmin.Id, assignedUserIds);
        // Primary/OwningDepartment come from whichever Admin was picked
        // first, so every existing single-admin scoping check still works.
        Assert.Equal(firstAdmin.Id, task.ResponsibleAdminUserId);
        Assert.Equal(firstAdmin.Department, task.OwningDepartment);

        Assert.NotNull(await context.Notifications.SingleOrDefaultAsync(n => n.RecipientUserId == firstAdmin.Id));
        Assert.NotNull(await context.Notifications.SingleOrDefaultAsync(n => n.RecipientUserId == secondAdmin.Id));
    }

    [Fact]
    public async Task Create_DirectAdminTask_NoAdminsSelected_IsRejectedAndNothingSaved()
    {
        var (context, _) = SeedAdmin();
        var controller = BuildController(context);
        var model = new TaskCreateViewModel
        {
            TaskName = "Unassigned Directive",
            Description = "No Admins picked.",
            TaskType = TaskTypes.DirectAdmin,
            DirectAdminUserIds = new List<int>(),
            DueDate = DateTime.UtcNow.AddDays(5),
            Priority = "medium",
        };

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
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
