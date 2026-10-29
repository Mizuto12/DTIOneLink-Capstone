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

// Covers the checklist-item feature from the employee side: toggling an
// item recomputes Progress from the checklist (not the manual slider),
// ownership is enforced, and the manual progress slider locks out once any
// checklist item exists.
public class EmployeeControllerChecklistTests
{
    private static (AppDbContext Context, TaskItem Task, TaskAssignment Assignment, User Employee) Seed()
    {
        var context = InMemoryDbContextFactory.Create();

        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        context.SaveChanges();

        var task = new TaskItem { TaskName = "Task", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(task);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = TaskWorkflow.InProgress, Progress = 0 };
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
        var controller = new EmployeeController(
            context,
            new TaskAssignmentService(context),
            new NotificationService(context, config, NullLogger<NotificationService>.Instance),
            NullLogger<EmployeeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
        return controller;
    }

    [Fact]
    public async Task ToggleChecklistItem_CheckingAnItem_RecomputesAssignmentProgress()
    {
        var (context, _, assignment, employee) = Seed();
        var item1 = new TaskChecklistItem { TaskAssignmentId = assignment.Id, Label = "Step 1", Weight = 1 };
        var item2 = new TaskChecklistItem { TaskAssignmentId = assignment.Id, Label = "Step 2", Weight = 1 };
        context.TaskChecklistItems.AddRange(item1, item2);
        await context.SaveChangesAsync();
        var controller = BuildController(context, employee.Id);

        var result = await controller.ToggleChecklistItem(item1.Id);

        Assert.IsType<JsonResult>(result);
        var reloaded = context.TaskAssignments.Single();
        Assert.Equal(50, reloaded.Progress); // 1 of 2 equal-weight items done
        Assert.Equal(TaskWorkflow.InProgress, reloaded.Status);
        Assert.True(context.TaskChecklistItems.Single(c => c.Id == item1.Id).IsDone);
    }

    [Fact]
    public async Task ToggleChecklistItem_NotTheOwner_IsNotFound()
    {
        var (context, _, assignment, _) = Seed();
        var item = new TaskChecklistItem { TaskAssignmentId = assignment.Id, Label = "Step 1" };
        context.TaskChecklistItems.Add(item);
        await context.SaveChangesAsync();

        // A different, unrelated user tries to toggle someone else's item.
        var intruder = new User { Id = 999, Username = "intruder", PasswordHash = "x", FullName = "Intruder", Role = "Employee" };
        context.Users.Add(intruder);
        await context.SaveChangesAsync();
        var controller = BuildController(context, intruder.Id);

        var result = await controller.ToggleChecklistItem(item.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.False(context.TaskChecklistItems.Single().IsDone); // untouched
    }

    [Fact]
    public async Task ApplyProgressUpdateAsync_AssignmentHasChecklistItems_ManualSliderIsRejected()
    {
        var (context, task, assignment, employee) = Seed();
        context.TaskChecklistItems.Add(new TaskChecklistItem { TaskAssignmentId = assignment.Id, Label = "Step 1" });
        await context.SaveChangesAsync();
        var controller = BuildController(context, employee.Id);

        var result = await controller.UpdateProgress(task.Id, 50);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, context.TaskAssignments.Single().Progress); // unchanged
    }
}
