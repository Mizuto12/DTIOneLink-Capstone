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

// Validation-audit coverage (2026-10-30) for TasksController.AddChecklistItem:
// the view's label <input> has maxlength="200" and the DB column is
// nvarchar(200), but neither was re-checked server-side — an overlong label
// from a direct POST would have hit a raw SQL truncation error instead of a
// clean message. Weight was floored at 1 but never capped, so a weight near
// int.MaxValue could overflow the checked Sum() in
// TaskAssignmentService.RecalculateProgressFromChecklist.
public class TasksControllerAddChecklistItemTests
{
    private static (AppDbContext Context, TaskAssignment Assignment) SeedAssignment()
    {
        var context = InMemoryDbContextFactory.Create();
        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        var task = new TaskItem { TaskName = "Task", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(task);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = TaskWorkflow.InProgress };
        context.TaskAssignments.Add(assignment);
        context.SaveChanges();

        return (context, assignment);
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
    public async Task AddChecklistItem_LabelOver200Characters_IsRejectedAndNothingSaved()
    {
        var (context, assignment) = SeedAssignment();
        var controller = BuildController(context);

        var result = await controller.AddChecklistItem(assignment.Id, new string('a', 201));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Checklist item label is too long (maximum 200 characters).", controller.TempData["ErrorMessage"]);
        Assert.Empty(await context.TaskChecklistItems.Where(c => c.TaskAssignmentId == assignment.Id).ToListAsync());
    }

    [Fact]
    public async Task AddChecklistItem_LabelExactly200Characters_Succeeds()
    {
        var (context, assignment) = SeedAssignment();
        var controller = BuildController(context);

        var result = await controller.AddChecklistItem(assignment.Id, new string('a', 200));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(await context.TaskChecklistItems.Where(c => c.TaskAssignmentId == assignment.Id).ToListAsync());
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1000)]
    [InlineData(101)]
    public async Task AddChecklistItem_WeightAboveCap_IsClampedTo100(int requestedWeight)
    {
        var (context, assignment) = SeedAssignment();
        var controller = BuildController(context);

        await controller.AddChecklistItem(assignment.Id, "Label", requestedWeight);

        var saved = await context.TaskChecklistItems.SingleAsync(c => c.TaskAssignmentId == assignment.Id);
        Assert.Equal(100, saved.Weight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AddChecklistItem_WeightBelowOne_IsFlooredAtOne(int requestedWeight)
    {
        var (context, assignment) = SeedAssignment();
        var controller = BuildController(context);

        await controller.AddChecklistItem(assignment.Id, "Label", requestedWeight);

        var saved = await context.TaskChecklistItems.SingleAsync(c => c.TaskAssignmentId == assignment.Id);
        Assert.Equal(1, saved.Weight);
    }
}
