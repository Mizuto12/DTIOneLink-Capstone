using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Services;
using DTIOneLink.Filters;
using DTIOneLink.Security;
using DTIOneLink.Models;   // <-- add this if it's missing


namespace DTIOneLink.Controllers
{
    [RequireLogin]
    public class EmployeeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly TaskAssignmentService _taskAssignments;
        private readonly NotificationService _notifications;
        private readonly ILogger<EmployeeController> _logger;

        public EmployeeController(
            AppDbContext context,
            TaskAssignmentService taskAssignments,
            NotificationService notifications,
            ILogger<EmployeeController> logger)
        {
            _context = context;
            _taskAssignments = taskAssignments;
            _notifications = notifications;
            _logger = logger;
        }

        // GET: /Employee or /Employee/Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var tasks = await AccessibleTasksQuery()
                .AsNoTracking() // read-only list
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }

       [HttpGet]
        public async Task<IActionResult> TaskManagement()
        {
            var tasks = await AccessibleTasksQuery()
                .AsNoTracking() // read-only list
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }
        // GET: /Employee/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var task = await GetAccessibleTaskAsync(id);
        if (task == null)
        {
            return NotFound();
        }
        return View(task);
    }
// POST: /Employee/UpdateProgress — the progress slider on the Details page.
// Same rules as the Update form (shared via ApplyProgressUpdateAsync), but
// answers with JSON so the slider can save without leaving the page.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UpdateProgress(int id, int progress)
{
    if (progress < 0 || progress > 100)
    {
        return BadRequest(new { message = "Progress must be between 0 and 100." });
    }

    var userId = HttpContext.Session.GetInt32("UserId");
    var task = await GetAccessibleTaskAsync(id);
    var assignment = task?.Assignments.FirstOrDefault(a => a.UserId == userId);
    if (task == null || assignment == null)
    {
        return NotFound(new { message = "You are not assigned to this task." });
    }

    var error = await ApplyProgressUpdateAsync(task, assignment, progress, requestedStatus: null);
    if (error != null)
    {
        return BadRequest(new { message = error });
    }

    return Json(new
    {
        progress = assignment.Progress,
        status = assignment.Status,
        taskProgress = task.Progress,
        taskStatus = task.Status
    });
}

// POST: /Employee/ToggleChecklistItem — check/uncheck one of the current
// user's own checklist items. Same status-lock rules as the progress
// slider (ApplyProgressUpdateAsync): frozen once Completed or ForReview.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ToggleChecklistItem(int id)
{
    var userId = HttpContext.Session.GetInt32("UserId");
    var item = await _context.TaskChecklistItems
        .Include(c => c.TaskAssignment).ThenInclude(a => a!.Task)
        .FirstOrDefaultAsync(c => c.Id == id);

    if (item?.TaskAssignment == null || item.TaskAssignment.UserId != userId)
    {
        return NotFound();
    }

    var task = item.TaskAssignment.Task!;
    var assignmentStatus = TaskWorkflow.Normalize(item.TaskAssignment.Status);
    if (assignmentStatus is TaskWorkflow.Completed or TaskWorkflow.ForReview)
    {
        return BadRequest(new { message = "This task can't be updated right now." });
    }

    item.IsDone = !item.IsDone;
    item.DoneAt = item.IsDone ? DateTime.UtcNow : null;

    // Re-load the assignment's full checklist (just the one item above is
    // tracked, not its siblings) to recompute Progress correctly.
    await _context.Entry(item.TaskAssignment).Collection(a => a.ChecklistItems).LoadAsync();
    TaskAssignmentService.RecalculateProgressFromChecklist(item.TaskAssignment);

    // Same "progress off zero implies started" rule ApplyProgressUpdateAsync
    // applies to the manual slider.
    if (item.TaskAssignment.Progress > 0 && assignmentStatus == TaskWorkflow.Pending)
    {
        item.TaskAssignment.Status = TaskWorkflow.InProgress;
    }
    item.TaskAssignment.RowVersion = Guid.NewGuid().ToByteArray();

    _taskAssignments.RecalculateOverallStatus(task);
    await _taskAssignments.PropagateToParentMainTaskAsync(task);

    try
    {
        await _context.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return BadRequest(new { message = "This task's status just changed (maybe it was reviewed, or you updated it from another tab). Please refresh and try again." });
    }

    return Json(new
    {
        isDone = item.IsDone,
        progress = item.TaskAssignment.Progress,
        status = item.TaskAssignment.Status,
        taskProgress = task.Progress,
        taskStatus = task.Status
    });
}

