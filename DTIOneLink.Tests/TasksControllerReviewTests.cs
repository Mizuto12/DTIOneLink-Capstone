using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
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
        var notifications = new NotificationService(context, config, NullLogger<NotificationService>.Instance);
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

    // Reproduces the race the RowVersion concurrency tokens were added for
    // (2026-10-28 audit, finding F10): two Review decisions on the same
    // submission, each loaded before the other saved. The
    // submission.Decision != null guard only catches a sequential
    // double-click — it can't catch this, since both reads happen before
    // either write. Two separate DbContext instances sharing one
    // in-memory database stand in for two concurrent requests.
    [Fact]
    public async Task Review_TwoDecisionsRacingOnTheSameSubmission_SecondOneIsRefused()
    {
        var dbName = Guid.NewGuid().ToString();
        DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        int submissionId;
        using (var seedContext = new AppDbContext(Options()))
        {
            var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
            seedContext.Users.Add(employee);
            seedContext.SaveChanges();

            var task = new TaskItem
            {
                TaskName = "Direct Admin Task",
                Description = "d",
                TaskLevel = TaskLevels.Main,
                TaskType = TaskTypes.DirectAdmin,
                OwningDepartment = "BDD",
                DueDate = DateTime.UtcNow.AddDays(5),
            };
            seedContext.TaskItems.Add(task);
            seedContext.SaveChanges();

            var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = TaskWorkflow.ForReview, Progress = 100 };
            seedContext.TaskAssignments.Add(assignment);
            seedContext.SaveChanges();

            var submission = new TaskSubmission
            {
                TaskId = task.Id,
                TaskAssignmentId = assignment.Id,
                ProofFileName = "proof.pdf",
                ProofStoredFileName = "stored.pdf",
                Remarks = "Done",
            };
            seedContext.TaskSubmissions.Add(submission);
            seedContext.SaveChanges();
            submissionId = submission.Id;
        }

        // Two independent contexts, each loading its own copy of the same
        // rows — exactly what two simultaneous requests would each do.
        using var contextA = new AppDbContext(Options());
        using var contextB = new AppDbContext(Options());
        var controllerA = BuildController(contextA, "SuperAdmin");
        var controllerB = BuildController(contextB, "SuperAdmin");

        var resultA = await controllerA.Review(new TaskSubmissionDecisionViewModel { SubmissionId = submissionId, Decision = "approve" });
        Assert.IsType<RedirectToActionResult>(resultA); // the first one through wins cleanly

        var resultB = await controllerB.Review(new TaskSubmissionDecisionViewModel { SubmissionId = submissionId, Decision = "approve" });

        // Refused, not a second silent "success" — and refused via the
        // normal redirect-with-TempData-error path, not an unhandled 500.
        Assert.IsType<RedirectToActionResult>(resultB);
        Assert.True(controllerB.TempData.ContainsKey("ErrorMessage"));

        // The row actually reflects only the first decision.
        using var verifyContext = new AppDbContext(Options());
        var finalSubmission = await verifyContext.TaskSubmissions.FindAsync(submissionId);
        Assert.Equal("approved", finalSubmission!.Decision);
    }
}
