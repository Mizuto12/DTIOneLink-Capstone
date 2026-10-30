using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;

namespace DTIOneLink.Tests;

// Unit coverage for the shared progress-write path extracted during
// panelist item T10 (2026-10-31): TaskAssignmentService.ApplyProgressChangeAsync,
// used by both EmployeeController (the assignee's own slider) and
// TasksController (an authorized Admin/SuperAdmin's correction). These lock
// rules used to live only inside EmployeeController's private method —
// this file is the first direct test of the rules themselves, independent
// of which controller calls them.
public class TaskAssignmentServiceApplyProgressChangeTests
{
    private static (AppDbContext Context, TaskItem Task, TaskAssignment Assignment) Seed(string status, int progress = 0)
    {
        var context = InMemoryDbContextFactory.Create();
        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        context.SaveChanges();

        var task = new TaskItem { TaskName = "Task", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(task);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = status, Progress = progress };
        context.TaskAssignments.Add(assignment);
        context.SaveChanges();

        return (context, task, assignment);
    }

    [Fact]
    public async Task Completed_IsLocked()
    {
        var (context, task, assignment) = Seed(TaskWorkflow.Completed, 100);
        var service = new TaskAssignmentService(context);

        var error = await service.ApplyProgressChangeAsync(task, assignment, 50, null);

        Assert.NotNull(error);
        Assert.Equal(100, assignment.Progress);
    }

    [Fact]
    public async Task ForReview_IsLocked()
    {
        var (context, task, assignment) = Seed(TaskWorkflow.ForReview, 100);
        var service = new TaskAssignmentService(context);

        var error = await service.ApplyProgressChangeAsync(task, assignment, 50, null);

        Assert.NotNull(error);
        Assert.Equal(100, assignment.Progress);
    }

    [Fact]
    public async Task ReturnedForCorrection_IsEditable()
    {
        var (context, task, assignment) = Seed(TaskWorkflow.ReturnedForCorrection, 40);
        var service = new TaskAssignmentService(context);

        var error = await service.ApplyProgressChangeAsync(task, assignment, 65, null);

        Assert.Null(error);
        Assert.Equal(65, assignment.Progress);
        Assert.Equal(TaskWorkflow.ReturnedForCorrection, assignment.Status); // status itself doesn't silently change
    }

    [Fact]
    public async Task ChecklistGovernedAssignment_IsLocked()
    {
        var (context, task, assignment) = Seed(TaskWorkflow.InProgress, 0);
        context.TaskChecklistItems.Add(new TaskChecklistItem { TaskAssignmentId = assignment.Id, Label = "Step 1" });
        context.SaveChanges();
        // Reload with ChecklistItems populated, same as every real caller's query.
        context.Entry(assignment).Collection(a => a.ChecklistItems).Load();
        var service = new TaskAssignmentService(context);

        var error = await service.ApplyProgressChangeAsync(task, assignment, 50, null);

        Assert.NotNull(error);
        Assert.Equal(0, assignment.Progress);
    }

    [Fact]
    public async Task PendingTaskWithProgressAboveZero_AutomaticallyMovesToInProgress()
    {
        var (context, task, assignment) = Seed(TaskWorkflow.Pending, 0);
        var service = new TaskAssignmentService(context);

        var error = await service.ApplyProgressChangeAsync(task, assignment, 10, null);

        Assert.Null(error);
        Assert.Equal(TaskWorkflow.InProgress, assignment.Status);
    }

    [Fact]
    public async Task NeverReachesCompleted_EvenAtProgress100()
    {
        var (context, task, assignment) = Seed(TaskWorkflow.InProgress, 80);
        var service = new TaskAssignmentService(context);

        var error = await service.ApplyProgressChangeAsync(task, assignment, 100, null);

        Assert.Null(error);
        Assert.Equal(100, assignment.Progress);
        Assert.Equal(TaskWorkflow.InProgress, assignment.Status); // only TasksController.Review's Approve reaches Completed
    }
}
