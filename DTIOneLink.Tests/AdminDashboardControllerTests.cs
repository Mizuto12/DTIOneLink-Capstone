using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Tests;

// Regression tests for Admin.Controllers.DashboardController's AdminDashboard()
// and Details(id): the same effective-department scoping used throughout
// TasksController (OwningDepartment, falling back to ParentTask's, falling
// back to a legacy assignee's Department), plus the Employee-only-sees-their-
// own-assignments rule and the SuperAdmin (ManageOfficeWideTasks) office-wide
// bypass. Both actions now carry [RequireLogin] (not re-tested here — already
// covered elsewhere); this calls the action methods directly after setting
// session state, same pattern as UserManagementControllerTests.
public class AdminDashboardControllerTests
{
    // Seeds the same four-task scoping fixture used across the dashboard
    // tests:
    //  - TaskA: OwningDepartment = "BDD" directly, assigned to bddEmployee.
    //  - TaskB: OwningDepartment = "FAU" directly, assigned to fauEmployee.
    //  - TaskC: OwningDepartment null, ParentTask's OwningDepartment = "BDD"
    //    (a subtask that hasn't been given its own OwningDepartment yet).
    //  - TaskD: OwningDepartment null, no ParentTask — legacy row — falls
    //    back to its sole assignee's (cpdEmployee) own Department, "CPD".
    private static AppDbContext SeedScopedTasks(
        out User bddEmployee, out User fauEmployee, out User cpdEmployee,
        out TaskItem taskA, out TaskItem taskB, out TaskItem taskC, out TaskItem taskD)
    {
        var context = InMemoryDbContextFactory.Create();

        bddEmployee = new User { Username = "bdd.emp", FullName = "BDD Employee", Role = "Employee", Department = "BDD", IsActive = true };
        fauEmployee = new User { Username = "fau.emp", FullName = "FAU Employee", Role = "Employee", Department = "FAU", IsActive = true };
        cpdEmployee = new User { Username = "cpd.emp", FullName = "CPD Employee", Role = "Employee", Department = "CPD", IsActive = true };
        context.Users.AddRange(bddEmployee, fauEmployee, cpdEmployee);
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

        context.TaskAssignments.AddRange(
            new TaskAssignment { TaskId = taskA.Id, UserId = bddEmployee.Id },
            new TaskAssignment { TaskId = taskB.Id, UserId = fauEmployee.Id },
            new TaskAssignment { TaskId = taskD.Id, UserId = cpdEmployee.Id });
        context.SaveChanges();

        return context;
    }

    private static DashboardController BuildController(AppDbContext context, string? role, string? department, int? userId = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (role != null) httpContext.Session.SetString("UserRole", role);
        if (department != null) httpContext.Session.SetString("UserDepartment", department);
        if (userId.HasValue) httpContext.Session.SetInt32("UserId", userId.Value);

        return new DashboardController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
    }

    [Fact]
    public async Task AdminDashboard_Employee_OnlySeesTasksTheyHoldAnAssignmentOn()
    {
        var context = SeedScopedTasks(out var bddEmployee, out _, out _, out var taskA, out var taskB, out _, out var taskD);
        var controller = BuildController(context, "Employee", "BDD", bddEmployee.Id);

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        var ids = tasks.Select(t => t.Id).ToHashSet();

        // Only TaskA, which bddEmployee actually holds a TaskAssignment row
        // on — never TaskB/TaskD, which belong to other employees, and never
        // decided by department membership for a plain Employee.
        Assert.Contains(taskA.Id, ids);
        Assert.DoesNotContain(taskB.Id, ids);
        Assert.DoesNotContain(taskD.Id, ids);
    }

