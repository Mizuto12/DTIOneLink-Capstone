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

// Effort-based subtask weighting (2026-10-31): workload Weight assignment,
// validation, authorization, and the recalculation triggers around it
// (TasksController.Create/AddSubtask/Edit, OpdTaskService, and
// TaskAssignmentService.PermanentlyDeleteTasksAsync). Weighted-aggregation
// math itself is covered in TaskAssignmentServiceTests — this file is the
// controller-level surface: who can set a weight, what gets rejected, and
// that a change actually reaches the parent Main Task's recalculated
// progress end-to-end.
public class TasksControllerSubtaskWeightTests
{
    private static (AppDbContext Context, User SuperAdmin, User Admin) SeedUsers(string department = TargetDepartments.BDD)
    {
        var context = InMemoryDbContextFactory.Create();
        var superAdmin = new User { Username = "opd", PasswordHash = "x", FullName = "OPD", Role = "SuperAdmin" };
        var admin = new User
        {
            Username = "admin@dti.gov.ph", Email = "admin@dti.gov.ph", FullName = "Test Admin",
            Role = "Admin", Department = department, IsActive = true, EmailConfirmed = true, SecurityStamp = "s",
        };
        context.Users.AddRange(superAdmin, admin);
        context.SaveChanges();
        return (context, superAdmin, admin);
    }

    private static TasksController BuildController(AppDbContext context, string role, string? department = null, int userId = 99)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", role);
        httpContext.Session.SetInt32("UserId", userId);
        if (department != null)
        {
            httpContext.Session.SetString("UserDepartment", department);
        }

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

    private static TaskCreateViewModel ValidDirectiveModel(User admin) => new()
    {
        TaskName = "Department Directive",
        Description = "Covers everyone in the department.",
        TaskType = TaskTypes.DepartmentDirective,
        OwningDepartment = TargetDepartments.BDD,
        ResponsibleAdminUserId = admin.Id,
        DueDate = DateTime.UtcNow.AddDays(5),
        Priority = "medium",
    };

    // ---------- Create: weight validation + persistence ----------

    [Fact]
    public async Task Create_ValidSubtaskWeights_ArePersisted()
    {
        var (context, _, admin) = SeedUsers();
        var controller = BuildController(context, "SuperAdmin");
        var model = ValidDirectiveModel(admin);
        model.SubtaskNames = new List<string> { "Obtain signature", "Prepare documentation" };
        model.SubtaskWeights = new List<int> { 1, 3 };

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        var subtasks = await context.TaskItems.Where(t => t.TaskLevel == TaskLevels.Subtask).OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, subtasks.Count);
        Assert.Equal(1, subtasks[0].Weight);
        Assert.Equal(3, subtasks[1].Weight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task Create_OutOfRangeSubtaskWeight_IsRejectedAndNothingSaved(int invalidWeight)
    {
        var (context, _, admin) = SeedUsers();
        var controller = BuildController(context, "SuperAdmin");
        var model = ValidDirectiveModel(admin);
        model.SubtaskNames = new List<string> { "Obtain signature" };
        model.SubtaskWeights = new List<int> { invalidWeight };

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync(t => t.TaskLevel == TaskLevels.Subtask));
    }

    [Fact]
    public async Task Create_NoWeightsSubmitted_DefaultsToOne()
    {
        var (context, _, admin) = SeedUsers();
        var controller = BuildController(context, "SuperAdmin");
        var model = ValidDirectiveModel(admin);
        model.SubtaskNames = new List<string> { "Obtain signature" };
        model.SubtaskWeights = null;

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        var subtask = await context.TaskItems.SingleAsync(t => t.TaskLevel == TaskLevels.Subtask);
        Assert.Equal(TaskWorkflow.DefaultSubtaskWeight, subtask.Weight);
    }

    // ---------- AddSubtask: validation + authorization ----------

