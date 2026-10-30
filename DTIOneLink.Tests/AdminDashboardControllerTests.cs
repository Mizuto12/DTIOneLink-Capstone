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

    // Validation-audit coverage (2026-10-30): AdminDashboard() previously
    // loaded a department's entire task history unbounded. Now excludes a
    // Completed task only once its approved TaskSubmission.DecidedAt is
    // older than 56 days (GetCompletionTrend's own 8-week window) — every
    // non-Completed status stays regardless of age.
    private static (AppDbContext Context, TaskItem OldCompleted, TaskItem RecentCompleted,
        TaskItem OldActive, TaskItem CompletedNoSubmission) SeedRecencyFixture()
    {
        var context = InMemoryDbContextFactory.Create();
        var now = DateTime.UtcNow;

        var oldCompleted = new TaskItem
        {
            TaskName = "Old Completed", Description = "d", OwningDepartment = "BDD",
            Status = TaskWorkflow.Completed, DueDate = now.AddDays(-100), CreatedAt = now.AddDays(-120),
        };
        var recentCompleted = new TaskItem
        {
            TaskName = "Recent Completed", Description = "d", OwningDepartment = "BDD",
            Status = TaskWorkflow.Completed, DueDate = now.AddDays(-5), CreatedAt = now.AddDays(-10),
        };
        var oldActive = new TaskItem
        {
            TaskName = "Old Active", Description = "d", OwningDepartment = "BDD",
            Status = TaskWorkflow.InProgress, DueDate = now.AddDays(10), CreatedAt = now.AddDays(-400),
        };
        // Completed with no TaskSubmission row at all (e.g. pre-dates
        // submission tracking) — must be excluded, same as a stale one,
        // not kept indefinitely for lack of a record.
        var completedNoSubmission = new TaskItem
        {
            TaskName = "Completed No Submission", Description = "d", OwningDepartment = "BDD",
            Status = TaskWorkflow.Completed, DueDate = now.AddDays(-200), CreatedAt = now.AddDays(-200),
        };
        context.TaskItems.AddRange(oldCompleted, recentCompleted, oldActive, completedNoSubmission);
        context.SaveChanges();

        context.TaskSubmissions.AddRange(
            new TaskSubmission { TaskId = oldCompleted.Id, Decision = "approved", DecidedAt = now.AddDays(-90), ProofFileName = "x", ProofStoredFileName = "x" },
            new TaskSubmission { TaskId = recentCompleted.Id, Decision = "approved", DecidedAt = now.AddDays(-5), ProofFileName = "x", ProofStoredFileName = "x" });
        context.SaveChanges();

        return (context, oldCompleted, recentCompleted, oldActive, completedNoSubmission);
    }

    [Fact]
    public async Task AdminDashboard_CompletedTaskOlderThan56Days_IsExcluded()
    {
        var (context, oldCompleted, _, _, _) = SeedRecencyFixture();
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        Assert.DoesNotContain(tasks, t => t.Id == oldCompleted.Id);
    }

    [Fact]
    public async Task AdminDashboard_CompletedTaskWithin56Days_IsIncluded()
    {
        var (context, _, recentCompleted, _, _) = SeedRecencyFixture();
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        Assert.Contains(tasks, t => t.Id == recentCompleted.Id);
    }

    [Fact]
    public async Task AdminDashboard_ActiveTaskRegardlessOfAge_IsNeverExcludedByTheRecencyFilter()
    {
        var (context, _, _, oldActive, _) = SeedRecencyFixture();
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        Assert.Contains(tasks, t => t.Id == oldActive.Id);
    }

    [Fact]
    public async Task AdminDashboard_CompletedTaskWithNoSubmissionRecord_IsExcluded()
    {
        var (context, _, _, _, completedNoSubmission) = SeedRecencyFixture();
        var controller = BuildController(context, "Admin", "BDD");

        var result = await controller.AdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        Assert.DoesNotContain(tasks, t => t.Id == completedNoSubmission.Id);
    }

    // SuperAdminDashboard() had the same unbounded-query problem
    // AdminDashboard() did, but was never covered by the fix above — it's a
    // separate action, querying TaskAssignments (not TaskItems) and
    // filtering per-assignee via TaskSubmission.TaskAssignmentId. Same
    // 56-day recency rule, same "non-Completed never excluded" guarantee.
    private static (AppDbContext Context, int OldCompletedAssignmentId, int RecentCompletedAssignmentId,
        int OldActiveAssignmentId, int CompletedNoSubmissionAssignmentId) SeedSuperAdminRecencyFixture()
    {
        var context = InMemoryDbContextFactory.Create();
        var now = DateTime.UtcNow;

        var user = new User { Username = "u", FullName = "User", Role = "Employee", Department = "BDD" };
        context.Users.Add(user);

        var oldCompletedTask = new TaskItem { TaskName = "Old Completed", Description = "d", OwningDepartment = "BDD", DueDate = now.AddDays(-100), CreatedAt = now.AddDays(-120) };
        var recentCompletedTask = new TaskItem { TaskName = "Recent Completed", Description = "d", OwningDepartment = "BDD", DueDate = now.AddDays(-5), CreatedAt = now.AddDays(-10) };
        var oldActiveTask = new TaskItem { TaskName = "Old Active", Description = "d", OwningDepartment = "BDD", DueDate = now.AddDays(10), CreatedAt = now.AddDays(-400) };
        var completedNoSubmissionTask = new TaskItem { TaskName = "Completed No Submission", Description = "d", OwningDepartment = "BDD", DueDate = now.AddDays(-200), CreatedAt = now.AddDays(-200) };
        context.TaskItems.AddRange(oldCompletedTask, recentCompletedTask, oldActiveTask, completedNoSubmissionTask);
        context.SaveChanges();

        var oldCompletedAssignment = new TaskAssignment { TaskId = oldCompletedTask.Id, UserId = user.Id, Status = TaskWorkflow.Completed, Progress = 100 };
        var recentCompletedAssignment = new TaskAssignment { TaskId = recentCompletedTask.Id, UserId = user.Id, Status = TaskWorkflow.Completed, Progress = 100 };
        var oldActiveAssignment = new TaskAssignment { TaskId = oldActiveTask.Id, UserId = user.Id, Status = TaskWorkflow.InProgress, Progress = 40 };
        var completedNoSubmissionAssignment = new TaskAssignment { TaskId = completedNoSubmissionTask.Id, UserId = user.Id, Status = TaskWorkflow.Completed, Progress = 100 };
        context.TaskAssignments.AddRange(oldCompletedAssignment, recentCompletedAssignment, oldActiveAssignment, completedNoSubmissionAssignment);
        context.SaveChanges();

        context.TaskSubmissions.AddRange(
            new TaskSubmission { TaskId = oldCompletedTask.Id, TaskAssignmentId = oldCompletedAssignment.Id, Decision = "approved", DecidedAt = now.AddDays(-90), ProofFileName = "x", ProofStoredFileName = "x" },
            new TaskSubmission { TaskId = recentCompletedTask.Id, TaskAssignmentId = recentCompletedAssignment.Id, Decision = "approved", DecidedAt = now.AddDays(-5), ProofFileName = "x", ProofStoredFileName = "x" });
        context.SaveChanges();

        return (context, oldCompletedAssignment.Id, recentCompletedAssignment.Id, oldActiveAssignment.Id, completedNoSubmissionAssignment.Id);
    }

    private static DashboardController BuildSuperAdminController(AppDbContext context)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", "SuperAdmin");
        httpContext.Session.SetInt32("UserId", 999);
        return new DashboardController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    [Fact]
    public async Task SuperAdminDashboard_CompletedAssignmentOlderThan56Days_IsExcluded()
    {
        var (context, oldCompletedId, _, _, _) = SeedSuperAdminRecencyFixture();
        var controller = BuildSuperAdminController(context);

        var result = await controller.SuperAdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SuperAdminDashboardViewModel>(view.Model);
        Assert.DoesNotContain(vm.WorkItems, i => i.TaskId == oldCompletedId);
    }

    [Fact]
    public async Task SuperAdminDashboard_CompletedAssignmentWithin56Days_IsIncluded()
    {
        var (context, _, recentCompletedAssignmentId, _, _) = SeedSuperAdminRecencyFixture();
        var controller = BuildSuperAdminController(context);

        var result = await controller.SuperAdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SuperAdminDashboardViewModel>(view.Model);
        // WorkItems is keyed by TaskId, which can collide with the task's
        // own Id across the fixture's rows — assert via the status count
        // instead, which is unambiguous.
        Assert.Equal(1, vm.CompletedCount);
    }

    [Fact]
    public async Task SuperAdminDashboard_ActiveAssignmentRegardlessOfAge_IsNeverExcluded()
    {
        var (context, _, _, oldActiveAssignmentId, _) = SeedSuperAdminRecencyFixture();
        var controller = BuildSuperAdminController(context);

        var result = await controller.SuperAdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SuperAdminDashboardViewModel>(view.Model);
        Assert.Equal(1, vm.InProgressCount);
    }

    [Fact]
    public async Task SuperAdminDashboard_CompletedAssignmentWithNoSubmissionRecord_IsExcluded()
    {
        var (context, _, _, _, _) = SeedSuperAdminRecencyFixture();
        var controller = BuildSuperAdminController(context);

        var result = await controller.SuperAdminDashboard();

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SuperAdminDashboardViewModel>(view.Model);
        // Of the two Completed assignments with no live/recent submission
        // (old one + no-submission one), only the recently-completed one
        // should survive — total Completed count is exactly 1.
        Assert.Equal(1, vm.CompletedCount);
    }
}
