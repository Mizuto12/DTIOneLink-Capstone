using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace DTIOneLink.Tests;

// Regression tests for TasksController's department-scoping logic (the
// IDOR-prevention rule a prior security audit verified): a department-scoped
// Admin/Supervisor may only see tasks belonging to their own department
// (OwningDepartment, falling back to the parent Main Task's, falling back to
// any assignee's own Department for legacy rows), while a SuperAdmin
// (granted via ManageOfficeWideTasks, never by role string) sees every
// department's tasks unfiltered. Exercised through the public Index action
// (TaskIndexViewModel.Tasks), not the private helpers directly, since Index
// is a thin wrapper around exactly this scoping query.
public class TasksControllerScopingTests
{
    private static TasksController BuildController(string? role, string? department, int? userId = null)
    {
        var context = InMemoryDbContextFactory.Create();

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (role != null)
        {
            httpContext.Session.SetString("UserRole", role);
        }
        if (department != null)
        {
            httpContext.Session.SetString("UserDepartment", department);
        }
        if (userId.HasValue)
        {
            httpContext.Session.SetInt32("UserId", userId.Value);
        }

        var config = new ConfigurationBuilder().Build();
        var notifications = new NotificationService(context, config);
        var taskAssignments = new TaskAssignmentService(context);
        var opdTasks = new OpdTaskService(context, taskAssignments, notifications);

        var controller = new TasksController(context, notifications, taskAssignments, opdTasks)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
        return controller;
    }

    // Seeds four tasks exercising every branch of the effective-department
    // rule used throughout TasksController:
    //  - TaskA: OwningDepartment = "BDD" directly.
    //  - TaskB: OwningDepartment = "FAU" directly (a different department).
    //  - TaskC: OwningDepartment null, but its ParentTask's OwningDepartment
    //    is "BDD" (a just-created OPD subtask with zero assignees yet).
    //  - TaskD: OwningDepartment null AND no ParentTask — legacy row that
    //    predates OwningDepartment — falls back to its sole assignee's own
    //    Department, "CPD".
    private static AppDbContext SeedScopedTasks(out TaskItem taskA, out TaskItem taskB, out TaskItem taskC, out TaskItem taskD)
    {
        var context = InMemoryDbContextFactory.Create();

        var bddEmployee = new User { Username = "bdd.emp", FullName = "BDD Employee", Role = "Employee", Department = "BDD", IsActive = true };
        var cpdEmployee = new User { Username = "cpd.emp", FullName = "CPD Employee", Role = "Employee", Department = "CPD", IsActive = true };
        context.Users.AddRange(bddEmployee, cpdEmployee);
        context.SaveChanges();

        var parent = new TaskItem
        {
            TaskName = "BDD Main Directive",
            Description = "parent",
            TaskLevel = TaskLevels.Main,
            OwningDepartment = "BDD",
            DueDate = DateTime.UtcNow.AddDays(10),
        };
        context.TaskItems.Add(parent);
        context.SaveChanges();

        taskA = new TaskItem { TaskName = "Task A", Description = "d", OwningDepartment = "BDD", DueDate = DateTime.UtcNow.AddDays(5) };
        taskB = new TaskItem { TaskName = "Task B", Description = "d", OwningDepartment = "FAU", DueDate = DateTime.UtcNow.AddDays(5) };
        taskC = new TaskItem { TaskName = "Task C", Description = "d", ParentTaskId = parent.Id, DueDate = DateTime.UtcNow.AddDays(5) };
        taskD = new TaskItem { TaskName = "Task D", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.AddRange(taskA, taskB, taskC, taskD);
        context.SaveChanges();

        context.TaskAssignments.Add(new TaskAssignment { TaskId = taskD.Id, UserId = cpdEmployee.Id });
        context.SaveChanges();

        return context;
    }

    // Rebuilds a controller around an already-seeded context (SeedScopedTasks
    // returns its own context so the seeded ids/entities are valid against it).
    private static TasksController BuildControllerForContext(AppDbContext context, string? role, string? department)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (role != null) httpContext.Session.SetString("UserRole", role);
        if (department != null) httpContext.Session.SetString("UserDepartment", department);

        var config = new ConfigurationBuilder().Build();
        var notifications = new NotificationService(context, config);
        var taskAssignments = new TaskAssignmentService(context);
        var opdTasks = new OpdTaskService(context, taskAssignments, notifications);

        return new TasksController(context, notifications, taskAssignments, opdTasks)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
    }

