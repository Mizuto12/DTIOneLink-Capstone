using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Tests;

public class TaskAssignmentServiceTests
{
    private static TaskItem NewTask(string taskType = TaskTypes.DirectAdmin) => new()
    {
        TaskName = "Submit Q1 Report",
        Description = "desc",
        TaskType = taskType,
    };

    // A fresh AppDbContext over a named InMemory database (rather than the
    // random-GUID-per-call InMemoryDbContextFactory.Create()), so two
    // contexts can share the same underlying store within one test — the
    // same way a real "Create" request and a later "Edit" request each get
    // their own scoped DbContext but read/write the same rows. Re-reading a
    // task through a second, freshly-queried context like this (rather than
    // reusing the first context's tracked `task` object) is what actually
    // matches production, and sidesteps EF's own relationship fixup
    // double-populating the original instance's in-memory Assignments list
    // when both the explicit `task.Assignments.Add(...)` in the service and
    // EF's automatic FK-based fixup add the same new row to it.
    private static AppDbContext CreateNamedDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task AssignEmployees_FirstUserBecomesPrimaryAndLegacyAssigneeId()
    {
        // Mirrors the old single-AssigneeId behavior for any legacy reader
        // of task.AssigneeId/task.Assignee — this is relied on across the
        // codebase, so the "first user wins" rule must hold exactly.
        using var db = InMemoryDbContextFactory.Create();
        var service = new TaskAssignmentService(db);
        var task = NewTask();
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();

        service.AssignEmployees(task, new List<int> { 10, 20, 30 }, assignedByUserId: 1);
        await db.SaveChangesAsync();

        Assert.Equal(10, task.AssigneeId);

        var persisted = await db.TaskAssignments.Where(a => a.TaskId == task.Id).ToListAsync();
        Assert.Equal(3, persisted.Count);
        Assert.True(persisted.Single(a => a.UserId == 10).IsPrimaryAssignee);
        Assert.False(persisted.Single(a => a.UserId == 20).IsPrimaryAssignee);
        Assert.False(persisted.Single(a => a.UserId == 30).IsPrimaryAssignee);
    }

    [Fact]
    public async Task AssignEmployees_DuplicateUserIds_OnlyOneAssignmentCreated()
    {
        using var db = InMemoryDbContextFactory.Create();
        var service = new TaskAssignmentService(db);
        var task = NewTask();
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();

        service.AssignEmployees(task, new List<int> { 10, 10, 20 }, assignedByUserId: 1);
        await db.SaveChangesAsync();

        var persisted = await db.TaskAssignments.Where(a => a.TaskId == task.Id).ToListAsync();
        Assert.Equal(2, persisted.Count);
    }

    [Fact]
    public async Task SyncAssignmentsAsync_RemovingAssigneeWithSubmissions_IsBlockedNotRemoved()
    {
        // An assignee who has already submitted proof can't be silently
        // dropped — that would orphan their submission history.
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateNamedDb(dbName);
        var task = NewTask();
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        new TaskAssignmentService(db).AssignEmployees(task, new List<int> { 10, 20 }, assignedByUserId: 1);
        await db.SaveChangesAsync();

        var assignmentForUser10 = await db.TaskAssignments.FirstAsync(a => a.TaskId == task.Id && a.UserId == 10);
        db.TaskSubmissions.Add(new TaskSubmission { TaskAssignmentId = assignmentForUser10.Id, TaskId = task.Id });
        await db.SaveChangesAsync();

        // Reload through a fresh context/service, exactly as the real
        // Edit action does, so task.Assignments reflects the two persisted
        // rows once each (not doubled by this test's own earlier fixup).
        using var editDb = CreateNamedDb(dbName);
        var editTask = await editDb.TaskItems.Include(t => t.Assignments).FirstAsync(t => t.Id == task.Id);
        var editService = new TaskAssignmentService(editDb);

        // Desired list drops both user 10 (has a submission) and user 20 (no submission).
        var result = await editService.SyncAssignmentsAsync(editTask, new List<int>(), changedByUserId: 1);

        Assert.Contains(10, result.BlockedRemovals);
        Assert.Contains(20, result.Removed);
        Assert.Contains(editTask.Assignments, a => a.UserId == 10); // kept
        Assert.DoesNotContain(editTask.Assignments, a => a.UserId == 20); // removed
    }

