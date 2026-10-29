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

// Regression coverage for the gap found auditing TasksController.ReassignResponsibleAdmin:
// it deliberately allows handing a Department Directive to an Admin outside
// the directive's own department (e.g. covering for one on leave), but
// IsWithinTaskScope/CanReviewSubmission/BuildIndexModelAsync's scope query
// and NotificationService.GetDepartmentAdminIdsAsync all used to check only
// department membership, silently locking the new Responsible Admin out of
// reviewing, seeing, or being notified about the subtasks they were just
// handed.
public class CrossDepartmentResponsibleAdminTests
{
    private static (AppDbContext Context, TaskItem MainTask, TaskItem Subtask, TaskSubmission Submission, User ReassignedAdmin)
        SeedReassignedDirective()
    {
        var context = InMemoryDbContextFactory.Create();

        // The directive originally belongs to CPD, but OPD has reassigned it
        // to an Admin whose own Department is FAU (e.g. covering for CPD's
        // own Division Chief being on leave).
        var reassignedAdmin = new User { Username = "faudmin", PasswordHash = "x", FullName = "FAU Admin", Role = "Admin", Department = "FAU" };
        var employee = new User { Username = "cpdemp", PasswordHash = "x", FullName = "CPD Employee", Role = "Employee", Department = "CPD" };
        context.Users.AddRange(reassignedAdmin, employee);
        context.SaveChanges();

        var mainTask = new TaskItem
        {
            TaskName = "CPD Directive",
            Description = "d",
            TaskLevel = TaskLevels.Main,
            TaskType = TaskTypes.DepartmentDirective,
            OwningDepartment = "CPD",
            ResponsibleAdminUserId = reassignedAdmin.Id, // handed across departments
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(mainTask);
        context.SaveChanges();

        var subtask = new TaskItem
        {
            TaskName = "CPD Subtask",
            Description = "d",
            TaskLevel = TaskLevels.Subtask,
            ParentTaskId = mainTask.Id,
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(subtask);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = subtask.Id, UserId = employee.Id, Status = TaskWorkflow.ForReview, Progress = 100 };
        context.TaskAssignments.Add(assignment);
        context.SaveChanges();

        var submission = new TaskSubmission
        {
            TaskId = subtask.Id,
            TaskAssignmentId = assignment.Id,
            ProofFileName = "proof.pdf",
            ProofStoredFileName = "stored.pdf",
            Remarks = "Done",
        };
        context.TaskSubmissions.Add(submission);
        context.SaveChanges();

        return (context, mainTask, subtask, submission, reassignedAdmin);
    }

    private static TasksController BuildController(AppDbContext context, string role, int userId, string? department)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", role);
        httpContext.Session.SetInt32("UserId", userId);
        if (department != null)
        {
            httpContext.Session.SetString("UserDepartment", department);
        }

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
    public async Task Review_ReassignedCrossDepartmentAdmin_CanApproveSubtaskSubmission()
    {
        var (context, _, _, submission, reassignedAdmin) = SeedReassignedDirective();
        // Reassigned Admin's own Department (FAU) differs from the
        // directive's OwningDepartment (CPD) — this is exactly the scenario
        // the department-only check used to reject.
        var controller = BuildController(context, "Admin", reassignedAdmin.Id, "FAU");

        var result = await controller.Review(new TaskSubmissionDecisionViewModel
        {
            SubmissionId = submission.Id,
            Decision = "approve",
        });

        Assert.IsType<RedirectToActionResult>(result);
        var reloaded = await context.TaskAssignments.FindAsync(submission.TaskAssignmentId);
        Assert.Equal(TaskWorkflow.Completed, reloaded!.Status);
    }

    [Fact]
    public async Task Review_AdminFromNeitherOwnNorDirectiveDepartment_IsStillRejected()
    {
        // A third department's Admin (not the Responsible Admin, and not in
        // the directive's own department) must still be refused — the fix
        // only extends access to the actual Responsible Admin, not to every
        // other department.
        var (context, _, _, submission, _) = SeedReassignedDirective();
        var otherAdmin = new User { Username = "bddadmin", PasswordHash = "x", FullName = "BDD Admin", Role = "Admin", Department = "BDD" };
        context.Users.Add(otherAdmin);
        context.SaveChanges();

        var controller = BuildController(context, "Admin", otherAdmin.Id, "BDD");

        var result = await controller.Review(new TaskSubmissionDecisionViewModel
        {
            SubmissionId = submission.Id,
            Decision = "approve",
        });

        Assert.IsType<RedirectToActionResult>(result);
        var reloaded = await context.TaskAssignments.FindAsync(submission.TaskAssignmentId);
        Assert.Equal(TaskWorkflow.ForReview, reloaded!.Status); // unchanged — rejected, not approved
    }

    [Fact]
    public async Task Index_ReassignedCrossDepartmentAdmin_SeesTheDirectiveAndItsSubtask()
    {
        var (context, mainTask, subtask, _, reassignedAdmin) = SeedReassignedDirective();
        var controller = BuildController(context, "Admin", reassignedAdmin.Id, "FAU");

        var result = await controller.Index(null, null, null, null, null, null, null, 1);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TaskIndexViewModel>(viewResult.Model);
        var ids = model.Tasks.Select(t => t.Id).ToList();
        Assert.Contains(mainTask.Id, ids);
        Assert.Contains(subtask.Id, ids);
    }
}