    [Fact]
    public async Task AdminDashboard_DepartmentScopedAdmin_SeesOnlyTasksMatchingEffectiveDepartment()
    {
        var context = SeedScopedTasks(out _, out _, out _, out var taskA, out var taskB, out var taskC, out var taskD);
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        var ids = tasks.Select(t => t.Id).ToHashSet();

        // TaskA (direct OwningDepartment=BDD) and TaskC (inherited from its
        // ParentTask's OwningDepartment=BDD) both belong to this Admin's
        // department. TaskB is a different department outright; TaskD's
        // legacy fallback resolves to CPD, not BDD.
        Assert.Contains(taskA.Id, ids);
        Assert.Contains(taskC.Id, ids);
        Assert.DoesNotContain(taskB.Id, ids);
        Assert.DoesNotContain(taskD.Id, ids);
    }

    [Fact]
    public async Task AdminDashboard_DepartmentScopedAdmin_LegacyTaskFallsBackToAssigneesDepartment()
    {
        var context = SeedScopedTasks(out _, out _, out _, out var taskA, out var taskB, out var taskC, out var taskD);
        var controller = BuildController(context, "Supervisor", "CPD");

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        var ids = tasks.Select(t => t.Id).ToHashSet();

        Assert.Contains(taskD.Id, ids);
        Assert.DoesNotContain(taskA.Id, ids);
        Assert.DoesNotContain(taskB.Id, ids);
        Assert.DoesNotContain(taskC.Id, ids);
    }

    [Fact]
    public async Task AdminDashboard_SuperAdmin_SeesEveryDepartmentsTasksUnfiltered()
    {
        // ManageOfficeWideTasks (granted only to the "SuperAdmin" role
        // string) is a separate check from the Admin/Supervisor
        // department-elevated branch — never a department filter for it.
        var context = SeedScopedTasks(out _, out _, out _, out var taskA, out var taskB, out var taskC, out var taskD);
        var controller = BuildController(context, "SuperAdmin", "BDD");

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        var ids = tasks.Select(t => t.Id).ToHashSet();

        Assert.Contains(taskA.Id, ids);
        Assert.Contains(taskB.Id, ids);
        Assert.Contains(taskC.Id, ids);
        Assert.Contains(taskD.Id, ids);
    }

    [Fact]
    public async Task Details_DepartmentScopedAdmin_OutOfScopeTask_ReturnsNotFound()
    {
        var context = SeedScopedTasks(out _, out _, out _, out _, out var taskB, out _, out _);
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.Details(taskB.Id);

        // Treated exactly like it doesn't exist, rather than a 403 that
        // would confirm the task's existence to someone outside its scope.
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_DepartmentScopedAdmin_InScopeTask_ReturnsTheTask()
    {
        var context = SeedScopedTasks(out _, out _, out _, out var taskA, out _, out _, out _);
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.Details(taskA.Id);

        var view = Assert.IsType<ViewResult>(result);
        var task = Assert.IsType<TaskItem>(view.Model);
        Assert.Equal(taskA.Id, task.Id);
    }

    [Fact]
    public async Task Details_Employee_TaskTheyAreNotAssignedTo_ReturnsNotFound()
    {
        var context = SeedScopedTasks(out var bddEmployee, out _, out _, out _, out var taskB, out _, out _);
        var controller = BuildController(context, "Employee", "BDD", bddEmployee.Id);

        var result = await controller.Details(taskB.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_Employee_TaskTheyHoldAnAssignmentOn_ReturnsTheTask()
    {
        var context = SeedScopedTasks(out var bddEmployee, out _, out _, out var taskA, out _, out _, out _);
        var controller = BuildController(context, "Employee", "BDD", bddEmployee.Id);

        var result = await controller.Details(taskA.Id);

        var view = Assert.IsType<ViewResult>(result);
        var task = Assert.IsType<TaskItem>(view.Model);
        Assert.Equal(taskA.Id, task.Id);
    }

    [Fact]
    public async Task Details_SuperAdmin_CanReachAnyDepartmentsTask()
    {
        var context = SeedScopedTasks(out _, out _, out _, out _, out var taskB, out _, out _);
        var controller = BuildController(context, "SuperAdmin", "BDD");

        var result = await controller.Details(taskB.Id);

        var view = Assert.IsType<ViewResult>(result);
        var task = Assert.IsType<TaskItem>(view.Model);
        Assert.Equal(taskB.Id, task.Id);
    }
}