// POST: /Employee/Delete — permanently deletes a COMPLETED task, for
// everyone. Allowed only for someone assigned to the task, and only once the
// whole task is Completed (every assignee approved). Removes the task with
// its assignments, proof submissions (and their files), comments, activity
// history, and notifications. Cannot be undone.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Delete(int id)
{
    var userId = HttpContext.Session.GetInt32("UserId");
    var task = await GetAccessibleTaskAsync(id);
    if (task == null || userId == null || !task.Assignments.Any(a => a.UserId == userId.Value))
    {
        return NotFound();
    }

    if (!string.Equals(task.Status, TaskWorkflow.Completed, StringComparison.OrdinalIgnoreCase))
    {
        TempData["ErrorMessage"] = "Only completed tasks can be deleted.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // OPD main tasks and any task with subtasks are never deletable here:
    // their subtasks reference them (restricted foreign key) and they aren't
    // an employee's own work item.
    if (task.TaskLevel == TaskLevels.Main || await _context.TaskItems.AnyAsync(t => t.ParentTaskId == task.Id))
    {
        TempData["ErrorMessage"] = "This task can't be deleted because other tasks depend on it.";
        return RedirectToAction(nameof(Details), new { id });
    }

    var taskName = task.TaskName;
    await _taskAssignments.PermanentlyDeleteTasksAsync(new[] { task }, userId.Value, _logger);

    TempData["SuccessMessage"] = $"\"{taskName}\" was deleted.";
    return RedirectToAction(nameof(TaskManagement));
}

// POST: /Employee/DeleteAllCompleted — the "Delete all completed tasks"
// option in the Completed column's menu. Deletes every task the signed-in
// user could delete one by one (same rules as Delete): they're assigned,
// the WHOLE task is Completed, it isn't an OPD main task, and nothing
// depends on it. Shared tasks still waiting on other assignees are kept.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> DeleteAllCompleted()
{
    var userId = HttpContext.Session.GetInt32("UserId");
    if (userId == null)
    {
        return RedirectToAction("Login", "Account");
    }

    var tasks = await AccessibleTasksQuery()
        .Include(t => t.Submissions)
        .Include(t => t.Activities)
        .Include(t => t.Comments)
        .Where(t => t.Assignments.Any(a => a.UserId == userId.Value)
            && t.Status == TaskWorkflow.Completed
            && t.TaskLevel != TaskLevels.Main
            && !_context.TaskItems.Any(child => child.ParentTaskId == t.Id))
        .AsSplitQuery()
        .ToListAsync();

    if (tasks.Count == 0)
    {
        TempData["ErrorMessage"] = "There are no completed tasks you can delete.";
        return RedirectToAction(nameof(TaskManagement));
    }

    await _taskAssignments.PermanentlyDeleteTasksAsync(tasks, userId.Value, _logger);

    TempData["SuccessMessage"] = tasks.Count == 1
        ? "1 completed task was deleted."
        : $"{tasks.Count} completed tasks were deleted.";
    return RedirectToAction(nameof(TaskManagement));
}

// Applies one assignee's progress change with the workflow rules both the
// Update form and the Details slider must follow. Returns an error message,
// or null once the change is saved. Only ever writes to the current user's
// own assignment (plus the derived task/main-task rollups).
private async Task<string?> ApplyProgressUpdateAsync(TaskItem task, TaskAssignment assignment, int progress, string? requestedStatus)
{
    // Core lock rules and status transition live in TaskAssignmentService
    // now, shared with TasksController.UpdateAssignmentProgress (T10) —
    // this wrapper only owns the save/concurrency handling, same as before.
    var error = await _taskAssignments.ApplyProgressChangeAsync(task, assignment, progress, requestedStatus);
    if (error != null)
    {
        return error;
    }

    try
    {
        await _context.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        // Same reasoning as SubmitProof's own concurrency guard: this
        // assignment changed (a Review decision, another tab) between
        // load and save.
        return "This task's status just changed (maybe it was reviewed, or you updated it from another tab). Please refresh and try again.";
    }
    return null;
}
// GET: /Employee/SubmitProof/5
[HttpGet]
public async Task<IActionResult> SubmitProof(int id)
{
    var userId = HttpContext.Session.GetInt32("UserId");
    var task = await GetAccessibleTaskAsync(id);

    if (task == null)
    {
        return NotFound();
    }

    var assignment = task.Assignments.FirstOrDefault(a => a.UserId == userId);
    if (assignment == null)
    {
        return NotFound();
    }

var status = (assignment.Status ?? "pending").Trim().ToLowerInvariant();
if (!TaskWorkflow.CanTransition(status, TaskWorkflow.ForReview))
{
    // Nothing to submit — either already done, or already awaiting
    // admin review. Send them back rather than showing a dead form.
    TempData["ErrorMessage"] = status == "completed"
        ? "This task is already completed."
        : "This task is already awaiting review.";
    return RedirectToAction(nameof(Details), new { id = task.Id });
}
if (!TaskWorkflow.CanSubmitProof(status, assignment.Progress))
{
    TempData["ErrorMessage"] = $"Reach at least {TaskWorkflow.MinProgressToSubmitProof}% progress before submitting proof.";
    return RedirectToAction(nameof(Details), new { id = task.Id });
}
    return View(task);
}

// POST: /Employee/SubmitProof
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SubmitProof(ProofSubmissionViewModel model)
{
    var userId = HttpContext.Session.GetInt32("UserId");
    var task = await GetAccessibleTaskAsync(model.Id);

    if (task == null)
    {
        return NotFound();
    }

    var assignment = task.Assignments.FirstOrDefault(a => a.UserId == userId);
    if (assignment == null)
    {
        return NotFound();
    }

var status = (assignment.Status ?? "pending").Trim().ToLowerInvariant();

// Allowed from: pending, in-progress, returned-for-correction — i.e.
// anywhere TaskWorkflow permits a transition into ForReview.
if (!TaskWorkflow.CanTransition(status, TaskWorkflow.ForReview))
{
    TempData["ErrorMessage"] = "This task can no longer accept a proof submission.";
    return RedirectToAction(nameof(Details), new { id = task.Id });
}
if (!TaskWorkflow.CanSubmitProof(status, assignment.Progress))
{
    TempData["ErrorMessage"] = $"Reach at least {TaskWorkflow.MinProgressToSubmitProof}% progress before submitting proof.";
    return RedirectToAction(nameof(Details), new { id = task.Id });
}

    if (!ModelState.IsValid)
    {
        return View(task);
    }

    var (isValid, error) = ProofFileValidator.Validate(model.ProofFile!);
    if (!isValid)
    {
        ModelState.AddModelError(nameof(model.ProofFile), error!);
        return View(task);
    }

    var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ProofUploads");
    Directory.CreateDirectory(storageRoot);

    var extension = Path.GetExtension(model.ProofFile!.FileName);
    var storedFileName = $"{Guid.NewGuid()}{extension}";
    var fullPath = Path.Combine(storageRoot, storedFileName);

    using (var fileStream = new FileStream(fullPath, FileMode.Create))
    {
        await model.ProofFile.CopyToAsync(fileStream);
    }

    // New row every time — this is what makes "submission history" real,
    // rather than overwriting the same fields on every resubmit. Now tied
    // to the specific assignee's TaskAssignment, not just the Task, so two
    // assignees on the same task each build their own submission history.
    var submission = new TaskSubmission
    {
        TaskId = task.Id,
        TaskAssignmentId = assignment.Id,
        ProofFileName = Path.GetFileName(model.ProofFile.FileName),
        ProofStoredFileName = storedFileName,
        Remarks = model.Remarks,
        SubmittedAt = DateTime.UtcNow
    };

    _context.TaskSubmissions.Add(submission);
    assignment.RowVersion = Guid.NewGuid().ToByteArray();

    // RequiresReview = false lets this assignment complete immediately
    // instead of waiting on an Admin decision — but only straight from
    // InProgress (TaskWorkflow.AllowedTransitions). A resubmission after
    // Returned for Correction always goes through review regardless: an
    // Admin already engaged with it once, so CanTransition correctly
    // refuses the bypass here and this falls through to the normal path.
    var bypassesReview = !task.RequiresReview && TaskWorkflow.CanTransition(status, TaskWorkflow.Completed);
    if (bypassesReview)
    {
        assignment.Status = TaskWorkflow.Completed;
        assignment.Progress = 100;
        // No Admin ever decides this one — "approved" keeps it out of every
        // "pending review" view/query and counts it in completion stats
        // exactly like a human-approved one would (DashboardController's
        // completion trend reads Decision == "approved").
        submission.Decision = "approved";
        submission.DecidedAt = submission.SubmittedAt;
    }
    else
    {
        assignment.Status = TaskWorkflow.ForReview;
        // Decision 5: the reviewer's clock starts now, not the task's own
        // due date — recomputed fresh on every resubmission after a Return.
        task.ReviewDueDate = submission.SubmittedAt + TaskWorkflow.ReviewWindow;
    }

    // Requirement 8: this is where "did everyone submit yet" gets
    // re-evaluated — the task only flips to ForReview once every
    // assignment reaches ForReview/Completed.
    _taskAssignments.RecalculateOverallStatus(task);

    // If this task is itself a subtask of an OPD Main Task, roll the change
    // up so the Main Task's own Status/Progress stays current too.
    await _taskAssignments.PropagateToParentMainTaskAsync(task);

    try
    {
        await _context.SaveChangesAsync(); // submission.Id is only assigned after this save
    }
    catch (DbUpdateConcurrencyException)
    {
        // Someone else (a Review decision, another device) changed this
        // same assignment between when the page loaded and this submit —
        // without this check, both writes would silently apply in
        // whichever order they land, one clobbering the other. The proof
        // file already written to disk has no DB row pointing to it now,
        // same cleanup as UploadAttachment's own failure path.
        try { System.IO.File.Delete(fullPath); } catch { /* best-effort */ }
        TempData["ErrorMessage"] = "This task's status just changed (maybe it was reviewed, or you updated it from another tab). Please refresh and try again.";
        return RedirectToAction(nameof(Details), new { id = task.Id });
    }
    catch (DbUpdateException)
    {
        // Any OTHER save failure (a transient SQL error, a timeout) — not
        // just the concurrency case above. Same cleanup reasoning:
        // RecordsController.UploadAttachment already catches broadly here
        // for exactly this pattern (a file written to disk with no DB row
        // left to find it by if the insert never lands).
        try { System.IO.File.Delete(fullPath); } catch { /* best-effort */ }
        TempData["ErrorMessage"] = "Could not submit your proof right now. Please try again.";
        return RedirectToAction(nameof(Details), new { id = task.Id });
    }

    if (userId.HasValue)
    {
        TaskActivityLogger.Log(_context, task.Id, userId.Value, TaskActivityType.ProofSubmitted,
            bypassesReview
                ? "Proof of completion submitted — this task doesn't require review, so it's now complete."
                : status == TaskWorkflow.ReturnedForCorrection
                    ? "Corrected proof resubmitted for review."
                    : "Proof of completion submitted for review.",
            submission.Id);
        await _context.SaveChangesAsync();
    }

    if (bypassesReview)
    {
        await _notifications.NotifyTaskApprovedAsync(assignment.UserId, task.Id, task.TaskName);

        // Same as TasksController.Review's approve branch: this completion
        // may have finished the last open subtask of a Department
        // Directive, even though no Admin ever reviewed it.
        if (task.ParentTaskId.HasValue)
        {
            await _notifications.NotifyOpdDirectiveCompletedAsync(task.ParentTaskId.Value);
        }
    }
    else
    {
        // Direct Admin Task proofs are reviewed by OPD, not a department
        // Admin, and the submitter there IS an Admin — so no department
        // alert for those.
        var submitterName = HttpContext.Session.GetString("FullName") ?? "An employee";
        if (!TaskTypes.IsAssignedByOpd(task.TaskType))
        {
            await _notifications.NotifyAdminsProofSubmittedAsync(task.Id, task.TaskName, submitterName, submission.Id);
        }
        else
        {
            // Direct Admin Task: OPD (not a department Admin) reviews this proof.
            await _notifications.NotifyOpdProofSubmittedAsync(task.Id, task.TaskName, submitterName, submission.Id);
        }
    }

    TempData["SuccessMessage"] = bypassesReview
        ? "Proof of completion submitted. This task doesn't require review, so it's complete."
        : status == "returned-for-correction"
            ? "Corrected submission sent for review."
            : "Proof of completion submitted. Your task is now awaiting review.";

    return RedirectToAction(nameof(Details), new { id = task.Id });
}
// GET: /Employee/DownloadProof/{submissionId}
[HttpGet]
public async Task<IActionResult> DownloadProof(int submissionId)
{
    var submission = await GetAccessibleSubmissionAsync(submissionId);
    if (submission == null)
    {
        return NotFound();
    }

    var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ProofUploads");
    var fullPath = Path.Combine(storageRoot, submission.ProofStoredFileName);

    if (!System.IO.File.Exists(fullPath))
    {
        return NotFound();
    }

    var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
    return File(bytes, "application/octet-stream", submission.ProofFileName);
}

// Inline-viewable types only (what a browser can render by itself, no
// plugin): PDF and the three image extensions ProofFileValidator allows.
// DOCX/XLSX are ZIP containers a browser can't display — those fall back
// to an ordinary download.
private static readonly Dictionary<string, string> PreviewableContentTypes = new(StringComparer.OrdinalIgnoreCase)
{
    [".pdf"] = "application/pdf",
    [".jpg"] = "image/jpeg",
    [".jpeg"] = "image/jpeg",
    [".png"] = "image/png",
};

// GET: /Employee/PreviewProof/{submissionId} — same file, same
// authorization as DownloadProof (GetAccessibleSubmissionAsync), but
// rendered inline (no Content-Disposition: attachment) for a type the
// browser can actually display. Anything else falls back to the exact
// same forced-download response DownloadProof gives.
[HttpGet]
public async Task<IActionResult> PreviewProof(int submissionId)
{
    var submission = await GetAccessibleSubmissionAsync(submissionId);
    if (submission == null)
    {
        return NotFound();
    }

    var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ProofUploads");
    var fullPath = Path.Combine(storageRoot, submission.ProofStoredFileName);

    if (!System.IO.File.Exists(fullPath))
    {
        return NotFound();
    }

    var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
    var extension = Path.GetExtension(submission.ProofFileName);

    if (!PreviewableContentTypes.TryGetValue(extension, out var contentType))
    {
        // Fallback to download for unsupported file types — same response
        // DownloadProof gives, just reached from the preview link too.
        return File(bytes, "application/octet-stream", submission.ProofFileName);
    }

    // No fileDownloadName: that's what makes File() render inline instead
    // of forcing a save-as dialog, the same way DownloadProof's third
    // argument is what forces one.
    return File(bytes, contentType);
}
// Every task-scoped action goes through this instead of querying
// _context.TaskItems directly. Admin/Supervisor bypass the assignment
// check entirely; everyone else only ever sees tasks they have a
// TaskAssignment row on (any one of possibly several assignees).
        // SuperAdmin: office-wide, via the same permission TasksController
        // uses — not a role-string comparison.
        private bool IsOfficeWideTaskManager() =>
            RolePermissions.Has(HttpContext.Session.GetString("UserRole"), Permissions.ManageOfficeWideTasks);

        // Admin/Supervisor: department-scoped, NOT unrestricted. Previously
        // this controller treated "Admin or Supervisor" as full bypass —
        // that let an Admin in one department view/update tasks in another.
        private bool IsDepartmentTaskManager()
        {
            var role = HttpContext.Session.GetString("UserRole");
            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase);
        }

        private string? CurrentUserDepartment() => HttpContext.Session.GetString("UserDepartment");

        // Every task-scoped action goes through this instead of querying
        // _context.TaskItems directly. Three tiers: SuperAdmin sees
        // everything; Admin/Supervisor see only tasks touching their own
        // department (via an assignee, or — for a Main Task, which has no
        // Assignments — TargetDepartment); everyone else (Employee) only
        // sees tasks they personally hold a TaskAssignment row on.
        private async Task<TaskItem?> GetAccessibleTaskAsync(int taskId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return null; // RequireLoginAttribute should already have caught this
            }

            var query = _context.TaskItems
                .Include(t => t.Assignee)
                .Include(t => t.CreatedBy)
                .Include(t => t.ParentTask)
                .Include(t => t.Assignments).ThenInclude(a => a.User)
                .Include(t => t.Assignments).ThenInclude(a => a.ChecklistItems)
                .Include(t => t.Submissions).ThenInclude(s => s.ValidatedBy)
                .Include(t => t.Activities).ThenInclude(a => a.PerformedBy)
                .Include(t => t.Activities).ThenInclude(a => a.RelatedSubmission)
                .Include(t => t.Comments).ThenInclude(c => c.Author)
                // Four independent collections (Assignments, Submissions,
                // Activities, Comments). As one JOIN their row counts
                // multiply, so a busy task returned thousands of duplicate
                // rows; separate queries return each row once. Tracking is
                // kept — Update/SubmitProof/AddComment save through this.
                .AsSplitQuery()
                .AsQueryable();

            if (IsOfficeWideTaskManager())
            {
                return await query.FirstOrDefaultAsync(t => t.Id == taskId);
            }

            if (IsDepartmentTaskManager())
            {
                // Same effective-department priority as AccessibleTasksQuery:
                // own OwningDepartment, then parent's (covers a subtask, main
                // or otherwise), then legacy assignee fallback only when both
                // are null. Previously this only checked TaskLevel == "main"
                // for OwningDepartment, so a department-scoped Admin could
                // open an out-of-department subtask directly by id.
                var department = CurrentUserDepartment();
                return await query.FirstOrDefaultAsync(t => t.Id == taskId &&
                    (t.Assignments.Any(a => a.UserId == userId) ||
                     (t.OwningDepartment != null && t.OwningDepartment == department) ||
                     (t.OwningDepartment == null && t.ParentTask != null && t.ParentTask.OwningDepartment == department) ||
                     (t.OwningDepartment == null && (t.ParentTask == null || t.ParentTask.OwningDepartment == null) &&
                        t.Assignments.Any(a => a.User != null && a.User.Department == department))));
            }

            return await query.FirstOrDefaultAsync(t => t.Id == taskId && t.Assignments.Any(a => a.UserId == userId));
        }
