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

// Regression tests for TasksController.AddSubtask: only the OPD (SuperAdmin,
// via ManageOfficeWideTasks) creates tasks or adds subtasks — an Admin or
// Supervisor only assigns the subtasks already there, even on a directive
// that belongs to their own department. AddSubtask previously allowed the
// owning department's Admin/Supervisor through too (same scope check as
// MainTaskDetails), which contradicted this rule.
public class TasksControllerAddSubtaskTests
{
    private static (AppDbContext Context, TaskItem Directive) SeedDirective()
    {
        var context = InMemoryDbContextFactory.Create();
        var directive = new TaskItem
        {
            TaskName = "BDD Directive",
            Description = "d",
            TaskLevel = TaskLevels.Main,
            TaskType = TaskTypes.DepartmentDirective,
            OwningDepartment = "BDD",
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(directive);
        context.SaveChanges();
        return (context, directive);
    }

    private static TasksController BuildController(AppDbContext context, string? role, string? department)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (role != null) httpContext.Session.SetString("UserRole", role);
        if (department != null) httpContext.Session.SetString("UserDepartment", department);
        httpContext.Session.SetInt32("UserId", 1);

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
    public async Task AddSubtask_DepartmentScopedAdmin_OwnDepartmentDirective_Is403()
    {
        var (context, directive) = SeedDirective();
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.AddSubtask(directive.Id, "New Subtask");

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
        Assert.Empty(await context.TaskItems.Where(t => t.ParentTaskId == directive.Id).ToListAsync());
    }

    [Fact]
    public async Task AddSubtask_Supervisor_OwnDepartmentDirective_Is403()
    {
        var (context, directive) = SeedDirective();
        var controller = BuildController(context, "Supervisor", "BDD");

        var result = await controller.AddSubtask(directive.Id, "New Subtask");

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task AddSubtask_SuperAdmin_Succeeds()
    {
        var (context, directive) = SeedDirective();
        var controller = BuildController(context, "SuperAdmin", null);

        var result = await controller.AddSubtask(directive.Id, "New Subtask");

        Assert.IsType<RedirectToActionResult>(result);
        var subtasks = await context.TaskItems.Where(t => t.ParentTaskId == directive.Id).ToListAsync();
        Assert.Single(subtasks);
        Assert.Equal("New Subtask", subtasks[0].TaskName);
    }
}