    [Fact]
    public async Task Index_DepartmentScopedAdmin_SeesOnlyOwnDepartmentsTasks_IncludingParentFallback()
    {
        var context = SeedScopedTasks(out var taskA, out var taskB, out var taskC, out var taskD);
        var controller = BuildControllerForContext(context, "Admin", "BDD");

        var result = await controller.Index(null, null, null, null, null, null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TaskIndexViewModel>(view.Model);
        var ids = model.Tasks.Select(t => t.Id).ToHashSet();

        // TaskA (direct OwningDepartment) and TaskC (inherited via ParentTask)
        // both belong to BDD and must be visible.
        Assert.Contains(taskA.Id, ids);
        Assert.Contains(taskC.Id, ids);
        // TaskB belongs to a different department (FAU) outright.
        Assert.DoesNotContain(taskB.Id, ids);
        // TaskD's legacy fallback only resolves to CPD (its assignee's
        // department), never BDD, so it must not leak into this department's view.
        Assert.DoesNotContain(taskD.Id, ids);
    }

    [Fact]
    public async Task Index_DepartmentScopedAdmin_LegacyTaskWithNoOwningDepartmentFallsBackToAssigneesDepartment()
    {
        var context = SeedScopedTasks(out var taskA, out var taskB, out var taskC, out var taskD);
        var controller = BuildControllerForContext(context, "Admin", "CPD");

        var result = await controller.Index(null, null, null, null, null, null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TaskIndexViewModel>(view.Model);
        var ids = model.Tasks.Select(t => t.Id).ToHashSet();

        // Only the legacy task (no OwningDepartment, no ParentTask) whose
        // sole assignee is in CPD should be visible to a CPD Admin.
        Assert.Contains(taskD.Id, ids);
        Assert.DoesNotContain(taskA.Id, ids);
        Assert.DoesNotContain(taskB.Id, ids);
        Assert.DoesNotContain(taskC.Id, ids);
    }

    [Fact]
    public async Task Index_SuperAdmin_IsOfficeWideAndSeesEveryDepartmentsTasksUnfiltered()
    {
        var context = SeedScopedTasks(out var taskA, out var taskB, out var taskC, out var taskD);
        // SuperAdmin's office-wide access comes exclusively from
        // ManageOfficeWideTasks (RolePermissions), and the role string
        // "SuperAdmin" is the only one mapped to it — never "Admin"/"Supervisor".
        var controller = BuildControllerForContext(context, "SuperAdmin", "BDD");

        var result = await controller.Index(null, null, null, null, null, null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TaskIndexViewModel>(view.Model);
        var ids = model.Tasks.Select(t => t.Id).ToHashSet();

        Assert.True(model.IsOfficeWide);
        Assert.Contains(taskA.Id, ids);
        Assert.Contains(taskB.Id, ids);
        Assert.Contains(taskC.Id, ids);
        Assert.Contains(taskD.Id, ids);
    }

    [Fact]
    public async Task Index_Supervisor_IsTreatedAsDepartmentScoped_NotOfficeWide()
    {
        // "Supervisor" is explicitly one of the two department-scoped role
        // strings (IsDepartmentTaskManager), and must never be granted the
        // office-wide (SuperAdmin-only) ManageOfficeWideTasks permission.
        var context = SeedScopedTasks(out var taskA, out var taskB, out _, out _);
        var controller = BuildControllerForContext(context, "Supervisor", "FAU");

        var result = await controller.Index(null, null, null, null, null, null, null, 1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TaskIndexViewModel>(view.Model);

        Assert.False(model.IsOfficeWide);
        var ids = model.Tasks.Select(t => t.Id).ToHashSet();
        Assert.Contains(taskB.Id, ids);
        Assert.DoesNotContain(taskA.Id, ids);
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData(null)]
    public async Task Index_RoleWithoutTaskManagementAccess_Is403BeforeAnyQueryRuns(string? role)
    {
        // CanAccessTaskManagement (IsDepartmentTaskManager || IsOfficeWideTaskManager)
        // is the combined gate for every action in this controller — an
        // Employee (or no session role) must never reach the scoping query.
        var controller = BuildController(role, "BDD");

        var result = await controller.Index(null, null, null, null, null, null, null, 1);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
    }
}
