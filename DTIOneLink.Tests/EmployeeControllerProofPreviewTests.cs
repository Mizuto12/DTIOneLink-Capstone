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

// PreviewProof must be reachable by exactly the same people DownloadProof
// already is (same GetAccessibleSubmissionAsync), and must fall back to an
// ordinary forced download for a file type a browser can't render inline.
//
// Deliberately does NOT touch Directory.CurrentDirectory: that's process-wide
// state, and xUnit runs test classes in parallel within one process — an
// earlier version of this file changed it in the constructor and broke
// every WebApplicationFactory-based integration test running at the same
// time. Instead, each test writes its proof file under the real CWD's
// App_Data/ProofUploads (exactly where DownloadProof/PreviewProof already
// look in production) with a GUID-unique stored name, then deletes it.
public class EmployeeControllerProofPreviewTests
{
    private static string StorageRoot => Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ProofUploads");

    private static (AppDbContext Context, TaskSubmission Submission, User Employee, string StoredPath) Seed(
        string extension, string proofFileName, byte[] fileContent)
    {
        var context = InMemoryDbContextFactory.Create();

        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        var other = new User { Id = 999, Username = "other", PasswordHash = "x", FullName = "Someone Else", Role = "Employee", Department = "FAU" };
        context.Users.AddRange(employee, other);
        context.SaveChanges();

        var task = new TaskItem { TaskName = "Task", Description = "d", DueDate = DateTime.UtcNow.AddDays(5) };
        context.TaskItems.Add(task);
        context.SaveChanges();

        var assignment = new TaskAssignment { TaskId = task.Id, UserId = employee.Id, Status = TaskWorkflow.ForReview, Progress = 100 };
        context.TaskAssignments.Add(assignment);
        context.SaveChanges();

        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var submission = new TaskSubmission
        {
            TaskId = task.Id,
            TaskAssignmentId = assignment.Id,
            ProofFileName = proofFileName,
            ProofStoredFileName = storedFileName,
        };
        context.TaskSubmissions.Add(submission);
        context.SaveChanges();

        Directory.CreateDirectory(StorageRoot);
        var storedPath = Path.Combine(StorageRoot, storedFileName);
        File.WriteAllBytes(storedPath, fileContent);

        return (context, submission, employee, storedPath);
    }

    private static EmployeeController BuildController(AppDbContext context, int userId, string role = "Employee", string? department = null)
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
        return new EmployeeController(
            context,
            new TaskAssignmentService(context),
            new NotificationService(context, config),
            NullLogger<EmployeeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    [Fact]
    public async Task PreviewProof_Owner_RendersInlineWithTheRightContentType()
    {
        var (context, submission, employee, storedPath) = Seed(".png", "proof.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            var controller = BuildController(context, employee.Id);

            var result = await controller.PreviewProof(submission.Id);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("image/png", file.ContentType);
            Assert.True(string.IsNullOrEmpty(file.FileDownloadName)); // no forced Content-Disposition: attachment
        }
        finally { File.Delete(storedPath); }
    }

    [Fact]
    public async Task DownloadProof_Owner_ForcesAttachmentWithOriginalFileName()
    {
        var (context, submission, employee, storedPath) = Seed(".png", "proof.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            var controller = BuildController(context, employee.Id);

            var result = await controller.DownloadProof(submission.Id);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/octet-stream", file.ContentType);
            Assert.Equal("proof.png", file.FileDownloadName);
        }
        finally { File.Delete(storedPath); }
    }

    [Fact]
    public async Task PreviewProof_UnsupportedFileType_FallsBackToTheSameForcedDownloadAsDownloadProof()
    {
        // .docx is allowed proof, but a browser can't render it inline.
        var (context, submission, employee, storedPath) = Seed(".docx", "proof.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        try
        {
            var controller = BuildController(context, employee.Id);

            var result = await controller.PreviewProof(submission.Id);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/octet-stream", file.ContentType);
            Assert.Equal("proof.docx", file.FileDownloadName);
        }
        finally { File.Delete(storedPath); }
    }

    [Fact]
    public async Task PreviewProof_NotAuthorized_IsNotFound_SameAsDownloadProof()
    {
        // Not the assignee, not elevated, different department — neither
        // action should expose the file.
        var (context, submission, _, storedPath) = Seed(".png", "proof.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            var controller = BuildController(context, userId: 999);

            var previewResult = await controller.PreviewProof(submission.Id);
            var downloadResult = await controller.DownloadProof(submission.Id);

            Assert.IsType<NotFoundResult>(previewResult);
            Assert.IsType<NotFoundResult>(downloadResult);
        }
        finally { File.Delete(storedPath); }
    }

    [Fact]
    public async Task PreviewProof_DepartmentAdmin_HasTheSameAccessAsDownloadProof()
    {
        var (context, submission, _, storedPath) = Seed(".pdf", "proof.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 });
        try
        {
            var admin = new User { Id = 500, Username = "admin", PasswordHash = "x", FullName = "Dept Admin", Role = "Admin", Department = "BDD" };
            context.Users.Add(admin);
            await context.SaveChangesAsync();
            var controller = BuildController(context, admin.Id, role: "Admin", department: "BDD");

            var previewResult = await controller.PreviewProof(submission.Id);
            var downloadResult = await controller.DownloadProof(submission.Id);

            Assert.IsType<FileContentResult>(previewResult);
            Assert.IsType<FileContentResult>(downloadResult);
        }
        finally { File.Delete(storedPath); }
    }

    [Fact]
    public async Task PreviewProof_MissingFileOnDisk_IsNotFound_SameAsDownloadProof()
    {
        var (context, submission, employee, storedPath) = Seed(".png", "proof.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.Delete(storedPath); // simulate the file having gone missing before either action runs
        var controller = BuildController(context, employee.Id);

        var previewResult = await controller.PreviewProof(submission.Id);
        var downloadResult = await controller.DownloadProof(submission.Id);

        Assert.IsType<NotFoundResult>(previewResult);
        Assert.IsType<NotFoundResult>(downloadResult);
    }
}
