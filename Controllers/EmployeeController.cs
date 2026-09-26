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
        // GET: /Employee/Update/5
[HttpGet]
public async Task<IActionResult> Update(int id)
{
    var task = await GetAccessibleTaskAsync(id);
    if (task == null)
    {
        return NotFound();
    }
    return View(task);
}

// POST: /Employee/Update
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Update(TaskProgressUpdateViewModel model)
{
    var userId = HttpContext.Session.GetInt32("UserId");
    var task = await GetAccessibleTaskAsync(model.Id);
    if (task == null)
    {
        return NotFound();
    }

    // Progress/Status now live per-assignee. An elevated user can OPEN any
    // task via GetAccessibleTaskAsync without being an assignee themselves —
    // there's nothing here for them to update.
    var assignment = task.Assignments.FirstOrDefault(a => a.UserId == userId);
    if (assignment == null)
    {
        return NotFound();
    }

    if (!ModelState.IsValid)
    {
        return View(task);
    }

    var error = await ApplyProgressUpdateAsync(task, assignment, model.Progress, model.RequestedStatus);
    if (error != null)
    {
        TempData["ErrorMessage"] = error;
        return RedirectToAction(nameof(TaskManagement));
    }

    TempData["SuccessMessage"] = "Progress updated successfully!";
    return RedirectToAction(nameof(TaskManagement));
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
    await PermanentlyDeleteTasksAsync(new[] { task }, userId.Value);

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

    await PermanentlyDeleteTasksAsync(tasks, userId.Value);

    TempData["SuccessMessage"] = tasks.Count == 1
        ? "1 completed task was deleted."
        : $"{tasks.Count} completed tasks were deleted.";
    return RedirectToAction(nameof(TaskManagement));
}

