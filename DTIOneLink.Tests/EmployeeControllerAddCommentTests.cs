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

// Validation-audit coverage (2026-10-30): EmployeeController.AddComment binds
// `text` as a raw action parameter, not through a ViewModel, so
// TaskComment's [StringLength(1000)] was never actually enforced before a
// save — an overlong comment relied entirely on the DB column to reject it.
// This is the single shared endpoint for both the Employee and Admin comment
// forms (see TasksController.cs:703-706), so one fix/test covers both.
public class EmployeeControllerAddCommentTests
{
    private static (AppDbContext Context, TaskItem Task, User Employee) Seed()
    {
        var context = InMemoryDbContextFactory.Create();
        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        var task = new TaskItem { TaskName = "Task", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(task);
        context.SaveChanges();

        context.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = TaskWorkflow.InProgress });
        context.SaveChanges();

        return (context, task, employee);
    }

    private static EmployeeController BuildController(AppDbContext context, int userId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", "Employee");
        httpContext.Session.SetInt32("UserId", userId);

        var config = new ConfigurationBuilder().Build();
        return new EmployeeController(
            context,
            new TaskAssignmentService(context),
            new NotificationService(context, config, NullLogger<NotificationService>.Instance),
            NullLogger<EmployeeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
    }

    [Fact]
    public async Task AddComment_Over1000Characters_IsRejectedAndNothingSaved()
    {
        var (context, task, employee) = Seed();
        var controller = BuildController(context, employee.Id);

        var result = await controller.AddComment(task.Id, new string('a', 1001));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(EmployeeController.Details), redirect.ActionName);
        Assert.Equal("Comment is too long (maximum 1000 characters).", controller.TempData["ErrorMessage"]);
        Assert.Empty(await context.TaskComments.ToListAsync());
    }

    [Fact]
    public async Task AddComment_Exactly1000Characters_Succeeds()
    {
        var (context, task, employee) = Seed();
        var controller = BuildController(context, employee.Id);

        var result = await controller.AddComment(task.Id, new string('a', 1000));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(await context.TaskComments.ToListAsync());
    }

    [Fact]
    public async Task AddComment_WhitespaceOnly_IsStillRejected()
    {
        // Pre-existing behavior (not part of this audit fix) — confirms the
        // new length check didn't disturb the existing emptiness check.
        var (context, task, employee) = Seed();
        var controller = BuildController(context, employee.Id);

        var result = await controller.AddComment(task.Id, "   ");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Comment can't be empty.", controller.TempData["ErrorMessage"]);
        Assert.Empty(await context.TaskComments.ToListAsync());
    }
}
