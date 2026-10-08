using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DTIOneLink.Tests;

public class NotificationServiceTests
{
    private static NotificationService BuildService(DTIOneLink.Data.AppDbContext db, string? baseUrl = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = baseUrl })
            .Build();
        return new NotificationService(db, config);
    }

    private static User ActiveConfirmedUser(int id) => new()
    {
        Id = id, FullName = "Juan", Email = $"user{id}@dti.gov.ph",
        Username = $"user{id}", PasswordHash = "x", IsActive = true, EmailConfirmed = true
    };

    [Fact]
    public async Task CreateAsync_MessageOver500Chars_IsTruncatedWithEllipsis()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        var longMessage = new string('a', 600);
        var notif = await service.CreateAsync(1, NotificationType.Task, longMessage);

        Assert.Equal(500, notif.Message.Length);
        Assert.EndsWith("...", notif.Message);
    }

    [Fact]
    public async Task CreateAsync_WithEmailSubject_QueuesOutboxEmailForActiveConfirmedUser()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        await service.CreateAsync(1, NotificationType.Task, "msg", emailSubject: "Subject line");

        var outbox = await db.EmailOutbox.SingleAsync();
        Assert.Equal("user1@dti.gov.ph", outbox.ToEmail);
        Assert.Equal("Subject line", outbox.Subject);
    }

    [Fact]
    public async Task CreateAsync_WithEmailSubject_RecipientNotEmailConfirmed_NoEmailQueued()
    {
        // Notification emails only go to confirmed addresses, so a mistyped
        // email can't hand task details to a stranger.
        using var db = InMemoryDbContextFactory.Create();
        var user = ActiveConfirmedUser(1);
        user.EmailConfirmed = false;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = BuildService(db);

        await service.CreateAsync(1, NotificationType.Task, "msg", emailSubject: "Subject line");

        Assert.Empty(db.EmailOutbox);
        Assert.Single(db.Notifications); // the in-app notification is still created
    }

    [Fact]
    public async Task CreateAsync_WithEmailSubject_InactiveRecipient_NoEmailQueued()
    {
        using var db = InMemoryDbContextFactory.Create();
        var user = ActiveConfirmedUser(1);
        user.IsActive = false;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = BuildService(db);

        await service.CreateAsync(1, NotificationType.Task, "msg", emailSubject: "Subject line");

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task CreateAsync_NoEmailSubject_NeverQueuesEmail()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        await service.CreateAsync(1, NotificationType.Task, "msg");

        Assert.Empty(db.EmailOutbox);
    }

    [Fact]
    public async Task NotifyTaskDueSoonAsync_CalledTwice_SecondCallIsIdempotent()
    {
        // ExistsAsync guard stops a duplicate DueSoon notification for the
        // same task/user from being created every time the reminder runs.
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        await service.NotifyTaskDueSoonAsync(1, taskId: 5, "Report", DateTime.Today.AddDays(2));
        await service.NotifyTaskDueSoonAsync(1, taskId: 5, "Report", DateTime.Today.AddDays(2));

        var count = await db.Notifications.CountAsync(n => n.Type == NotificationType.DueSoon && n.RelatedTaskId == 5);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NotifyTaskOverdueAsync_CalledTwice_SecondCallIsIdempotent()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        await service.NotifyTaskOverdueAsync(1, taskId: 5, "Report", DateTime.Today.AddDays(-1));
        await service.NotifyTaskOverdueAsync(1, taskId: 5, "Report", DateTime.Today.AddDays(-1));

        var count = await db.Notifications.CountAsync(n => n.Type == NotificationType.Overdue && n.RelatedTaskId == 5);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NotifyTaskOverdueAsync_AdminCopy_LinksToEditorNotEmployeeDetails()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);

        await service.NotifyTaskOverdueAsync(1, taskId: 5, "Report", DateTime.Today.AddDays(-1), isAdminCopy: true);

        var notif = await db.Notifications.SingleAsync();
        Assert.Equal("/Tasks/Edit/5", notif.Link);
        Assert.Contains("still incomplete", notif.Message);
    }

    [Fact]
    public async Task ResetDeadlineRemindersAsync_RemovesStaleDueSoonAndOverdueNotifications()
    {
        // Called when a due date changes, so a new date can trigger fresh
        // due-soon/overdue notices (the ExistsAsync guard would otherwise
        // permanently block them for this task).
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);
        await service.NotifyTaskDueSoonAsync(1, taskId: 5, "Report", DateTime.Today);
        await service.NotifyTaskOverdueAsync(1, taskId: 5, "Report", DateTime.Today);
        // An unrelated notification type on the same task must survive.
        await service.NotifyTaskAssignedAsync(1, taskId: 5, "Report");

        await service.ResetDeadlineRemindersAsync(5);

        Assert.Equal(0, await db.Notifications.CountAsync(n => n.Type == NotificationType.DueSoon));
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.Type == NotificationType.Overdue));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.Task));
    }

    [Fact]
    public async Task NotifyRecordDisposalDueAsync_AlreadyNotified_DoesNotCreateDuplicate()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var today = DateTime.Today;

        await service.NotifyRecordDisposalDueAsync(1, recordId: 7, "REC-01", "Title", today.AddDays(30), today);
        await service.NotifyRecordDisposalDueAsync(1, recordId: 7, "REC-01", "Title", today.AddDays(30), today);

        var count = await db.Notifications.CountAsync(n => n.Type == NotificationType.RecordDisposalDue && n.RelatedRecordId == 7);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NotifyRecordDisposalDueAsync_FutureDueDate_UsesReachesWording()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var today = DateTime.Today;

        await service.NotifyRecordDisposalDueAsync(1, recordId: 7, "REC-01", "Title", today.AddDays(30), today);

        var notif = await db.Notifications.SingleAsync();
        Assert.Contains("reaches the end", notif.Message);
    }

    [Fact]
    public async Task NotifyRecordDisposalDueAsync_PastDueDate_UsesReachedWording()
    {
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var today = DateTime.Today;

        await service.NotifyRecordDisposalDueAsync(1, recordId: 7, "REC-01", "Title", today.AddDays(-10), today);

        var notif = await db.Notifications.SingleAsync();
        Assert.Contains("reached the end", notif.Message);
    }

    [Fact]
    public async Task NotifyRecordDisposalDueAsync_EmailBodyOmitsRecordTitle()
    {
        // Records can be confidential; the email leaves out the title even
        // though the in-app message includes it.
        using var db = InMemoryDbContextFactory.Create();
        db.Users.Add(ActiveConfirmedUser(1));
        await db.SaveChangesAsync();
        var service = BuildService(db);
        var today = DateTime.Today;

        await service.NotifyRecordDisposalDueAsync(1, recordId: 7, "REC-01", "Confidential Title Here", today.AddDays(30), today);

        var outbox = await db.EmailOutbox.SingleAsync();
        Assert.DoesNotContain("Confidential Title Here", outbox.TextBody);
        Assert.DoesNotContain("Confidential Title Here", outbox.HtmlBody);
        Assert.Contains("REC-01", outbox.TextBody);
    }

    // ── ResolveOpdRecipients (static, pure logic) ──────────────────────

    [Fact]
    public void ResolveOpdRecipients_MainTaskCreatedByActiveSuperAdmin_ReturnsOnlyCreator()
    {
        var task = new TaskItem { TaskLevel = TaskLevels.Main, CreatedByUserId = 42 };
        var result = NotificationService.ResolveOpdRecipients(task, new List<int> { 42, 99 });
        Assert.Equal(new List<int> { 42 }, result);
    }

    [Fact]
    public void ResolveOpdRecipients_CreatorNoLongerActiveSuperAdmin_FallsBackToAllActiveSuperAdmins()
    {
        // The creator might have been demoted/deactivated since issuing the
        // task — falls back so a Direct Admin proof never has nobody to review it.
        var task = new TaskItem { TaskLevel = TaskLevels.Main, CreatedByUserId = 42 };
        var result = NotificationService.ResolveOpdRecipients(task, new List<int> { 99, 100 });
        Assert.Equal(new List<int> { 99, 100 }, result);
    }

    [Fact]
    public void ResolveOpdRecipients_SubtaskOfMainTask_UsesParentsCreator()
    {
        var parent = new TaskItem { TaskLevel = TaskLevels.Main, CreatedByUserId = 42 };
        var subtask = new TaskItem { TaskLevel = TaskLevels.Subtask, ParentTaskId = 1, ParentTask = parent };
        var result = NotificationService.ResolveOpdRecipients(subtask, new List<int> { 42 });
        Assert.Equal(new List<int> { 42 }, result);
    }

    [Fact]
    public void ResolveOpdRecipients_StandaloneSubtaskNotOpdIssued_ReturnsEmpty()
    {
        var task = new TaskItem { TaskLevel = TaskLevels.Subtask, ParentTaskId = null };
        var result = NotificationService.ResolveOpdRecipients(task, new List<int> { 42 });
        Assert.Empty(result);
    }

    // ── ResolveEffectiveDepartment (static, pure logic) ────────────────

    [Fact]
    public void ResolveEffectiveDepartment_PrefersTasksOwnDepartment()
    {
        var task = new TaskItem { OwningDepartment = "BDD", ParentTask = new TaskItem { OwningDepartment = "FAU" } };
        Assert.Equal("BDD", NotificationService.ResolveEffectiveDepartment(task, "CPD"));
    }

    [Fact]
    public void ResolveEffectiveDepartment_FallsBackToParentMainTaskDepartment()
    {
        var task = new TaskItem { OwningDepartment = null, ParentTask = new TaskItem { OwningDepartment = "FAU" } };
        Assert.Equal("FAU", NotificationService.ResolveEffectiveDepartment(task, "CPD"));
    }

    [Fact]
    public void ResolveEffectiveDepartment_FallsBackToLegacyAssigneeDepartment()
    {
        var task = new TaskItem { OwningDepartment = null, ParentTask = null };
        Assert.Equal("CPD", NotificationService.ResolveEffectiveDepartment(task, "CPD"));
    }

    [Fact]
    public void ResolveEffectiveDepartment_NothingAvailable_ReturnsNull()
    {
        var task = new TaskItem { OwningDepartment = null, ParentTask = null };
        Assert.Null(NotificationService.ResolveEffectiveDepartment(task, null));
    }

    // ── GetDepartmentAdminIdsAsync ──────────────────────────────────────

    [Fact]
    public async Task GetDepartmentAdminIdsAsync_ReturnsActiveAdminsAndSupervisorsInDepartmentOnly()
    {
        using var db = InMemoryDbContextFactory.Create();
        var task = new TaskItem { TaskName = "t", Description = "d", OwningDepartment = "BDD" };
        db.TaskItems.Add(task);
        db.Users.AddRange(
            new User { Id = 1, FullName = "A", Role = "Admin", Department = "BDD", IsActive = true, Username = "a", PasswordHash = "x" },
            new User { Id = 2, FullName = "B", Role = "Supervisor", Department = "BDD", IsActive = true, Username = "b", PasswordHash = "x" },
            new User { Id = 3, FullName = "C", Role = "Admin", Department = "FAU", IsActive = true, Username = "c", PasswordHash = "x" }, // other dept
            new User { Id = 4, FullName = "D", Role = "Employee", Department = "BDD", IsActive = true, Username = "d", PasswordHash = "x" }, // wrong role
            new User { Id = 5, FullName = "E", Role = "Admin", Department = "BDD", IsActive = false, Username = "e", PasswordHash = "x" }); // inactive
        await db.SaveChangesAsync();

        var service = BuildService(db);
        var ids = await service.GetDepartmentAdminIdsAsync(task.Id);

        Assert.Equal(new List<int> { 1, 2 }, ids.OrderBy(i => i).ToList());
    }

    [Fact]
    public async Task NotifyAdminsProofSubmittedAsync_ExcludesNobody_NotifiesEveryDepartmentAdmin()
    {
        using var db = InMemoryDbContextFactory.Create();
        var task = new TaskItem { TaskName = "t", Description = "d", OwningDepartment = "BDD" };
        db.TaskItems.Add(task);
        db.Users.AddRange(
            new User { Id = 1, FullName = "A", Role = "Admin", Department = "BDD", IsActive = true, Username = "a", PasswordHash = "x" },
            new User { Id = 2, FullName = "B", Role = "Admin", Department = "BDD", IsActive = true, Username = "b", PasswordHash = "x" });
        await db.SaveChangesAsync();

        var service = BuildService(db);
        await service.NotifyAdminsProofSubmittedAsync(task.Id, "Report", "Employee X", submissionId: 9);

        Assert.Equal(2, await db.Notifications.CountAsync());
        Assert.All(await db.Notifications.ToListAsync(), n => Assert.Equal("/Tasks/Review/9", n.Link));
    }

    // ── NotifyOpdDirectiveCompletedAsync guard conditions ───────────────

    [Fact]
    public async Task NotifyOpdDirectiveCompletedAsync_TaskNotCompleted_SendsNothing()
    {
        using var db = InMemoryDbContextFactory.Create();
        var task = new TaskItem
        {
            TaskName = "t", Description = "d", TaskLevel = TaskLevels.Main,
            TaskType = TaskTypes.DepartmentDirective, Status = TaskWorkflow.InProgress, CreatedByUserId = 42
        };
        db.TaskItems.Add(task);
        db.Users.Add(new User { Id = 42, FullName = "SA", Role = "SuperAdmin", IsActive = true, Username = "sa", PasswordHash = "x" });
        await db.SaveChangesAsync();

        var service = BuildService(db);
        await service.NotifyOpdDirectiveCompletedAsync(task.Id);

        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task NotifyOpdDirectiveCompletedAsync_OpdIssuedTaskType_SendsNothing()
    {
        // This notice is only for Department Directives — a Direct Admin
        // Task or Whole Office task (both IsAssignedByOpd) uses a different
        // notification path (NotifyOpdProofSubmittedAsync/approval flow).
        using var db = InMemoryDbContextFactory.Create();
        var task = new TaskItem
        {
            TaskName = "t", Description = "d", TaskLevel = TaskLevels.Main,
            TaskType = TaskTypes.DirectAdmin, Status = TaskWorkflow.Completed, CreatedByUserId = 42
        };
        db.TaskItems.Add(task);
        db.Users.Add(new User { Id = 42, FullName = "SA", Role = "SuperAdmin", IsActive = true, Username = "sa", PasswordHash = "x" });
        await db.SaveChangesAsync();

        var service = BuildService(db);
        await service.NotifyOpdDirectiveCompletedAsync(task.Id);

        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task NotifyOpdDirectiveCompletedAsync_CompletedDepartmentDirective_NotifiesCreatorOnce()
    {
        using var db = InMemoryDbContextFactory.Create();
        var task = new TaskItem
        {
            TaskName = "Directive", Description = "d", TaskLevel = TaskLevels.Main,
            TaskType = TaskTypes.DepartmentDirective, Status = TaskWorkflow.Completed, CreatedByUserId = 42
        };
        db.TaskItems.Add(task);
        db.Users.Add(new User { Id = 42, FullName = "SA", Role = "SuperAdmin", IsActive = true, Username = "sa", PasswordHash = "x" });
        await db.SaveChangesAsync();

        var service = BuildService(db);
        await service.NotifyOpdDirectiveCompletedAsync(task.Id);
        await service.NotifyOpdDirectiveCompletedAsync(task.Id); // fires once per recipient per directive

        var count = await db.Notifications.CountAsync(n => n.Type == NotificationType.Completed);
        Assert.Equal(1, count);
    }
}