// Same pattern for submission history / proof downloads, since those are
// now scoped through the parent TaskAssignment's UserId, not the Task's
// legacy AssigneeId.
        // Same tiering as GetAccessibleTaskAsync, applied to submission
        // history / proof downloads, since those are scoped through the
        // parent TaskAssignment's UserId, not the Task's legacy AssigneeId.
        private async Task<TaskSubmission?> GetAccessibleSubmissionAsync(int submissionId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return null;
            }

            var query = _context.TaskSubmissions
                .Include(s => s.Task).ThenInclude(t => t!.ParentTask)
                .Include(s => s.TaskAssignment).ThenInclude(a => a!.User)
                .AsQueryable();

            if (IsOfficeWideTaskManager())
            {
                return await query.FirstOrDefaultAsync(s => s.Id == submissionId);
            }

            if (IsDepartmentTaskManager())
            {
                // Same effective-department priority as GetAccessibleTaskAsync:
                // the submission's Task.OwningDepartment first, then its
                // parent Main Task's, then the assignee's own Department only
                // as a legacy fallback when both are null. Previously this
                // checked only the assignee's Department, so a submission on
                // an out-of-department subtask could be downloaded if it
                // happened to share an assignee's department by coincidence.
                var department = CurrentUserDepartment();
                return await query.FirstOrDefaultAsync(s => s.Id == submissionId &&
                    s.Task != null &&
                    ((s.TaskAssignment != null && s.TaskAssignment.UserId == userId) ||
                     (s.Task.OwningDepartment != null && s.Task.OwningDepartment == department) ||
                     (s.Task.OwningDepartment == null && s.Task.ParentTask != null && s.Task.ParentTask.OwningDepartment == department) ||
                     (s.Task.OwningDepartment == null && (s.Task.ParentTask == null || s.Task.ParentTask.OwningDepartment == null) &&
                        s.TaskAssignment != null && s.TaskAssignment.User != null && s.TaskAssignment.User.Department == department)));
            }

            return await query.FirstOrDefaultAsync(s => s.Id == submissionId && s.TaskAssignment != null && s.TaskAssignment.UserId == userId);
        }
