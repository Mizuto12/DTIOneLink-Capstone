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

// Panelist item T10 (2026-10-31): Admin/SuperAdmin can now correct an
// employee's progress from the general Task Details page, not just at the
// moment of a Review decision. TasksController.UpdateAssignmentProgress is
// a thin controller wrapper around the shared TaskAssignmentService.
// ApplyProgressChangeAsync (same lock rules as the employee's own slider),
// so these tests focus on what's specific to this action: authorization,
// department scope, and audit logging.
public class TasksControllerUpdateAssignmentProgressTests
{
    private static (AppDbContext Context, TaskItem Task, TaskAssignment Assignment, User Employee) Seed(
        string department = "BDD", string status = TaskWorkflow.ReturnedForCorrection, int progress = 40)
    {
        var context = InMemoryDbContextFactory.Create();

        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Elijah Lucena", Role = "Employee", Department = department };
        context.Users.Add(employee);
        context.SaveChanges();

        var task = new TaskItem
        {
            TaskName = "Test 2",
            Description = "d",
            TaskType = TaskTypes.DirectAdmin,
            OwningDepartment = department,
            DueDate = DateTime.UtcNow.AddDays(5),
        };
        context.TaskItems.Add(task);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = status, Progress = progress, IsPrimaryAssignee = true };
        context.TaskAssignments.Add(assignment);
        context.SaveChanges();
        task.AssigneeId = employee.Id;
        context.SaveChanges();

        return (context, task, assignment, employee);
    }

    private static TasksController BuildController(AppDbContext context, string role, string? department = null, int adminUserId = 99)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", role);
        httpContext.Session.SetInt32("UserId", adminUserId);
        httpContext.Session.SetString("FullName", "Lhexus Paul M. Sosong");
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
    public async Task Admin_CanAdjustAReturnedTasksProgress()
    {
        var (context, _, assignment, _) = Seed(status: TaskWorkflow.ReturnedForCorrection, progress: 40);
        var controller = BuildController(context, "Admin", department: "BDD");

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 65);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(65, context.TaskAssignments.Single().Progress);
    }

    [Fact]
    public async Task SuperAdmin_CanAdjustAReturnedTasksProgress_AcrossAnyDepartment()
    {
        var (context, _, assignment, _) = Seed(department: "OtherDept", status: TaskWorkflow.ReturnedForCorrection, progress: 40);
        // SuperAdmin never sets UserDepartment — IsOfficeWideTaskManager bypasses scope entirely.
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 70);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(70, context.TaskAssignments.Single().Progress);
    }

    [Fact]
    public async Task Employee_CannotUseTheAdministrativeAction()
    {
        var (context, _, assignment, employee) = Seed();
        var controller = BuildController(context, "Employee", adminUserId: employee.Id);

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 65);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(40, context.TaskAssignments.Single().Progress); // unchanged
    }

    [Fact]
    public async Task Admin_CannotModifyAnAssignmentOutsideTheirOwnDepartment()
    {
        var (context, _, assignment, _) = Seed(department: "OtherDept");
        var controller = BuildController(context, "Admin", department: "BDD"); // different department

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 65);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(40, context.TaskAssignments.Single().Progress); // unchanged
    }

    [Fact]
    public async Task TamperedAssignmentId_IsNotFound()
    {
        var (context, _, _, _) = Seed();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.UpdateAssignmentProgress(999999, 65);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task OutOfRangeProgress_IsRejectedAndUnchanged(int outOfRange)
    {
        var (context, _, assignment, _) = Seed();
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.UpdateAssignmentProgress(assignment.Id, outOfRange);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(40, context.TaskAssignments.Single().Progress); // unchanged
    }

    [Fact]
    public async Task ProgressChange_IsRecordedInTheAuditLog()
    {
        var (context, task, assignment, _) = Seed(status: TaskWorkflow.ReturnedForCorrection, progress: 40);
        var controller = BuildController(context, "Admin", department: "BDD", adminUserId: 99);

        await controller.UpdateAssignmentProgress(assignment.Id, 65);

        var activity = context.TaskActivities.Single(a => a.TaskId == task.Id);
        Assert.Equal(TaskActivityType.ProgressUpdated, activity.ActivityType);
        Assert.Equal(99, activity.PerformedByUserId);
        Assert.Contains("40%", activity.Details);
        Assert.Contains("65%", activity.Details);
    }

    [Fact]
    public async Task SettingProgressTo100_DoesNotAutomaticallyCompleteTheTask()
    {
        var (context, _, assignment, _) = Seed(status: TaskWorkflow.ReturnedForCorrection, progress: 80);
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 100);

        Assert.IsType<JsonResult>(result);
        var reloaded = context.TaskAssignments.Single();
        Assert.Equal(100, reloaded.Progress);
        Assert.NotEqual(TaskWorkflow.Completed, reloaded.Status); // only TasksController.Review's Approve reaches Completed
    }

    [Theory]
    [InlineData(TaskWorkflow.Pending)]
    [InlineData(TaskWorkflow.InProgress)]
    public async Task CannotAdjustProgress_UnlessTheSubmissionWasActuallyReturnedForCorrection(string status)
    {
        // Narrower than the assignee's own slider on purpose: an ordinary
        // Pending/In Progress task should never have this control and the
        // assignee's own slider both live on the same row at once.
        var (context, _, assignment, _) = Seed(status: status, progress: 40);
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 65);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(40, context.TaskAssignments.Single().Progress); // unchanged
    }

    [Fact]
    public async Task CannotAdjustProgressWhileAwaitingReview_DoesNotBypassTheReviewWorkflow()
    {
        var (context, _, assignment, _) = Seed(status: TaskWorkflow.ForReview, progress: 100);
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 50);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(100, context.TaskAssignments.Single().Progress); // unchanged — correct it via the Review decision instead
    }

    [Fact]
    public async Task CannotAdjustProgressOnACompletedTask()
    {
        var (context, _, assignment, _) = Seed(status: TaskWorkflow.Completed, progress: 100);
        var controller = BuildController(context, "SuperAdmin");

        var result = await controller.UpdateAssignmentProgress(assignment.Id, 50);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(100, context.TaskAssignments.Single().Progress); // unchanged
    }
}
