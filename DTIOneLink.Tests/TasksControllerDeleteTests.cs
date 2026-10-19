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

// Regression tests for TasksController.Delete: same symmetry as Create and
// AddSubtask (OPD/SuperAdmin only), and refused once any real work exists
// anywhere in the task's tree (any assignment past 0% or any proof ever
// submitted) — a mistaken or no-longer-needed directive can still be
// removed, but real employee work never silently disappears.
public class TasksControllerDeleteTests
{
    private static AppDbContext SeedContext() => InMemoryDbContextFactory.Create();

    private static TaskItem AddMainTask(AppDbContext context, string taskType = TaskTypes.DirectAdmin)
    {
        var task = new TaskItem
        {
            TaskName = "Direct Admin Task",
            Description = "d",
            TaskLevel = TaskLevels.Main,
            TaskType = taskType,
            OwningDepartment = "BDD",
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(task);
        context.SaveChanges();
        return task;
    }

    private static TasksController BuildController(AppDbContext context, string? role)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (role != null) httpContext.Session.SetString("UserRole", role);
        httpContext.Session.SetInt32("UserId", 1);

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
    public async Task Delete_SuperAdmin_NoWorkStarted_RemovesTheTask()
    {
        using var context = SeedContext();
        var task = AddMainTask(context);
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.Delete(task.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(await context.TaskItems.Where(t => t.Id == task.Id).ToListAsync());
    }

    [Fact]
    public async Task Delete_Admin_IsForbidden()
    {
        using var context = SeedContext();
        var task = AddMainTask(context);
        var controller = BuildController(context, "Admin");

        var result = await controller.Delete(task.Id);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
        Assert.NotEmpty(await context.TaskItems.Where(t => t.Id == task.Id).ToListAsync());
    }

    [Fact]
    public async Task Delete_WorkAlreadyStarted_IsRefusedAndTaskSurvives()
    {
        using var context = SeedContext();
        var task = AddMainTask(context);
        context.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, UserId = 2, Status = TaskWorkflow.InProgress, Progress = 10 });
        context.SaveChanges();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.Delete(task.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotEmpty(await context.TaskItems.Where(t => t.Id == task.Id).ToListAsync());
    }

    [Fact]
    public async Task Delete_DepartmentDirective_RemovesSubtasksToo()
    {
        using var context = SeedContext();
        var directive = AddMainTask(context, TaskTypes.DepartmentDirective);
        var subtask = new TaskItem
        {
            TaskName = "Subtask",
            Description = "d",
            TaskLevel = TaskLevels.Subtask,
            TaskType = TaskTypes.DepartmentDirective,
            OwningDepartment = "BDD",
            ParentTaskId = directive.Id,
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(subtask);
        context.SaveChanges();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.Delete(directive.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(await context.TaskItems.Where(t => t.Id == directive.Id || t.Id == subtask.Id).ToListAsync());
    }

    [Fact]
    public async Task Delete_SubtaskHasStarted_WholeDirectiveIsRefused()
    {
        using var context = SeedContext();
        var directive = AddMainTask(context, TaskTypes.DepartmentDirective);
        var subtask = new TaskItem
        {
            TaskName = "Subtask",
            Description = "d",
            TaskLevel = TaskLevels.Subtask,
            TaskType = TaskTypes.DepartmentDirective,
            OwningDepartment = "BDD",
            ParentTaskId = directive.Id,
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(subtask);
        context.SaveChanges();
        context.TaskAssignments.Add(new TaskAssignment { TaskId = subtask.Id, UserId = 2, Status = TaskWorkflow.InProgress, Progress = 50 });
        context.SaveChanges();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.Delete(directive.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotEmpty(await context.TaskItems.Where(t => t.Id == directive.Id).ToListAsync());
        Assert.NotEmpty(await context.TaskItems.Where(t => t.Id == subtask.Id).ToListAsync());
    }

    [Fact]
    public async Task Delete_UnknownTaskId_ReturnsNotFound()
    {
        using var context = SeedContext();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }
}
