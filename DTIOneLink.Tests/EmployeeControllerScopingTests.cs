using System.Reflection;
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

// Regression coverage for EmployeeController's task-scoping rules: an
// Employee only ever sees tasks they personally hold a TaskAssignment row
// on; a department-scoped Admin/Supervisor only sees their own department's
// tasks (own OwningDepartment, falling back to the parent task's
// OwningDepartment, falling back to any assignee's Department); and nobody
// can read or act on an out-of-scope task by passing its id directly (IDOR).
public class EmployeeControllerScopingTests
{
    private static EmployeeController BuildController(AppDbContext context, string? role, string? department, int? userId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (role != null) httpContext.Session.SetString("UserRole", role);
        if (department != null) httpContext.Session.SetString("UserDepartment", department);
        if (userId != null) httpContext.Session.SetInt32("UserId", userId.Value);

        var config = new ConfigurationBuilder().Build();
        var controller = new EmployeeController(
            context,
            new TaskAssignmentService(context),
            new NotificationService(context, config),
            NullLogger<EmployeeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
        return controller;
    }

    // Seeds three departments (BDD, FAU, CPD) with users and tasks:
    //  - Task 1 ("BDD subtask"): OwningDepartment=null, ParentTask=Task 3 (BDD main), assignee=employeeBdd
    //  - Task 2 ("FAU subtask"): OwningDepartment=null, ParentTask=Task 4 (FAU main), assignee=employeeFau
    //  - Task 3 ("BDD main"): TaskLevel=Main, OwningDepartment=BDD, no assignments
    //  - Task 4 ("FAU main"): TaskLevel=Main, OwningDepartment=FAU, no assignments
    //  - Task 5 ("CPD legacy subtask"): OwningDepartment=null, ParentTask=null, assignee=employeeCpd
    //    (department derived only from the assignee's own Department — the
    //    legacy fallback tier)
    private static (AppDbContext Context, User EmployeeBdd, User EmployeeFau, User EmployeeCpd, User AdminBdd,
        TaskItem BddSubtask, TaskItem FauSubtask, TaskItem BddMain, TaskItem FauMain, TaskItem CpdLegacySubtask) Seed()
    {
        var context = InMemoryDbContextFactory.Create();

        var employeeBdd = new User { Username = "ebdd", PasswordHash = "x", FullName = "Employee BDD", Role = "Employee", Department = "BDD" };
        var employeeFau = new User { Username = "efau", PasswordHash = "x", FullName = "Employee FAU", Role = "Employee", Department = "FAU" };
        var employeeCpd = new User { Username = "ecpd", PasswordHash = "x", FullName = "Employee CPD", Role = "Employee", Department = "CPD" };
        var adminBdd = new User { Username = "abdd", PasswordHash = "x", FullName = "Admin BDD", Role = "Admin", Department = "BDD" };
        context.Users.AddRange(employeeBdd, employeeFau, employeeCpd, adminBdd);
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

        return (context, employeeBdd, employeeFau, employeeCpd, adminBdd, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask);
    }

    [Fact]
    public async Task Index_Employee_OnlySeesTasksTheyAreAssignedTo()
    {
        var (context, employeeBdd, _, _, _, bddSubtask, _, _, _, _) = Seed();
        var controller = BuildController(context, "Employee", "BDD", employeeBdd.Id);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        Assert.Single(tasks);
        Assert.Equal(bddSubtask.Id, tasks[0].Id);
    }

    [Fact]
    public async Task Index_DepartmentAdmin_OnlySeesOwnDepartmentTasks()
    {
        var (context, _, _, employeeCpd, adminBdd, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask) = Seed();
        var controller = BuildController(context, "Admin", "BDD", adminBdd.Id);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var tasks = Assert.IsAssignableFrom<List<TaskItem>>(view.Model);
        var ids = tasks.Select(t => t.Id).ToList();

        // Sees the BDD main task (own OwningDepartment) and the BDD subtask
        // (derived via its parent's OwningDepartment).
        Assert.Contains(bddMain.Id, ids);
        Assert.Contains(bddSubtask.Id, ids);
        // Does not see FAU's tasks or the CPD legacy subtask (different
        // department, derived via the assignee fallback).
        Assert.DoesNotContain(fauMain.Id, ids);
        Assert.DoesNotContain(fauSubtask.Id, ids);
        Assert.DoesNotContain(cpdLegacySubtask.Id, ids);
    }

    [Fact]
    public async Task Details_Employee_CannotOpenAnotherEmployeesTaskByGuessingId()
    {
        // IDOR regression: an Employee assigned to the BDD subtask must not
        // be able to open the FAU subtask just by passing its id directly.
        var (context, employeeBdd, _, _, _, _, fauSubtask, _, _, _) = Seed();
        var controller = BuildController(context, "Employee", "BDD", employeeBdd.Id);

        var result = await controller.Details(fauSubtask.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_Employee_CanOpenTheirOwnAssignedTask()
    {
        var (context, employeeBdd, _, _, _, bddSubtask, _, _, _, _) = Seed();
        var controller = BuildController(context, "Employee", "BDD", employeeBdd.Id);

        var result = await controller.Details(bddSubtask.Id);

        var view = Assert.IsType<ViewResult>(result);
        var task = Assert.IsType<TaskItem>(view.Model);
        Assert.Equal(bddSubtask.Id, task.Id);
    }

    [Fact]
    public async Task Details_DepartmentAdmin_CannotOpenOutOfDepartmentSubtaskByGuessingId()
    {
        // IDOR regression specific to the fix noted in the controller
        // comments: a department-scoped Admin must not be able to open an
        // out-of-department subtask directly by id, even though it's
        // reachable via ParentTask.OwningDepartment for an in-department one.
        var (context, _, _, _, adminBdd, _, fauSubtask, _, _, _) = Seed();
        var controller = BuildController(context, "Admin", "BDD", adminBdd.Id);

        var result = await controller.Details(fauSubtask.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_DepartmentAdmin_CanOpenOwnDepartmentSubtaskViaParent()
    {
        var (context, _, _, _, adminBdd, bddSubtask, _, _, _, _) = Seed();
        var controller = BuildController(context, "Admin", "BDD", adminBdd.Id);

        var result = await controller.Details(bddSubtask.Id);

        var view = Assert.IsType<ViewResult>(result);
        var task = Assert.IsType<TaskItem>(view.Model);
        Assert.Equal(bddSubtask.Id, task.Id);
    }

    [Fact]
    public async Task Details_DepartmentAdmin_CannotOpenOtherDepartmentsLegacyAssigneeTask()
    {
        // The legacy assignee-department fallback only applies when both
        // OwningDepartment and ParentTask.OwningDepartment are null — but it
        // still must not leak a CPD employee's task to a BDD Admin.
        var (context, _, _, _, adminBdd, _, _, _, _, cpdLegacySubtask) = Seed();
        var controller = BuildController(context, "Admin", "BDD", adminBdd.Id);

        var result = await controller.Details(cpdLegacySubtask.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateProgress_Employee_CannotUpdateATaskTheyAreNotAssignedTo()
    {
        // IDOR check on a POST action, not just a read: posting a foreign
        // task's id must not let an Employee touch its progress.
        var (context, employeeBdd, _, _, _, _, fauSubtask, _, _, _) = Seed();
        var controller = BuildController(context, "Employee", "BDD", employeeBdd.Id);

        var result = await controller.UpdateProgress(fauSubtask.Id, 50);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void AccessibleTasksQuery_SuperAdmin_SeesEverything()
    {
        var (context, _, _, _, _, bddSubtask, fauSubtask, bddMain, fauMain, cpdLegacySubtask) = Seed();
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", "SuperAdmin");
        httpContext.Session.SetInt32("UserId", 999);

        var config = new ConfigurationBuilder().Build();
        var controller = new EmployeeController(context, new TaskAssignmentService(context), new NotificationService(context, config), NullLogger<EmployeeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var method = typeof(EmployeeController).GetMethod("AccessibleTasksQuery", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var query = (IQueryable<TaskItem>)method.Invoke(controller, null)!;
        var ids = query.Select(t => t.Id).ToList();

        Assert.Equal(5, ids.Count);
        Assert.Contains(bddSubtask.Id, ids);
        Assert.Contains(fauSubtask.Id, ids);
        Assert.Contains(bddMain.Id, ids);
        Assert.Contains(fauMain.Id, ids);
        Assert.Contains(cpdLegacySubtask.Id, ids);
    }
}
