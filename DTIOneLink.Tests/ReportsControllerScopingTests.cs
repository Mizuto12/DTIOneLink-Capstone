using System.Reflection;
using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Regression coverage for ReportsController's private ScopedTasks(userId,
// isAdmin, isOfficeWide, department) method and IsAllowedRole(role) guard.
// ScopedTasks mirrors EmployeeController.AccessibleTasksQuery's scoping
// rules, invoked here via reflection since it's private.
public class ReportsControllerScopingTests
{
    // DatabaseHelper requires a connection string in its constructor, but
    // ScopedTasks/IsAllowedRole never touch it - an unreachable placeholder
    // is enough to construct the controller.
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,1;Database=unreachable;Connect Timeout=1;TrustServerCertificate=True;";

    private static ReportsController BuildController(AppDbContext context)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = UnreachableConnectionString
            })
            .Build();

        return new ReportsController(context, new DatabaseHelper(config), NullLogger<ReportsController>.Instance);
    }

    private static IQueryable<TaskItem> InvokeScopedTasks(ReportsController controller, int userId, bool isAdmin, bool isOfficeWide, string? department)
    {
        var method = typeof(ReportsController).GetMethod("ScopedTasks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (IQueryable<TaskItem>)method.Invoke(controller, new object?[] { userId, isAdmin, isOfficeWide, department })!;
    }

    private static bool InvokeIsAllowedRole(string? role)
    {
        var method = typeof(ReportsController).GetMethod("IsAllowedRole", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object?[] { role })!;
    }

    // Same seed shape as EmployeeControllerScopingTests: BDD/FAU
    // main+subtask pairs (subtask department derived via ParentTask), plus a
    // legacy CPD subtask whose department is derivable only via its
    // assignee's own Department.
    private static (AppDbContext Context, User EmployeeBdd, User EmployeeFau, User EmployeeCpd,
        TaskItem BddSubtask, TaskItem FauSubtask, TaskItem BddMain, TaskItem FauMain, TaskItem CpdLegacySubtask) Seed()
    {
        var context = InMemoryDbContextFactory.Create();

        var employeeBdd = new User { Username = "ebdd", PasswordHash = "x", FullName = "Employee BDD", Role = "Employee", Department = "BDD" };
        var employeeFau = new User { Username = "efau", PasswordHash = "x", FullName = "Employee FAU", Role = "Employee", Department = "FAU" };
        var employeeCpd = new User { Username = "ecpd", PasswordHash = "x", FullName = "Employee CPD", Role = "Employee", Department = "CPD" };
        context.Users.AddRange(employeeBdd, employeeFau, employeeCpd);
        context.SaveChanges();

        var bddMain = new TaskItem { TaskName = "BDD main", TaskLevel = TaskLevels.Main, OwningDepartment = "BDD", Description = "d", DueDate = DateTime.UtcNow };
        var fauMain = new TaskItem { TaskName = "FAU main", TaskLevel = TaskLevels.Main, OwningDepartment = "FAU", Description = "d", DueDate = DateTime.UtcNow };
        context.TaskItems.AddRange(bddMain, fauMain);
        context.SaveChanges();

        var bddSubtask = new TaskItem { TaskName = "BDD subtask", TaskLevel = TaskLevels.Subtask, ParentTaskId = bddMain.Id, Description = "d", DueDate = DateTime.UtcNow };
        var fauSubtask = new TaskItem { TaskName = "FAU subtask", TaskLevel = TaskLevels.Subtask, ParentTaskId = fauMain.Id, Description = "d", DueDate = DateTime.UtcNow };
        var cpdLegacySubtask = new TaskItem { TaskName = "CPD legacy subtask", TaskLevel = TaskLevels.Subtask, Description = "d", DueDate = DateTime.UtcNow };
        context.TaskItems.AddRange(bddSubtask, fauSubtask, cpdLegacySubtask);
        context.SaveChanges();

        context.TaskAssignments.Add(new TaskAssignment { TaskId = bddSubtask.Id, UserId = employeeBdd.Id });
        context.TaskAssignments.Add(new TaskAssignment { TaskId = fauSubtask.Id, UserId = employeeFau.Id });
        context.TaskAssignments.Add(new TaskAssignment { TaskId = cpdLegacySubtask.Id, UserId = employeeCpd.Id });
        context.SaveChanges();

        return (context, employeeBdd, employeeFau, employeeCpd, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask);
    }

    [Fact]
    public void ScopedTasks_OfficeWide_ReturnsEverythingRegardlessOfAdminOrDepartment()
    {
        var (context, employeeBdd, _, _, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask) = Seed();
        var controller = BuildController(context);

        var ids = InvokeScopedTasks(controller, employeeBdd.Id, isAdmin: false, isOfficeWide: true, department: "BDD")
            .Select(t => t.Id).ToList();

        Assert.Equal(5, ids.Count);
        Assert.Contains(bddSubtask.Id, ids);
        Assert.Contains(fauSubtask.Id, ids);
        Assert.Contains(bddMain.Id, ids);
        Assert.Contains(fauMain.Id, ids);
        Assert.Contains(cpdLegacySubtask.Id, ids);
    }

    [Fact]
    public void ScopedTasks_Admin_ReturnsOnlyOwnDepartmentTasks()
    {
        var (context, _, _, _, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask) = Seed();
        var controller = BuildController(context);

        // userId 0 belongs to nobody - isolates the department-membership
        // branch from the "own assignment" branch.
        var ids = InvokeScopedTasks(controller, userId: 0, isAdmin: true, isOfficeWide: false, department: "BDD")
            .Select(t => t.Id).ToList();

        Assert.Contains(bddMain.Id, ids);
        Assert.Contains(bddSubtask.Id, ids);
        Assert.DoesNotContain(fauMain.Id, ids);
        Assert.DoesNotContain(fauSubtask.Id, ids);
        Assert.DoesNotContain(cpdLegacySubtask.Id, ids);
    }

    [Fact]
    public void ScopedTasks_Admin_StillSeesOwnAssignmentOutsideDepartment()
    {
        // The "own assignment" clause is OR'd in regardless of department,
        // same as EmployeeController.AccessibleTasksQuery - an Admin
        // assigned to a FAU task should see it even while scoped to BDD.
        var (context, _, employeeFau, _, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask) = Seed();
        var controller = BuildController(context);

        var ids = InvokeScopedTasks(controller, employeeFau.Id, isAdmin: true, isOfficeWide: false, department: "BDD")
            .Select(t => t.Id).ToList();

        Assert.Contains(fauSubtask.Id, ids);
        Assert.Contains(bddMain.Id, ids);
        Assert.Contains(bddSubtask.Id, ids);
        Assert.DoesNotContain(fauMain.Id, ids);
        Assert.DoesNotContain(cpdLegacySubtask.Id, ids);
    }

    [Fact]
    public void ScopedTasks_NeitherFlagSet_ReturnsOnlyTasksTheUserIsAssignedTo()
    {
        var (context, employeeBdd, _, _, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask) = Seed();
        var controller = BuildController(context);

        var ids = InvokeScopedTasks(controller, employeeBdd.Id, isAdmin: false, isOfficeWide: false, department: "BDD")
            .Select(t => t.Id).ToList();

        Assert.Single(ids);
        Assert.Contains(bddSubtask.Id, ids);
        Assert.DoesNotContain(fauSubtask.Id, ids);
        Assert.DoesNotContain(bddMain.Id, ids);
        Assert.DoesNotContain(fauMain.Id, ids);
        Assert.DoesNotContain(cpdLegacySubtask.Id, ids);
    }

    [Fact]
    public void ScopedTasks_NeitherFlagSet_IgnoresDepartmentEntirely()
    {
        // Even if a department string is passed, the plain-employee branch
        // (!isAdmin) must not use it - only the caller's own assignment
        // rows matter. This mirrors an IDOR concern: a non-admin can't see
        // a same-department coworker's task just because they share a dept.
        var (context, employeeBdd, _, _, bddSubtask, _, bddMain, _, _) = Seed();
        var controller = BuildController(context);

        var ids = InvokeScopedTasks(controller, employeeBdd.Id, isAdmin: false, isOfficeWide: false, department: "BDD")
            .Select(t => t.Id).ToList();

        // bddMain has OwningDepartment=BDD and no assignments; a plain
        // employee must not see it even though it's their department.
        Assert.DoesNotContain(bddMain.Id, ids);
        Assert.Contains(bddSubtask.Id, ids);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    [InlineData("employee")]
    [InlineData("Employee")]
    [InlineData("EMPLOYEE")]
    public void IsAllowedRole_AdminAndEmployee_AreAllowedCaseInsensitively(string role)
    {
        Assert.True(InvokeIsAllowedRole(role));
    }

    [Theory]
    [InlineData("Guest")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("random-role")]
    public void IsAllowedRole_UnknownRoles_AreRejected(string? role)
    {
        Assert.False(InvokeIsAllowedRole(role));
    }

    [Fact]
    public void IsAllowedRole_SuperAdmin_IsAllowedViaViewOfficeWideSummariesPermission()
    {
        // Not an Admin/Employee string match - allowed only because
        // RolePermissions grants SuperAdmin the ViewOfficeWideSummaries
        // permission.
        Assert.True(InvokeIsAllowedRole("SuperAdmin"));
    }

    [Fact]
    public void IsAllowedRole_Supervisor_IsNotAllowed()
    {
        // Supervisor has no RolePermissions entry of its own and isn't
        // "Admin" or "Employee" by string, so it falls outside Reports.
        Assert.False(InvokeIsAllowedRole("Supervisor"));
    }
}
