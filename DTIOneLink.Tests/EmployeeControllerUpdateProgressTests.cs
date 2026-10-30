using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Regression coverage for EmployeeController.UpdateProgress after T10
// (2026-10-31) extracted its lock/status-transition logic into the shared
// TaskAssignmentService.ApplyProgressChangeAsync — confirms the assignee's
// own slider still behaves exactly as before the refactor.
public class EmployeeControllerUpdateProgressTests
{
    private static (AppDbContext Context, TaskItem Task, TaskAssignment Assignment, User Employee) Seed(string status, int progress = 0)
    {
        var context = InMemoryDbContextFactory.Create();
        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        context.SaveChanges();

        var task = new TaskItem { TaskName = "Task", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(task);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = status, Progress = progress };
        context.TaskAssignments.Add(assignment);
        context.SaveChanges();

        return (context, task, assignment, employee);
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
    public async Task InProgress_UpdatesSuccessfully()
    {
        var (context, task, _, employee) = Seed(TaskWorkflow.InProgress, 20);
        var controller = BuildController(context, employee.Id);

        var result = await controller.UpdateProgress(task.Id, 55);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(55, context.TaskAssignments.Single().Progress);
    }

    [Fact]
    public async Task ReturnedForCorrection_StillUpdatesSuccessfully()
    {
        var (context, task, _, employee) = Seed(TaskWorkflow.ReturnedForCorrection, 40);
        var controller = BuildController(context, employee.Id);

        var result = await controller.UpdateProgress(task.Id, 60);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(60, context.TaskAssignments.Single().Progress);
    }

    [Fact]
    public async Task ForReview_IsStillLocked()
    {
        var (context, task, _, employee) = Seed(TaskWorkflow.ForReview, 100);
        var controller = BuildController(context, employee.Id);

        var result = await controller.UpdateProgress(task.Id, 50);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(100, context.TaskAssignments.Single().Progress);
    }
}
