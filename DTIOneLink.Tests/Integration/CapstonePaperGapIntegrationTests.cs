using System.Net;
using System.Text;
using DTIOneLink.Models;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Tests.Integration;

// Real-HTTP coverage for five Level 2 scenarios the capstone manuscript's
// expanded integration-test tables (Accounts and Access / Tasks, Employee
// Work and Review / Directives and Subtasks) named but never actually ran:
// IT-37 (anonymous visitor vs. Records), IT-06 (task assignment ->
// notification + queued email), IT-17 (cross-department review authority),
// IT-12 (a directive's last subtask approval rolls the directive up to
// Completed and notifies OPD), and IT-23 (Admin attempting AddSubtask,
// corrected to the real, current behavior — Admin is refused; only the OPD
// adds subtasks, per DEF-005).
public class CapstonePaperGapIntegrationTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();

    public CapstonePaperGapIntegrationTests()
    {
        // Forces the test host (and its EnsureCreated() schema) to exist
        // before any test seeds data through CreateDbContext() — without
        // this, seeding races the lazily-created host and fails with
        // "no such table".
        using var warmup = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task LogInAsync(HttpClient client, string username)
    {
        var getResponse = await client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(getResponse);
        var loginResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = username,
            ["Password"] = "Correct-Horse-1",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode); // sanity: login actually succeeded
    }

    // ── IT-37: Account -> Records — anonymous visitor ──────────────────
    [Fact]
    public async Task AnonymousVisitor_RequestingRecordsPages_IsRefusedAndRedirectedToLogin()
    {
        using var client = NewClient();

        var indexResponse = await client.GetAsync("/Records");
        IntegrationTestHelpers.AssertRedirectsToLogin(indexResponse);

        var listResponse = await client.GetAsync("/Records/GetAll");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        // Save requires a real antiforgery token (harvested anonymously —
        // the token itself doesn't require a session) so the 401 proves it
        // comes from RecordsController's own CheckAccess, not the
        // antiforgery filter rejecting a missing/invalid token first.
        var tokenPage = await client.GetAsync("/Account/Login");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(tokenPage);
        var saveRequest = new HttpRequestMessage(HttpMethod.Post, "/Records/Save")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        saveRequest.Headers.Add("X-CSRF-TOKEN", token);
        var saveResponse = await client.SendAsync(saveRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, saveResponse.StatusCode);
    }

    // ── IT-06: Tasks -> Notifications -> E-mail outbox ──────────────────
    [Fact]
    public async Task DirectAdminTaskAssignment_NotifiesTheAdminAndQueuesOneEmail()
    {
        int adminId;
        using (var db = _factory.CreateDbContext())
        {
            var superAdmin = IntegrationTestHelpers.NewActiveUser("SuperAdmin", "Office of the Provincial Director", username: "super1@dti.gov.ph");
            var admin = IntegrationTestHelpers.NewActiveUser("Admin", TargetDepartments.BDD, username: "admin1@dti.gov.ph");
            db.Users.AddRange(superAdmin, admin);
            await db.SaveChangesAsync();
            adminId = admin.Id;
        }

        using var client = NewClient();
        await LogInAsync(client, "super1@dti.gov.ph");

        var createPage = await client.GetAsync("/Tasks/Create");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(createPage);

        var response = await client.PostAsync("/Tasks/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TaskName"] = "Quarterly Report",
            ["TaskType"] = TaskTypes.DirectAdmin,
            ["OwningDepartment"] = TargetDepartments.BDD,
            ["ResponsibleAdminUserId"] = adminId.ToString(),
            ["DueDate"] = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"),
            ["Priority"] = "medium",
            ["Description"] = "Prepare the quarterly report.",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyDb = _factory.CreateDbContext();
        var notification = await verifyDb.Notifications.FirstOrDefaultAsync(n => n.RecipientUserId == adminId);
        Assert.NotNull(notification);
        var queuedEmails = await verifyDb.EmailOutbox.Where(e => e.ToEmail == "admin1@dti.gov.ph").ToListAsync();
        Assert.Single(queuedEmails);
    }

    // ── IT-17: Review authority — a different department Admin ─────────
    [Fact]
    public async Task DifferentDepartmentAdmin_ApprovingAnOpdAssignedSubmission_IsRefused()
    {
        int submissionId;
        using (var db = _factory.CreateDbContext())
        {
            var responsibleAdmin = IntegrationTestHelpers.NewActiveUser("Admin", TargetDepartments.BDD, username: "bdd.admin@dti.gov.ph");
            var otherAdmin = IntegrationTestHelpers.NewActiveUser("Admin", TargetDepartments.CPD, username: "cpd.admin@dti.gov.ph");
            db.Users.AddRange(responsibleAdmin, otherAdmin);
            await db.SaveChangesAsync();

            var task = new TaskItem
            {
                TaskName = "Direct Admin Task",
                Description = "d",
                TaskLevel = TaskLevels.Main,
                TaskType = TaskTypes.DirectAdmin,
                OwningDepartment = TargetDepartments.BDD,
                ResponsibleAdminUserId = responsibleAdmin.Id,
                DueDate = DateTime.UtcNow.AddDays(5),
            };
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();

            var assignment = new TaskAssignment { TaskId = task.Id, UserId = responsibleAdmin.Id, Status = TaskWorkflow.ForReview, Progress = 100 };
            db.TaskAssignments.Add(assignment);
            await db.SaveChangesAsync();

            var submission = new TaskSubmission
            {
                TaskId = task.Id,
                TaskAssignmentId = assignment.Id,
                ProofFileName = "proof.pdf",
                ProofStoredFileName = "stored.pdf",
                Remarks = "Done",
            };
            db.TaskSubmissions.Add(submission);
            await db.SaveChangesAsync();
            submissionId = submission.Id;
        }

        using var client = NewClient();
        await LogInAsync(client, "cpd.admin@dti.gov.ph"); // a different department than the task's own (BDD)

        var reviewPage = await client.GetAsync($"/Tasks/Review/{submissionId}");
        // CanReviewSubmission refuses a different department's Admin — the
        // GET redirects to Index with a TempData error, not to login.
        Assert.Equal(HttpStatusCode.Redirect, reviewPage.StatusCode);

        var dashboardPage = await client.GetAsync("/Dashboard/AdminDashboard");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(dashboardPage);

        var postResponse = await client.PostAsync("/Tasks/Review", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SubmissionId"] = submissionId.ToString(),
            ["Decision"] = "approve",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, postResponse.StatusCode); // refused -> redirected to Index, not a success path

        using var verifyDb = _factory.CreateDbContext();
        var submission2 = await verifyDb.TaskSubmissions.FirstAsync(s => s.Id == submissionId);
        var assignment2 = await verifyDb.TaskAssignments.FirstAsync(a => a.Id == submission2.TaskAssignmentId);
        Assert.Null(submission2.Decision); // untouched — the refusal never reached the decision logic
        Assert.Equal(TaskWorkflow.ForReview, assignment2.Status, ignoreCase: true);
    }

    // ── IT-12: last-subtask approval rolls a directive up to Completed,
    //    and notifies the OPD ──────────────────────────────────────────
    [Fact]
    public async Task ApprovingADirectivesLastSubtask_CompletesTheDirectiveAndNotifiesOpd()
    {
        int directiveId, submissionId, opdUserId;
        using (var db = _factory.CreateDbContext())
        {
            var opd = IntegrationTestHelpers.NewActiveUser("SuperAdmin", "Office of the Provincial Director", username: "opd1@dti.gov.ph");
            var admin = IntegrationTestHelpers.NewActiveUser("Admin", TargetDepartments.FAU, username: "fau.admin@dti.gov.ph");
            var employee = IntegrationTestHelpers.NewActiveUser("Employee", TargetDepartments.FAU, username: "fau.employee@dti.gov.ph");
            db.Users.AddRange(opd, admin, employee);
            await db.SaveChangesAsync();
            opdUserId = opd.Id;

            var directive = new TaskItem
            {
                TaskName = "FAU Directive",
                Description = "d",
                TaskLevel = TaskLevels.Main,
                TaskType = TaskTypes.DepartmentDirective,
                OwningDepartment = TargetDepartments.FAU,
                CreatedByUserId = opd.Id,
                DueDate = DateTime.UtcNow.AddDays(5),
            };
            db.TaskItems.Add(directive);
            await db.SaveChangesAsync();
            directiveId = directive.Id;

            var subtask = new TaskItem
            {
                TaskName = "Only Subtask",
                Description = "d",
                TaskLevel = TaskLevels.Subtask,
                ParentTaskId = directive.Id,
                OwningDepartment = TargetDepartments.FAU,
                DueDate = DateTime.UtcNow.AddDays(5),
            };
            db.TaskItems.Add(subtask);
            await db.SaveChangesAsync();

            var assignment = new TaskAssignment { TaskId = subtask.Id, UserId = employee.Id, Status = TaskWorkflow.ForReview, Progress = 100 };
            db.TaskAssignments.Add(assignment);
            await db.SaveChangesAsync();

            var submission = new TaskSubmission
            {
                TaskId = subtask.Id,
                TaskAssignmentId = assignment.Id,
                ProofFileName = "proof.pdf",
                ProofStoredFileName = "stored.pdf",
                Remarks = "Finished the only subtask",
            };
            db.TaskSubmissions.Add(submission);
            await db.SaveChangesAsync();
            submissionId = submission.Id;
        }

        using var client = NewClient();
        await LogInAsync(client, "fau.admin@dti.gov.ph");

        var dashboardPage = await client.GetAsync("/Dashboard/AdminDashboard");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(dashboardPage);

        var response = await client.PostAsync("/Tasks/Review", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SubmissionId"] = submissionId.ToString(),
            ["Decision"] = "approve",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyDb = _factory.CreateDbContext();
        var reloadedDirective = await verifyDb.TaskItems.FirstAsync(t => t.Id == directiveId);
        Assert.Equal(TaskWorkflow.Completed, reloadedDirective.Status, ignoreCase: true);

        var opdNotification = await verifyDb.Notifications.FirstOrDefaultAsync(n => n.RecipientUserId == opdUserId && n.RelatedTaskId == directiveId);
        Assert.NotNull(opdNotification);
    }

    // ── IT-23 (corrected): Admin attempting AddSubtask is refused ──────
    // The manuscript's original scenario description ("Admin adds a
    // subtask... it is added only to the department directive") describes
    // behavior that no longer exists — AddSubtask was deliberately
    // restricted to the OPD (SuperAdmin) only, matching Create's own rule
    // (see DEF-005 in the Level 1 defect log). This test proves the real,
    // current behavior instead of the stale one.
    [Fact]
    public async Task Admin_AttemptingAddSubtaskOnALiveDirective_Gets403()
    {
        int directiveId;
        using (var db = _factory.CreateDbContext())
        {
            var admin = IntegrationTestHelpers.NewActiveUser("Admin", TargetDepartments.CPD, username: "cpd.admin2@dti.gov.ph");
            db.Users.Add(admin);
            await db.SaveChangesAsync();

            var directive = new TaskItem
            {
                TaskName = "CPD Directive",
                Description = "d",
                TaskLevel = TaskLevels.Main,
                TaskType = TaskTypes.DepartmentDirective,
                OwningDepartment = TargetDepartments.CPD,
                DueDate = DateTime.UtcNow.AddDays(5),
            };
            db.TaskItems.Add(directive);
            await db.SaveChangesAsync();
            directiveId = directive.Id;
        }

        using var client = NewClient();
        await LogInAsync(client, "cpd.admin2@dti.gov.ph");

        var dashboardPage = await client.GetAsync("/Dashboard/AdminDashboard");
        var token = await IntegrationTestHelpers.ExtractAntiforgeryTokenAsync(dashboardPage);

        var response = await client.PostAsync("/Tasks/AddSubtask", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = directiveId.ToString(),
            ["subtaskName"] = "New Subtask",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var verifyDb = _factory.CreateDbContext();
        var subtaskCount = await verifyDb.TaskItems.CountAsync(t => t.ParentTaskId == directiveId);
        Assert.Equal(0, subtaskCount); // nothing was added
    }
}
