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

// System-level coverage for EmployeeController.SubmitProof's real upload
// path: a file renamed to claim an extension its real contents don't match
// must be rejected by the controller itself (the real multipart/form-data
// path, via a real IFormFile), not just by the pure ProofFileValidator
// function (already covered at the unit level by UT-FILE-01) — this is the
// real controller path TC-FILE-010 in docs/testing/four-levels-of-testing.md
// names as still needing automation. No file is ever written to disk here:
// SubmitProof only touches the filesystem after validation passes.
public class EmployeeControllerSubmitProofTests
{
    private static readonly byte[] JpgHeader = { 0xFF, 0xD8, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00 };

    private static IFormFile MakeFile(byte[] content, string fileName) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName);

    private static (AppDbContext Context, TaskItem Task, User Employee) Seed()
    {
        var context = InMemoryDbContextFactory.Create();

        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        context.SaveChanges();

        var task = new TaskItem { TaskName = "Task", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(task);
        context.SaveChanges();

        // Status pending/in-progress (eligible to submit) and Progress at
        // the 80% threshold, so the only reason this gets refused is the
        // file content/extension mismatch — not an earlier gate.
        context.TaskAssignments.Add(new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = TaskWorkflow.InProgress, Progress = 80 });
        context.SaveChanges();

        return (context, task, employee);
    }

    private static EmployeeController BuildController(AppDbContext context, int userId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", "Employee");
        httpContext.Session.SetInt32("UserId", userId);

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

    [Fact]
    public async Task SubmitProof_FileContentsDontMatchExtension_IsRejectedAndNoSubmissionIsCreated()
    {
        var (context, task, employee) = Seed();
        var controller = BuildController(context, employee.Id);

        var result = await controller.SubmitProof(new ProofSubmissionViewModel
        {
            Id = task.Id,
            ProofFile = MakeFile(JpgHeader, "proof.pdf"), // real JPEG bytes, claims to be a PDF
            Remarks = "Finished the report",
        });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(context.TaskSubmissions);
        var assignment = context.TaskAssignments.Single();
        Assert.Equal(TaskWorkflow.InProgress, assignment.Status); // unchanged — never reached ForReview
    }

    [Fact]
    public async Task SubmitProof_GenuinePdf_Succeeds()
    {
        var (context, task, employee) = Seed();
        var controller = BuildController(context, employee.Id);
        byte[] pdfHeader = { 0x25, 0x50, 0x44, 0x46, 0x00, 0x00, 0x00, 0x00 };

        var result = await controller.SubmitProof(new ProofSubmissionViewModel
        {
            Id = task.Id,
            ProofFile = MakeFile(pdfHeader, "proof.pdf"),
            Remarks = "Finished the report",
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(context.TaskSubmissions);
        var assignment = context.TaskAssignments.Single();
        Assert.Equal(TaskWorkflow.ForReview, assignment.Status);
    }
}