    [Fact]
    public async Task AddSubtask_ValidWeight_IsPersistedAndRecalculatesParent()
    {
        var (context, _, _) = SeedUsers();
        var controller = BuildController(context, "SuperAdmin");
        // Build the directive directly rather than through Create, to keep this test focused.
        var mainTask = new TaskItem { TaskName = "Directive", Description = "d", TaskLevel = TaskLevels.Main, TaskType = TaskTypes.DepartmentDirective, OwningDepartment = TargetDepartments.BDD, DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(mainTask);
        await context.SaveChangesAsync();
        var existingSubtask = new TaskItem { TaskName = "Existing", Description = "d", TaskLevel = TaskLevels.Subtask, ParentTaskId = mainTask.Id, OwningDepartment = TargetDepartments.BDD, DueDate = mainTask.DueDate, Status = TaskWorkflow.Completed, Progress = 100, Weight = 1 };
        context.TaskItems.Add(existingSubtask);
        await context.SaveChangesAsync();

        var result = await controller.AddSubtask(mainTask.Id, "New heavier subtask", requiresReview: true, weight: 3);

        Assert.IsType<RedirectToActionResult>(result);
        var added = await context.TaskItems.SingleAsync(t => t.TaskName == "New heavier subtask");
        Assert.Equal(3, added.Weight);
        var reloadedMain = await context.TaskItems.SingleAsync(t => t.Id == mainTask.Id);
        // (100*1 + 0*3) / (1+3) = 25%, not the 50% an unweighted average would give.
        Assert.Equal(25, reloadedMain.Progress);
        Assert.NotEqual(TaskWorkflow.Completed, reloadedMain.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task AddSubtask_OutOfRangeWeight_IsRejectedAndNothingAdded(int invalidWeight)
    {
        var (context, _, admin) = SeedUsers();
        var controller = BuildController(context, "SuperAdmin");
        var mainTask = new TaskItem { TaskName = "Directive", Description = "d", TaskLevel = TaskLevels.Main, TaskType = TaskTypes.DepartmentDirective, OwningDepartment = TargetDepartments.BDD, DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(mainTask);
        await context.SaveChangesAsync();

        var result = await controller.AddSubtask(mainTask.Id, "New subtask", requiresReview: true, weight: invalidWeight);

        Assert.IsType<RedirectToActionResult>(result); // redirects back with TempData error, same as the name-validation failures
        Assert.False(await context.TaskItems.AnyAsync(t => t.TaskLevel == TaskLevels.Subtask));
    }

    [Fact]
    public async Task AddSubtask_AdminIsNotOfficeWide_Forbidden()
    {
        var (context, _, admin) = SeedUsers();
        var controller = BuildController(context, "Admin", department: TargetDepartments.BDD, userId: admin.Id);
        var mainTask = new TaskItem { TaskName = "Directive", Description = "d", TaskLevel = TaskLevels.Main, TaskType = TaskTypes.DepartmentDirective, OwningDepartment = TargetDepartments.BDD, DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(mainTask);
        await context.SaveChangesAsync();

        var result = await controller.AddSubtask(mainTask.Id, "New subtask", requiresReview: true, weight: 1);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.False(await context.TaskItems.AnyAsync(t => t.TaskLevel == TaskLevels.Subtask));
    }

    // ---------- Edit: weight validation, authorization, recalculation ----------

    private static async Task<(TaskItem MainTask, TaskItem Subtask)> SeedDirectiveWithTwoSubtasksAsync(AppDbContext context)
    {
        // Edit() treats any non-Main task as carrying its own assignment
        // model (hasAssignmentModel) and requires at least one current
        // assignee regardless of isAssignmentOnly — each subtask needs one
        // here so the edit itself can succeed; this test is about Weight,
        // not assignment.
        var employee1 = new User { Username = "e1", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = TargetDepartments.BDD };
        var employee2 = new User { Username = "e2", PasswordHash = "x", FullName = "Employee Two", Role = "Employee", Department = TargetDepartments.BDD };
        context.Users.AddRange(employee1, employee2);
        await context.SaveChangesAsync();

        var mainTask = new TaskItem { TaskName = "Directive", Description = "d", TaskLevel = TaskLevels.Main, TaskType = TaskTypes.DepartmentDirective, OwningDepartment = TargetDepartments.BDD, DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(mainTask);
        await context.SaveChangesAsync();

        var doneSubtask = new TaskItem { TaskName = "Done", Description = "d", TaskLevel = TaskLevels.Subtask, ParentTaskId = mainTask.Id, OwningDepartment = TargetDepartments.BDD, DueDate = mainTask.DueDate, Status = TaskWorkflow.Completed, Progress = 100, Weight = 1 };
        var pendingSubtask = new TaskItem { TaskName = "Pending", Description = "d", TaskLevel = TaskLevels.Subtask, ParentTaskId = mainTask.Id, OwningDepartment = TargetDepartments.BDD, DueDate = mainTask.DueDate, Status = TaskWorkflow.Pending, Progress = 0, Weight = 1 };
        context.TaskItems.AddRange(doneSubtask, pendingSubtask);
        await context.SaveChangesAsync();

        context.TaskAssignments.Add(new TaskAssignment { TaskId = doneSubtask.Id, UserId = employee1.Id, Status = TaskWorkflow.Completed, Progress = 100, IsPrimaryAssignee = true });
        context.TaskAssignments.Add(new TaskAssignment { TaskId = pendingSubtask.Id, UserId = employee2.Id, Status = TaskWorkflow.Pending, Progress = 0, IsPrimaryAssignee = true });
        await context.SaveChangesAsync();

        var service = new TaskAssignmentService(context);
        service.RecalculateMainTaskFromSubtasks(mainTask);
        await context.SaveChangesAsync();

        return (mainTask, pendingSubtask);
    }

    [Fact]
    public async Task Edit_SuperAdminChangesSubtaskWeight_RecalculatesParentProgress()
    {
        var (context, _, _) = SeedUsers();
        var (mainTask, pendingSubtask) = await SeedDirectiveWithTwoSubtasksAsync(context);
        Assert.Equal(50, (await context.TaskItems.SingleAsync(t => t.Id == mainTask.Id)).Progress); // sanity: equal-weight baseline

        var controller = BuildController(context, "SuperAdmin");
        var model = new TaskEditViewModel
        {
            Id = pendingSubtask.Id,
            TaskName = pendingSubtask.TaskName,
            Description = pendingSubtask.Description,
            DueDate = pendingSubtask.DueDate,
            Priority = pendingSubtask.Priority,
            Weight = 3,
        };

        var result = await controller.Edit(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(3, (await context.TaskItems.SingleAsync(t => t.Id == pendingSubtask.Id)).Weight);
        var reloadedMain = await context.TaskItems.SingleAsync(t => t.Id == mainTask.Id);
        // (100*1 + 0*3) / (1+3) = 25%, not the previous 50%.
        Assert.Equal(25, reloadedMain.Progress);
    }

    [Fact]
    public async Task Edit_DepartmentAdminCannotChangeSubtaskWeight()
    {
        var (context, _, admin) = SeedUsers();
        var (mainTask, pendingSubtask) = await SeedDirectiveWithTwoSubtasksAsync(context);

        var controller = BuildController(context, "Admin", department: TargetDepartments.BDD, userId: admin.Id);
        var model = new TaskEditViewModel
        {
            Id = pendingSubtask.Id,
            TaskName = pendingSubtask.TaskName,
            Description = pendingSubtask.Description,
            DueDate = pendingSubtask.DueDate,
            Priority = pendingSubtask.Priority,
            Weight = 10, // attempted tamper — isAssignmentOnly should silently drop this
        };

        var result = await controller.Edit(model);

        Assert.IsType<RedirectToActionResult>(result); // the edit itself succeeds (due date is still assignment-only editable) — just not the weight
        Assert.Equal(1, (await context.TaskItems.SingleAsync(t => t.Id == pendingSubtask.Id)).Weight); // unchanged
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Edit_OutOfRangeWeight_IsRejectedAndUnchanged(int invalidWeight)
    {
        var (context, _, _) = SeedUsers();
        var (_, pendingSubtask) = await SeedDirectiveWithTwoSubtasksAsync(context);

        var controller = BuildController(context, "SuperAdmin");
        var model = new TaskEditViewModel
        {
            Id = pendingSubtask.Id,
            TaskName = pendingSubtask.TaskName,
            Description = pendingSubtask.Description,
            DueDate = pendingSubtask.DueDate,
            Priority = pendingSubtask.Priority,
            Weight = invalidWeight,
        };

        var result = await controller.Edit(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(1, (await context.TaskItems.SingleAsync(t => t.Id == pendingSubtask.Id)).Weight); // unchanged
    }

    // ---------- Removing a subtask recalculates the parent ----------

    [Fact]
    public async Task RemovingASubtask_RecalculatesParentFromRemainingWeights()
    {
        var (context, _, _) = SeedUsers();
        var mainTask = new TaskItem { TaskName = "Directive", Description = "d", TaskLevel = TaskLevels.Main, TaskType = TaskTypes.DepartmentDirective, OwningDepartment = TargetDepartments.BDD, DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(mainTask);
        await context.SaveChangesAsync();

        var keptSubtask = new TaskItem { TaskName = "Kept", Description = "d", TaskLevel = TaskLevels.Subtask, ParentTaskId = mainTask.Id, OwningDepartment = TargetDepartments.BDD, DueDate = mainTask.DueDate, Status = TaskWorkflow.Completed, Progress = 100, Weight = 1 };
        var removedSubtask = new TaskItem { TaskName = "Removed", Description = "d", TaskLevel = TaskLevels.Subtask, ParentTaskId = mainTask.Id, OwningDepartment = TargetDepartments.BDD, DueDate = mainTask.DueDate, Status = TaskWorkflow.Pending, Progress = 0, Weight = 3 };
        context.TaskItems.AddRange(keptSubtask, removedSubtask);
        await context.SaveChangesAsync();

        var service = new TaskAssignmentService(context);
        service.RecalculateMainTaskFromSubtasks(mainTask);
        await context.SaveChangesAsync();
        Assert.Equal(25, mainTask.Progress); // (100*1+0*3)/(1+3) baseline before removal

        // Load tracked with the empty collections PermanentlyDeleteTasksAsync expects.
        var toDelete = await context.TaskItems
            .Include(t => t.Assignments).Include(t => t.Submissions)
            .Include(t => t.Activities).Include(t => t.Comments)
            .SingleAsync(t => t.Id == removedSubtask.Id);

        await service.PermanentlyDeleteTasksAsync(new[] { toDelete }, deletedByUserId: 99, NullLogger.Instance);

        var reloadedMain = await context.TaskItems.SingleAsync(t => t.Id == mainTask.Id);
        Assert.Equal(100, reloadedMain.Progress); // only the weight-1, fully-done subtask remains
        Assert.Equal(TaskWorkflow.Completed, reloadedMain.Status);
        Assert.False(await context.TaskItems.AnyAsync(t => t.Id == removedSubtask.Id));
    }
}
