using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Regression/System-level coverage for TasksController.Review: a decision
// outside "approve"/"return" (e.g. an attempt to skip straight back to
// "in-progress") must be refused by the controller itself, not just by the
// pure TaskWorkflow.CanTransition rule (already covered at the unit level by
// UT-WF-01/02) — this is the real controller path TC-TASK-006 in
// docs/testing/four-levels-of-testing.md names as still needing automation.
public class TasksControllerReviewTests
{
    private static (AppDbContext Context, TaskItem Task, TaskSubmission Submission) SeedForReview()
    {
        var context = InMemoryDbContextFactory.Create();

        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        context.SaveChanges();

        var task = new TaskItem
        {
            TaskName = "Direct Admin Task",
            Description = "d",
            TaskLevel = TaskLevels.Main,
            TaskType = TaskTypes.DirectAdmin,
            OwningDepartment = "BDD",
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(task);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = TaskWorkflow.ForReview, Progress = 100 };
        context.TaskAssignments.Add(assignment);
        context.SaveChanges();

        var submission = new TaskSubmission
        {
            TaskId = task.Id,
            TaskAssignmentId = assignment.Id,
            ProofFileName = "proof.pdf",
            ProofStoredFileName = "stored.pdf",
            Remarks = "Done",
        };
        context.TaskSubmissions.Add(submission);
        context.SaveChanges();

        return (context, task, submission);
    }

    private static TasksController BuildController(AppDbContext context, string role)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", role);
        httpContext.Session.SetInt32("UserId", 99);

        var config = new ConfigurationBuilder().Build();
        var notifications = new NotificationService(context, config);
        var taskAssignments = new TaskAssignmentService(context);
        var opdTasks = new OpdTaskService(context, taskAssignments, notifications);

        return new TasksController(context, notifications, taskAssignments, opdTasks, NullLogger<TasksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
    }

    [Fact]
    public async Task Review_DecisionOtherThanApproveOrReturn_IsRejectedAndAssignmentUnchanged()
    {
        var (context, _, submission) = SeedForReview();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.Review(new TaskSubmissionDecisionViewModel
        {
            SubmissionId = submission.Id,
            Decision = "in-progress", // not a legal decision — attempting to skip the review step entirely
        });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);

        var reloaded = await context.TaskAssignments.FindAsync(submission.TaskAssignmentId);
        Assert.Equal(TaskWorkflow.ForReview, reloaded!.Status);
        var reloadedSubmission = await context.TaskSubmissions.FindAsync(submission.Id);
        Assert.Null(reloadedSubmission!.Decision);
    }

    [Fact]
    public async Task Review_Approve_Succeeds()
    {
        var (context, _, submission) = SeedForReview();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.Review(new TaskSubmissionDecisionViewModel
        {
            SubmissionId = submission.Id,
            Decision = "approve",
        });

        Assert.IsType<RedirectToActionResult>(result);
        var reloaded = await context.TaskAssignments.FindAsync(submission.TaskAssignmentId);
        Assert.Equal(TaskWorkflow.Completed, reloaded!.Status);
    }
}
