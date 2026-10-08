using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DTIOneLink.Tests;

public class OpdTaskServiceTests
{
    private static OpdTaskService BuildService(DTIOneLink.Data.AppDbContext db)
    {
        var config = new ConfigurationBuilder().Build();
        var notifications = new NotificationService(db, config);
        var assignments = new TaskAssignmentService(db);
        return new OpdTaskService(db, assignments, notifications);
    }

    private static User Staffer(int id, string role, bool active = true) => new()
    {
        Id = id, FullName = $"Staff {id}", Role = role, IsActive = active,
        Username = $"staff{id}", PasswordHash = "x"
    };

    private static OpdTaskService.NewOpdTask WholeOfficeSpec() => new(
        TaskName: "Flag Raising", Description: "desc", DueDate: DateTime.Today.AddDays(7),
        Priority: "medium", TaskType: TaskTypes.WholeOffice, OwningDepartment: "BDD",
        ResponsibleAdminUserId: null, SubtaskNames: new List<string>(), Recurrence: null);

    private static OpdTaskService.NewOpdTask DirectAdminSpec(int responsibleAdminId) => new(
        TaskName: "Submit Report", Description: "desc", DueDate: DateTime.Today.AddDays(7),
        Priority: "high", TaskType: TaskTypes.DirectAdmin, OwningDepartment: "BDD",
        ResponsibleAdminUserId: responsibleAdminId, SubtaskNames: new List<string>(), Recurrence: null);

    private static OpdTaskService.NewOpdTask DepartmentDirectiveSpec(int responsibleAdminId, params string[] subtaskNames) => new(
        TaskName: "Directive", Description: "desc", DueDate: DateTime.Today.AddDays(7),
        Priority: "medium", TaskType: TaskTypes.DepartmentDirective, OwningDepartment: "BDD",
        ResponsibleAdminUserId: responsibleAdminId, SubtaskNames: subtaskNames, Recurrence: null);

    [Fact]
    public async Task CreateAsync_WholeOffice_AssignsEveryActiveAdminAndEmployeeExceptSuperAdmin()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.AddRange(
            Staffer(1, "Admin"),
            Staffer(2, "Employee"),
            Staffer(3, "SuperAdmin"), // the OPD itself — must be excluded
            Staffer(4, "Employee", active: false)); // inactive — excluded
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var (mainTask, subtaskCount) = await service.CreateAsync(WholeOfficeSpec(), createdByUserId: 3, "OPD created this.");

        Assert.Equal(0, subtaskCount);
        var assignedUserIds = await db.TaskAssignments.Where(a => a.TaskId == mainTask.Id).Select(a => a.UserId).ToListAsync();
        Assert.Equal(new List<int> { 1, 2 }, assignedUserIds.OrderBy(i => i).ToList());
    }

    [Fact]
    public async Task CreateAsync_WholeOffice_NotifiesEveryAssignedStaffer()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.AddRange(Staffer(1, "Admin"), Staffer(2, "Employee"));
        await db.SaveChangesAsync();

        var service = BuildService(db);
        await service.CreateAsync(WholeOfficeSpec(), createdByUserId: 3, "note");

        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WholeOffice_LogsCreatedAndAssignedActivities()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(Staffer(1, "Employee"));
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var (mainTask, _) = await service.CreateAsync(WholeOfficeSpec(), createdByUserId: 3, "OPD note");

        var activities = await db.TaskActivities.Where(a => a.TaskId == mainTask.Id).ToListAsync();
        Assert.Equal(2, activities.Count);
        Assert.Contains(activities, a => a.ActivityType == "created");
        Assert.Contains(activities, a => a.ActivityType == TaskActivityType.Assigned);
    }

    [Fact]
    public async Task CreateAsync_DirectAdmin_AssignsOnlyTheResponsibleAdmin()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(Staffer(10, "Admin"));
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var (mainTask, subtaskCount) = await service.CreateAsync(DirectAdminSpec(10), createdByUserId: 3, "note");

        Assert.Equal(0, subtaskCount);
        var assignment = await db.TaskAssignments.SingleAsync(a => a.TaskId == mainTask.Id);
        Assert.Equal(10, assignment.UserId);
        Assert.True(assignment.IsPrimaryAssignee);
        Assert.Equal(10, mainTask.AssigneeId);
    }

    [Fact]
    public async Task CreateAsync_DirectAdmin_NotifiesOnlyTheResponsibleAdmin()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(Staffer(10, "Admin"));
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var (mainTask, _) = await service.CreateAsync(DirectAdminSpec(10), createdByUserId: 3, "note");

        var notif = await db.Notifications.SingleAsync();
        Assert.Equal(10, notif.RecipientUserId);
        Assert.Equal(mainTask.Id, notif.RelatedTaskId);
    }

    [Fact]
    public async Task CreateAsync_DepartmentDirective_CreatesUnassignedSubtasksInheritingDueDateAndDepartment()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(Staffer(10, "Admin"));
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var (mainTask, subtaskCount) = await service.CreateAsync(
            DepartmentDirectiveSpec(10, "Draft report", "Collect data"), createdByUserId: 3, "note");

        Assert.Equal(2, subtaskCount);
        var subtasks = await db.TaskItems.Where(t => t.ParentTaskId == mainTask.Id).ToListAsync();
        Assert.Equal(2, subtasks.Count);
        Assert.All(subtasks, s =>
        {
            Assert.Equal(TaskLevels.Subtask, s.TaskLevel);
            Assert.Equal(mainTask.OwningDepartment, s.OwningDepartment);
            Assert.Equal(mainTask.DueDate, s.DueDate);
            Assert.Null(s.ResponsibleAdminUserId);
        });
        Assert.Empty(await db.TaskAssignments.Where(a => a.TaskId == mainTask.Id).ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_DepartmentDirective_BlankSubtaskNamesAreSkipped()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(Staffer(10, "Admin"));
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var (_, subtaskCount) = await service.CreateAsync(
            DepartmentDirectiveSpec(10, "Draft report", "  ", "", null!), createdByUserId: 3, "note");

        Assert.Equal(1, subtaskCount);
    }

    [Fact]
    public async Task CreateAsync_DepartmentDirective_NotifiesTheResponsibleAdmin()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(Staffer(10, "Admin"));
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var (mainTask, _) = await service.CreateAsync(DepartmentDirectiveSpec(10, "Subtask A"), createdByUserId: 3, "note");

        var notif = await db.Notifications.SingleAsync();
        Assert.Equal(10, notif.RecipientUserId);
        Assert.Equal($"/Tasks/MainTaskDetails/{mainTask.Id}", notif.Link);
    }

    [Fact]
    public async Task AddSubtaskAsync_AddsSubtaskAndLogsActivity()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(Staffer(10, "Admin"));
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var (mainTask, _) = await service.CreateAsync(DepartmentDirectiveSpec(10), createdByUserId: 3, "note");

        var subtask = await service.AddSubtaskAsync(mainTask, "Extra subtask", createdByUserId: 3);

        Assert.Equal(mainTask.Id, subtask.ParentTaskId);
        Assert.Equal(TaskLevels.Subtask, subtask.TaskLevel);
        Assert.Contains(await db.TaskActivities.Where(a => a.TaskId == mainTask.Id).ToListAsync(),
            a => a.ActivityType == "updated" && a.Details.Contains("Extra subtask"));
    }
}