    [Fact]
    public async Task SyncAssignmentsAsync_AddingNewUser_CreatesPendingAssignment()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateNamedDb(dbName);
        var task = NewTask();
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        new TaskAssignmentService(db).AssignEmployees(task, new List<int> { 10 }, assignedByUserId: 1);
        await db.SaveChangesAsync();

        using var editDb = CreateNamedDb(dbName);
        var editTask = await editDb.TaskItems.Include(t => t.Assignments).FirstAsync(t => t.Id == task.Id);
        var service = new TaskAssignmentService(editDb);

        var result = await service.SyncAssignmentsAsync(editTask, new List<int> { 10, 99 }, changedByUserId: 2);
        await editDb.SaveChangesAsync();

        Assert.Contains(99, result.Added);
        // Query the persisted row rather than editTask.Assignments: adding a
        // brand-new TaskAssignment inside the same tracked context triggers
        // EF's own FK-based relationship fixup on top of the service's
        // explicit task.Assignments.Add(...), which would otherwise show the
        // same new row twice in the in-memory list.
        var added = await editDb.TaskAssignments.SingleAsync(a => a.TaskId == editTask.Id && a.UserId == 99);
        Assert.Equal(TaskWorkflow.Pending, added.Status);
        Assert.False(added.IsPrimaryAssignee);
    }

    [Fact]
    public async Task SyncAssignmentsAsync_RemovingPrimaryAssignee_PromotesAnotherAssignee()
    {
        // Keeps exactly one primary assignee so legacy AssigneeId reads stay valid.
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateNamedDb(dbName);
        var task = NewTask();
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        new TaskAssignmentService(db).AssignEmployees(task, new List<int> { 10, 20 }, assignedByUserId: 1); // 10 is primary
        await db.SaveChangesAsync();

        using var editDb = CreateNamedDb(dbName);
        var editTask = await editDb.TaskItems.Include(t => t.Assignments).FirstAsync(t => t.Id == task.Id);
        var service = new TaskAssignmentService(editDb);

        await service.SyncAssignmentsAsync(editTask, new List<int> { 20 }, changedByUserId: 1);

        var remaining = editTask.Assignments.Single();
        Assert.Equal(20, remaining.UserId);
        Assert.True(remaining.IsPrimaryAssignee);
        Assert.Equal(20, editTask.AssigneeId);
    }

    [Theory]
    [InlineData(TaskWorkflow.Pending, true)]
    [InlineData(TaskWorkflow.InProgress, true)]
    [InlineData(TaskWorkflow.ReturnedForCorrection, true)]
    [InlineData(TaskWorkflow.ForReview, false)]
    [InlineData(TaskWorkflow.Completed, false)]
    public void CanReassign_OnlyAllowedForWorkNotYetHandedBackForReview(string status, bool expected)
    {
        // Work awaiting review or already approved belongs to whoever
        // submitted it, so only Pending/InProgress/ReturnedForCorrection
        // assignments may be handed over to someone else.
        var assignment = new TaskAssignment { Status = status };
        Assert.Equal(expected, TaskAssignmentService.CanReassign(assignment));
    }

    [Fact]
    public void Reassign_MovesOwnershipAndKeepsProgressAndStatus()
    {
        var task = NewTask();
        var assignment = new TaskAssignment { UserId = 10, Progress = 40, Status = TaskWorkflow.InProgress, IsPrimaryAssignee = true };
        task.Assignments.Add(assignment);
        task.AssigneeId = 10;
        var newUser = new User { Id = 20, FullName = "New Guy" };

        var service = new TaskAssignmentService(InMemoryDbContextFactory.Create());
        service.Reassign(task, assignment, newUser, changedByUserId: 1);

        Assert.Equal(10, assignment.ReassignedFromUserId);
        Assert.Equal(20, assignment.UserId);
        Assert.Equal(40, assignment.Progress); // progress carries over
        Assert.Equal(TaskWorkflow.InProgress, assignment.Status); // status carries over
        Assert.Equal(20, task.AssigneeId); // primary assignee change updates legacy AssigneeId
    }

    [Fact]
    public void Reassign_NonPrimaryAssignee_DoesNotChangeLegacyAssigneeId()
    {
        var task = NewTask();
        task.AssigneeId = 999;
        var assignment = new TaskAssignment { UserId = 10, IsPrimaryAssignee = false };
        var newUser = new User { Id = 20, FullName = "New Guy" };

        var service = new TaskAssignmentService(InMemoryDbContextFactory.Create());
        service.Reassign(task, assignment, newUser, changedByUserId: 1);

        Assert.Equal(999, task.AssigneeId);
    }

    [Fact]
    public void RecalculateOverallStatus_AllCompleted_TaskCompletedAtFullProgress()
    {
        var task = NewTask();
        task.Assignments.Add(new TaskAssignment { UserId = 1, Status = TaskWorkflow.Completed, Progress = 100, IsPrimaryAssignee = true });
        task.Assignments.Add(new TaskAssignment { UserId = 2, Status = TaskWorkflow.Completed, Progress = 100 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateOverallStatus(task);

        Assert.Equal(TaskWorkflow.Completed, task.Status);
        Assert.Equal(100, task.Progress);
    }

    [Fact]
    public void RecalculateOverallStatus_AllForReviewOrCompleted_TaskForReview()
    {
        var task = NewTask();
        task.Assignments.Add(new TaskAssignment { UserId = 1, Status = TaskWorkflow.ForReview, Progress = 100, IsPrimaryAssignee = true });
        task.Assignments.Add(new TaskAssignment { UserId = 2, Status = TaskWorkflow.Completed, Progress = 100 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateOverallStatus(task);

        Assert.Equal(TaskWorkflow.ForReview, task.Status);
    }

    [Fact]
    public void RecalculateOverallStatus_NormalTask_ProgressStockpilesAcrossAssignees()
    {
        // Non-WholeOffice tasks are one shared piece of work: each
        // assignee's progress is their contribution, so they add up
        // (capped at 100) rather than averaging.
        var task = NewTask(TaskTypes.DirectAdmin);
        task.Assignments.Add(new TaskAssignment { UserId = 1, Status = TaskWorkflow.InProgress, Progress = 10, IsPrimaryAssignee = true });
        task.Assignments.Add(new TaskAssignment { UserId = 2, Status = TaskWorkflow.InProgress, Progress = 20 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateOverallStatus(task);

        Assert.Equal(TaskWorkflow.InProgress, task.Status);
        Assert.Equal(30, task.Progress);
    }

    [Fact]
    public void RecalculateOverallStatus_WholeOfficeTask_ProgressIsAverageNotSum()
    {
        // Whole Office tasks are the documented exception: everyone does
        // their own copy of the work, so progress averages instead of summing.
        var task = NewTask(TaskTypes.WholeOffice);
        task.Assignments.Add(new TaskAssignment { UserId = 1, Status = TaskWorkflow.InProgress, Progress = 40, IsPrimaryAssignee = true });
        task.Assignments.Add(new TaskAssignment { UserId = 2, Status = TaskWorkflow.InProgress, Progress = 60 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateOverallStatus(task);

        Assert.Equal(50, task.Progress);
    }

    [Fact]
    public void RecalculateOverallStatus_ProgressSumCapsAt100()
    {
        var task = NewTask();
        task.Assignments.Add(new TaskAssignment { UserId = 1, Status = TaskWorkflow.InProgress, Progress = 70, IsPrimaryAssignee = true });
        task.Assignments.Add(new TaskAssignment { UserId = 2, Status = TaskWorkflow.InProgress, Progress = 60 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateOverallStatus(task);

        Assert.Equal(100, task.Progress);
    }

    [Fact]
    public void RecalculateOverallStatus_NoneStarted_TaskStaysPending()
    {
        var task = NewTask();
        task.Assignments.Add(new TaskAssignment { UserId = 1, Status = TaskWorkflow.Pending, Progress = 0, IsPrimaryAssignee = true });
        task.Assignments.Add(new TaskAssignment { UserId = 2, Status = TaskWorkflow.Pending, Progress = 0 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateOverallStatus(task);

        Assert.Equal(TaskWorkflow.Pending, task.Status);
    }

    [Fact]
    public void RecalculateMainTaskFromSubtasks_NonMainTaskLevel_IsNoOp()
    {
        var mainTask = new TaskItem { TaskLevel = TaskLevels.Subtask, Status = "untouched" };
        mainTask.Subtasks.Add(new TaskItem { Status = TaskWorkflow.Completed, Progress = 100 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateMainTaskFromSubtasks(mainTask);

        Assert.Equal("untouched", mainTask.Status);
    }

    [Fact]
    public void RecalculateMainTaskFromSubtasks_NoSubtasksYet_LeavesStatusUntouched()
    {
        var mainTask = new TaskItem { TaskLevel = TaskLevels.Main, Status = "untouched" };

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateMainTaskFromSubtasks(mainTask);

        Assert.Equal("untouched", mainTask.Status);
    }

    [Fact]
    public void RecalculateMainTaskFromSubtasks_AllSubtasksCompleted_MainTaskCompleted()
    {
        var mainTask = new TaskItem { TaskLevel = TaskLevels.Main };
        mainTask.Subtasks.Add(new TaskItem { Status = TaskWorkflow.Completed, Progress = 100 });
        mainTask.Subtasks.Add(new TaskItem { Status = TaskWorkflow.Completed, Progress = 100 });

        new TaskAssignmentService(InMemoryDbContextFactory.Create()).RecalculateMainTaskFromSubtasks(mainTask);

        Assert.Equal(TaskWorkflow.Completed, mainTask.Status);
        Assert.Equal(100, mainTask.Progress);
    }

    [Fact]
    public async Task PropagateToParentMainTaskAsync_StandaloneTask_IsNoOp()
    {
        using var db = InMemoryDbContextFactory.Create();
        var service = new TaskAssignmentService(db);
        var standalone = new TaskItem { TaskLevel = TaskLevels.Subtask, ParentTaskId = null };

        // Should not throw even though there's no parent to load.
        await service.PropagateToParentMainTaskAsync(standalone);
    }

    [Fact]
    public async Task RecalculateMainTaskAsync_PropagatesSubtaskRollupToParent()
    {
        using var db = InMemoryDbContextFactory.Create();
        var mainTask = new TaskItem { TaskName = "Directive", TaskLevel = TaskLevels.Main, Description = "d" };
        db.TaskItems.Add(mainTask);
        await db.SaveChangesAsync();

        var subtask = new TaskItem
        {
            TaskName = "Subtask",
            Description = "d",
            TaskLevel = TaskLevels.Subtask,
            ParentTaskId = mainTask.Id,
            Status = TaskWorkflow.Completed,
            Progress = 100
        };
        db.TaskItems.Add(subtask);
        await db.SaveChangesAsync();

        var service = new TaskAssignmentService(db);
        await service.PropagateToParentMainTaskAsync(subtask);

        var reloaded = await db.TaskItems.FirstAsync(t => t.Id == mainTask.Id);
        Assert.Equal(TaskWorkflow.Completed, reloaded.Status);
        Assert.Equal(100, reloaded.Progress);
    }
}