// Permanently deletes the given tasks (all in one transaction) with their
// assignments, proof submissions, comments, activity history, and
// notifications; recalculates any OPD directive a deleted subtask belonged
// to; then removes the proof files from disk. Callers must have already
// checked the delete rules and loaded Assignments, Submissions, Activities,
// and Comments (tracked) on every task.
private async Task PermanentlyDeleteTasksAsync(IReadOnlyCollection<TaskItem> tasks, int deletedByUserId)
{
    var taskIds = tasks.Select(t => t.Id).ToList();
    var parentTaskIds = tasks
        .Where(t => t.ParentTaskId.HasValue)
        .Select(t => t.ParentTaskId!.Value)
        .Distinct()
        .ToList();
    var proofFiles = tasks
        .SelectMany(t => t.Submissions.Select(s => new { TaskId = t.Id, File = s.ProofStoredFileName }))
        .Where(f => !string.IsNullOrWhiteSpace(f.File))
        .ToList();
    var deletedNames = tasks.Select(t => new { t.Id, t.TaskName }).ToList();

    await using (var transaction = await _context.Database.BeginTransactionAsync())
    {
        // Notifications only lose their task link (SET NULL) on delete, which
        // would leave messages pointing at a task that no longer exists.
        await _context.Notifications
            .Where(n => n.RelatedTaskId.HasValue && taskIds.Contains(n.RelatedTaskId.Value))
            .ExecuteDeleteAsync();

        // Children first, explicitly: activities reference submissions and
        // submissions reference assignments (both restricted), so EF deletes
        // them in dependency order before the tasks themselves.
        foreach (var task in tasks)
        {
            _context.TaskActivities.RemoveRange(task.Activities);
            _context.TaskComments.RemoveRange(task.Comments);
            _context.TaskSubmissions.RemoveRange(task.Submissions);
            _context.TaskAssignments.RemoveRange(task.Assignments);
            _context.TaskItems.Remove(task);
        }
        await _context.SaveChangesAsync();

        // A deleted subtask no longer counts toward its OPD directive, so
        // recompute each affected directive from what remains.
        foreach (var parentId in parentTaskIds)
        {
            await _taskAssignments.RecalculateMainTaskAsync(parentId);
        }
        if (parentTaskIds.Count > 0)
        {
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
    }

    // Files go only after the database commit, so a failed delete never
    // leaves submissions pointing at missing files. Best effort: a file that
    // can't be removed is logged, not shown to the user.
    var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ProofUploads");
    foreach (var proof in proofFiles)
    {
        try
        {
            var fullPath = Path.Combine(storageRoot, Path.GetFileName(proof.File));
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete proof file {File} for deleted task {TaskId}.", proof.File, proof.TaskId);
        }
    }

    // The tasks' own activity history is gone, so the server log is the
    // remaining record of who deleted what.
    foreach (var deleted in deletedNames)
    {
        _logger.LogInformation("Task {TaskId} \"{TaskName}\" was permanently deleted by user {UserId}.",
            deleted.Id, deleted.TaskName, deletedByUserId);
    }
}

// Applies one assignee's progress change with the workflow rules both the
// Update form and the Details slider must follow. Returns an error message,
// or null once the change is saved. Only ever writes to the current user's
// own assignment (plus the derived task/main-task rollups).
private async Task<string?> ApplyProgressUpdateAsync(TaskItem task, TaskAssignment assignment, int progress, string? requestedStatus)
{
    var currentStatus = (assignment.Status ?? "pending").Trim().ToLowerInvariant();

    if (currentStatus == "completed")
    {
        return "This task is already completed and can no longer be updated.";
    }

    // Locked while awaiting Admin review — progress/status are frozen until
    // a decision comes back (Completed or Returned for Correction).
    if (currentStatus == "for-review")
    {
        return "This task is awaiting review and can't be updated right now.";
    }

    var requested = (requestedStatus ?? currentStatus).Trim().ToLowerInvariant();
    var nextStatus = currentStatus;

    // The only manual transition allowed from here: Pending -> In Progress.
    if (currentStatus == TaskWorkflow.Pending && requested == TaskWorkflow.InProgress)
    {
        nextStatus = TaskWorkflow.InProgress;
    }

    // Moving progress off zero implies work has started, even if the
    // checkbox wasn't also ticked.
    if (progress > 0 && nextStatus == TaskWorkflow.Pending)
    {
        nextStatus = TaskWorkflow.InProgress;
    }

    // Defense in depth: reject a tampered RequestedStatus that doesn't match
    // a legal transition, even though the UI never offers one. Reaching
    // Completed happens only via TasksController.Review, never from here.
    if (!TaskWorkflow.CanTransition(currentStatus, nextStatus) || nextStatus == TaskWorkflow.Completed)
    {
        return "That status change isn't allowed.";
    }

    assignment.Progress = progress;
    assignment.Status = nextStatus;
    // AssigneeId, DueDate, Priority, TaskName, Description, CreatedAt:
    // untouched, because nothing above ever assigns to them. Other
    // assignees' TaskAssignment rows are untouched too — this only ever
    // writes to the current user's own assignment.

    _taskAssignments.RecalculateOverallStatus(task);

    // If this task is itself a subtask of an OPD Main Task, roll the change
    // up so the Main Task's own Status/Progress stays current too.
    await _taskAssignments.PropagateToParentMainTaskAsync(task);

    await _context.SaveChangesAsync();
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
    assignment.Status = TaskWorkflow.ForReview;

    // Requirement 8: this is where "did everyone submit yet" gets
    // re-evaluated — the task only flips to ForReview once every
    // assignment reaches ForReview/Completed.
    _taskAssignments.RecalculateOverallStatus(task);

    // If this task is itself a subtask of an OPD Main Task, roll the change
    // up so the Main Task's own Status/Progress stays current too.
    await _taskAssignments.PropagateToParentMainTaskAsync(task);

    await _context.SaveChangesAsync(); // submission.Id is only assigned after this save

    if (userId.HasValue)
    {
        TaskActivityLogger.Log(_context, task.Id, userId.Value, TaskActivityType.ProofSubmitted,
            status == TaskWorkflow.ReturnedForCorrection
                ? "Corrected proof resubmitted for review."
                : "Proof of completion submitted for review.",
            submission.Id);
        await _context.SaveChangesAsync();
    }

    // Direct Admin Task proofs are reviewed by OPD, not a department Admin,
    // and the submitter there IS an Admin — so no department alert for those.
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

    TempData["SuccessMessage"] = status == "returned-for-correction"
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

    var query = _context.TaskItems
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