// Same access rule as GetAccessibleTaskAsync, but returns a queryable list
// scope rather than a single task — used by Index/TaskManagement so the
// "which tasks can this user see" rule lives in exactly one place.
private IQueryable<TaskItem> AccessibleTasksQuery()
{
    var userId = HttpContext.Session.GetInt32("UserId");

    // AsSplitQuery: without it, a task with multiple assignees comes back
    // as one joined row PER assignee (the Assignments.User include
    // multiplies the whole row) — matches GetAccessibleTaskAsync/
    // DeleteAllCompleted in this same file, which already split for the
    // identical Include shape; this one was just missed.
    var query = _context.TaskItems
        .AsSplitQuery()
        .Include(t => t.Assignee)
        .Include(t => t.ParentTask)
        .Include(t => t.Assignments).ThenInclude(a => a.User)
        .AsQueryable();

    // SuperAdmin: office-wide, no department filter.
    if (IsOfficeWideTaskManager())
    {
        return query;
    }

    // Admin/Supervisor: department-scoped, not a full bypass — this was
    // previously unfiltered for this role, letting an Admin see every
    // department's tasks through /Employee/Index or /TaskManagement.
    if (IsDepartmentTaskManager())
    {
        var department = CurrentUserDepartment();
        return query.Where(t =>
            // Their own assignment, wherever the task belongs (e.g. a
            // Whole Office task owned by the OPD).
            t.Assignments.Any(a => a.UserId == userId) ||
            (t.OwningDepartment != null && t.OwningDepartment == department) ||
            (t.OwningDepartment == null && t.ParentTask != null && t.ParentTask.OwningDepartment == department) ||
            (t.OwningDepartment == null && (t.ParentTask == null || t.ParentTask.OwningDepartment == null) &&
                t.Assignments.Any(a => a.User != null && a.User.Department == department)));
    }

    // Employee: only tasks they personally hold a TaskAssignment row on.
    return query.Where(t => t.Assignments.Any(a => a.UserId == userId));
}
// POST: /Employee/AddComment
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> AddComment(int taskId, string text)
{
    var task = await GetAccessibleTaskAsync(taskId);
    if (task == null)
    {
        return NotFound();
    }

    if (string.IsNullOrWhiteSpace(text))
    {
        TempData["ErrorMessage"] = "Comment can't be empty.";
        return RedirectToAction(nameof(Details), new { id = taskId });
    }
    if (text.Trim().Length > 1000)
    {
        TempData["ErrorMessage"] = "Comment is too long (maximum 1000 characters).";
        // The redirect below is a full navigation — without this, the
        // textarea comes back empty and whatever was typed is lost.
        TempData["CommentDraft"] = text;
        return RedirectToAction(nameof(Details), new { id = taskId });
    }

    var userId = HttpContext.Session.GetInt32("UserId");
    if (userId == null)
    {
        return NotFound();
    }

    _context.TaskComments.Add(new TaskComment
    {
        TaskId = taskId,
        AuthorUserId = userId.Value,
        Text = text.Trim(),
        CreatedAt = DateTime.UtcNow
    });

    TaskActivityLogger.Log(_context, taskId, userId.Value, "commented", "Added a comment.");

    await _context.SaveChangesAsync();

    // Notify every assignee on the task except the person who wrote the comment.
    var authorName = HttpContext.Session.GetString("FullName") ?? "Someone";
    var commentRecipientIds = task.Assignments
        .Select(a => a.UserId)
        .Where(id => id != userId.Value)
        .Distinct()
        .ToList();

    foreach (var recipientId in commentRecipientIds)
    {
        await _notifications.NotifyTaskCommentedAsync(recipientId, task.Id, task.TaskName, authorName);
    }

    // Department Admins are alerted only when an Employee comments, not when
    // an Admin/Supervisor/SuperAdmin does.
    if (string.Equals(HttpContext.Session.GetString("UserRole"), "Employee", StringComparison.OrdinalIgnoreCase))
    {
        await _notifications.NotifyAdminsTaskCommentedAsync(task.Id, task.TaskName, authorName);
    }

    // OPD hears about Admin/Supervisor comments (escalations) on OPD-issued
    // tasks. The service returns no recipients for a non-OPD task.
    var commenterRole = HttpContext.Session.GetString("UserRole");
    if (string.Equals(commenterRole, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(commenterRole, "Supervisor", StringComparison.OrdinalIgnoreCase))
    {
        await _notifications.NotifyOpdTaskCommentedAsync(task.Id, task.TaskName, authorName);
    }

    return RedirectToAction(nameof(Details), new { id = taskId });
}
    }
}