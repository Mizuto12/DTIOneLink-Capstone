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

// Regression coverage for the 2026-10-29 audit's highest-severity finding:
// adding a new assignee to an already-Completed task used to be allowed,
// and since RecalculateOverallStatus recomputes the task's overall status
// purely from the assignment list, the new (Pending) assignment flipped
// the whole task back to "In Progress" — with no transition check and no
// audit trail entry explaining why. Completed is meant to be terminal
// everywhere else in the app (TaskWorkflow.AllowedTransitions[Completed]
// is empty).
public class TasksControllerEditValidationTests
{
    private static (AppDbContext Context, TaskItem Task, User NewEmployee) SeedCompletedTask()
    {
        var context = InMemoryDbContextFactory.Create();

        var existingEmployee = new User
        {
            Username = "emp1@dti.gov.ph", Email = "emp1@dti.gov.ph", FullName = "Employee One",
            Role = "Employee", Department = "BDD", IsActive = true, EmailConfirmed = true, SecurityStamp = "s1",
        };
        var newEmployee = new User
        {
            Username = "emp2@dti.gov.ph", Email = "emp2@dti.gov.ph", FullName = "Employee Two",
            Role = "Employee", Department = "BDD", IsActive = true, EmailConfirmed = true, SecurityStamp = "s2",
        };
        context.Users.AddRange(existingEmployee, newEmployee);
        context.SaveChanges();

        var task = new TaskItem
        {
            TaskName = "Finished Task",
            Description = "d",
            TaskLevel = TaskLevels.Subtask, // standalone, not a Main Task with its own scope rules
            TaskType = TaskTypes.DirectAdmin,
            OwningDepartment = "BDD",
            DueDate = DateTime.UtcNow.AddDays(5),
            Status = TaskWorkflow.Completed,
            Priority = "medium",
        };
        context.TaskItems.Add(task);
        context.SaveChanges();

        context.TaskAssignments.Add(new TaskAssignment
        {
            TaskId = task.Id,
            UserId = existingEmployee.Id,
            Status = TaskWorkflow.Completed,
            Progress = 100,
            IsPrimaryAssignee = true,
        });
        context.SaveChanges();

        return (context, task, newEmployee);
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
    public async Task Edit_AddingAssigneeToACompletedTask_IsRejectedAndTaskStaysCompleted()
    {
        var (context, task, newEmployee) = SeedCompletedTask();
        var controller = BuildController(context);

        var model = new TaskEditViewModel
        {
            Id = task.Id,
            TaskName = task.TaskName,
            Description = task.Description,
            DueDate = task.DueDate,
            Priority = task.Priority,
            AddAssigneeIds = new List<int> { newEmployee.Id },
        };

        var result = await controller.Edit(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);

        var reloadedTask = await context.TaskItems.FindAsync(task.Id);
        Assert.Equal(TaskWorkflow.Completed, reloadedTask!.Status);
        Assert.False(await context.TaskAssignments.AnyAsync(a => a.UserId == newEmployee.Id));
    }

    [Fact]
    public async Task Edit_InvalidPriorityOnAFullEdit_IsRejectedAndNothingChanges()
    {
        var (context, task, _) = SeedCompletedTask();
        // Not a terminal-state scenario here — swap to a normal in-progress
        // task so this test isolates the Priority check specifically.
        task.Status = TaskWorkflow.InProgress;
        context.SaveChanges();
        var controller = BuildController(context);

        var model = new TaskEditViewModel
        {
            Id = task.Id,
            TaskName = task.TaskName,
            Description = task.Description,
            DueDate = task.DueDate,
            Priority = "ultra-mega-urgent",
        };

        var result = await controller.Edit(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        var reloadedTask = await context.TaskItems.FindAsync(task.Id);
        Assert.Equal("medium", reloadedTask!.Priority);
    }

    // Validation-audit coverage (2026-10-30): same gap as Create — a
    // TaskName/Description made only of symbols/punctuation passed
    // [Required]+[StringLength] unnoticed on Edit too.
    [Theory]
    [InlineData("...")]
    [InlineData("---")]
    public async Task Edit_TaskNameIsSymbolsOnly_IsRejectedAndNothingChanges(string taskName)
    {
        var (context, task, _) = SeedCompletedTask();
        task.Status = TaskWorkflow.InProgress;
        context.SaveChanges();
        var controller = BuildController(context);

        var model = new TaskEditViewModel
        {
            Id = task.Id,
            TaskName = taskName,
            Description = task.Description,
            DueDate = task.DueDate,
            Priority = task.Priority,
        };

        var result = await controller.Edit(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        var reloadedTask = await context.TaskItems.FindAsync(task.Id);
        Assert.Equal("Finished Task", reloadedTask!.TaskName);
    }

    [Theory]
    [InlineData("...")]
    [InlineData("---")]
    public async Task Edit_DescriptionIsSymbolsOnly_IsRejectedAndNothingChanges(string description)
    {
        var (context, task, _) = SeedCompletedTask();
        task.Status = TaskWorkflow.InProgress;
        context.SaveChanges();
        var controller = BuildController(context);

        var model = new TaskEditViewModel
        {
            Id = task.Id,
            TaskName = task.TaskName,
            Description = description,
            DueDate = task.DueDate,
            Priority = task.Priority,
        };

        var result = await controller.Edit(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        var reloadedTask = await context.TaskItems.FindAsync(task.Id);
        Assert.Equal("d", reloadedTask!.Description);
    }
}
