using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Models;
using DTIOneLink.Data;
using DTIOneLink.Services;

namespace DTIOneLink.Controllers
{
    public class TasksController : Controller
    {
        private readonly AppDbContext _context;
        private readonly NotificationService _notifications;
        private readonly TaskAssignmentService _taskAssignments;

        public TasksController(AppDbContext context, NotificationService notifications, TaskAssignmentService taskAssignments)
        {
            _context = context;
             _notifications = notifications;
            _taskAssignments = taskAssignments;
        }

        // GET: /Tasks/Index
        public async Task<IActionResult> Index()
        {
            var tasks = await _context.TaskItems
                .Include(t => t.Assignee)
                .Include(t => t.Assignments).ThenInclude(a => a.User)
                .Include(t => t.Submissions)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }

        // GET: /Tasks/Create
        public async Task<IActionResult> Create()
        {
            await PopulateEmployeesAsync();
            return View(new TaskCreateViewModel());
        }

        // POST: /Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TaskCreateViewModel model)
        {
            await ValidateAssigneeIdsAsync(model.AssigneeIds, nameof(model.AssigneeIds));

            if (!ModelState.IsValid)
            {
                await PopulateEmployeesAsync();
                return View(model);
            }

            var task = new TaskItem
            {
                TaskName = model.TaskName,
                DueDate = model.DueDate,
                Priority = model.Priority,
                Description = model.Description
            };

            _context.TaskItems.Add(task);
            await _context.SaveChangesAsync(); // need task.Id before creating assignments

            var createdByUserId = HttpContext.Session.GetInt32("UserId");
            _taskAssignments.AssignEmployees(task, model.AssigneeIds, createdByUserId);

            await _context.SaveChangesAsync();

            // new — notify every assignee once the task (and its assignment
            // rows) have Ids to link to.
            foreach (var assignment in task.Assignments)
            {
                await _notifications.NotifyTaskAssignedAsync(assignment.UserId, task.Id, task.TaskName);
            }

            TempData["SuccessMessage"] = "Task created successfully!";
            return RedirectToAction(nameof(Index));
        }
        // GET: /Tasks/Edit/5
 [HttpGet]
 public async Task<IActionResult> Edit(int id)
 {
     if (!IsElevated())
     {
        return StatusCode(403);
     }

     var task = await _context.TaskItems
         .Include(t => t.Assignments).ThenInclude(a => a.User)
         .FirstOrDefaultAsync(t => t.Id == id);
     if (task == null)
     {
         return NotFound();
     }

     var model = new TaskEditViewModel
     {
         Id = task.Id,
         TaskName = task.TaskName,
         AssigneeIds = task.Assignments.Select(a => a.UserId).ToList(),
         DueDate = task.DueDate,
         Priority = task.Priority,
         Description = task.Description
     };

    // Read-only display context for the "Task Overview" panel — not part of
    // TaskEditViewModel on purpose (Status/Progress/CreatedAt aren't editable
    // here; they're owned by the employee-facing Update/SubmitProof flow and
    // the Admin Review decision).
    ViewBag.TaskCode = $"TASK-{task.Id:D4}";
    ViewBag.CurrentStatus = task.Status;      // aggregate across all assignees
    ViewBag.CurrentProgress = task.Progress;  // aggregate across all assignees
    ViewBag.CreatedAt = task.CreatedAt;
    ViewBag.AssignmentSummaries = BuildAssignmentSummaries(task);

    // Initial suggestion for page load, before any JS runs. Recalculated
    // live via SuggestPriority whenever the due-date field changes.
    var suggestion = PrioritySuggestionService.Suggest(task.DueDate);
    ViewBag.SuggestedPriority = suggestion.Priority;
    ViewBag.SuggestedReason = suggestion.Reason;

    await PopulateEmployeesAsync();
    return View(model);
 }
// POST: /Tasks/Edit
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Edit(TaskEditViewModel model)
{
    if (!IsElevated())
    {
       return StatusCode(403);
    }

    var task = await _context.TaskItems
        .Include(t => t.Assignments).ThenInclude(a => a.User)
        .FirstOrDefaultAsync(t => t.Id == model.Id);
    if (task == null)
    {
        return NotFound();
    }

    await ValidateAssigneeIdsAsync(model.AssigneeIds, nameof(model.AssigneeIds));

    if (!ModelState.IsValid)
    {
        await RepopulateEditContextAsync(task);
        return View(model);
    }

    // new — snapshot the pre-edit values before they get overwritten below,
    // so we know what actually changed once the save is done.
    var oldDueDate = task.DueDate;
    var oldPriority = task.Priority;
    var oldAssigneeUserIds = task.Assignments.Select(a => a.UserId).ToList();

    // Only the editable fields — Progress, Status, CreatedAt, Submissions
    // are untouched, same discipline as Employee.Update's comment block.
    task.TaskName = model.TaskName;
    task.DueDate = model.DueDate;
    task.Priority = model.Priority;
    task.Description = model.Description;

    var changedByUserId = HttpContext.Session.GetInt32("UserId");
    var sync = await _taskAssignments.SyncAssignmentsAsync(task, model.AssigneeIds, changedByUserId);

    if (sync.BlockedRemovals.Count > 0)
    {
        ModelState.AddModelError(nameof(model.AssigneeIds),
            "Can't unassign someone who has already submitted proof for this task.");
        await RepopulateEditContextAsync(task);
        return View(model);
    }

    _taskAssignments.RecalculateOverallStatus(task);

    await _context.SaveChangesAsync();

    // new — notify only the affected employees, and only for what actually
    // changed. Newly added assignees get one "assigned to you" notice;
    // everyone who was already on the task before AND after this edit gets
    // notified about due-date/priority changes (each is independent, so
    // both can fire).
    foreach (var newUserId in sync.Added)
    {
        await _notifications.NotifyTaskReassignedAsync(newUserId, task.Id, task.TaskName);
    }

    var stillAssignedUserIds = oldAssigneeUserIds.Except(sync.Removed).ToList();
    foreach (var userId in stillAssignedUserIds)
    {
        if (oldDueDate != task.DueDate)
        {
            await _notifications.NotifyTaskDueDateChangedAsync(userId, task.Id, task.TaskName, task.DueDate);
        }
        if (oldPriority != task.Priority)
        {
            await _notifications.NotifyTaskPriorityChangedAsync(userId, task.Id, task.TaskName, task.Priority);
        }
    }

    TempData["SuccessMessage"] = "Task updated successfully!";
    return RedirectToAction(nameof(Index));
}

// Same elevation check as EmployeeController.GetAccessibleTaskAsync,
// just enforced as a hard gate since this whole controller is Admin/
// Supervisor territory rather than something Employees ever see scoped.
private bool IsElevated()
{
    var role = HttpContext.Session.GetString("UserRole");
    return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase);
}
private async Task PopulateEmployeesAsync()
{
    var employees = await _context.Users
        .Where(u => u.IsActive && u.Role == "Employee")
        .OrderBy(u => u.FullName)
        .ToListAsync();
    ViewBag.Employees = new SelectList(employees, "Id", "FullName");
}

// Checks every requested id is an active Employee, and that at least one
// was selected. Shared by Create and Edit so the rule lives in one place.
private async Task ValidateAssigneeIdsAsync(List<int> assigneeIds, string modelKey)
{
    if (assigneeIds == null || assigneeIds.Count == 0)
    {
        ModelState.AddModelError(modelKey, "Select at least one assignee.");
        return;
    }

    var distinct = assigneeIds.Distinct().ToList();
    var validCount = await _context.Users
        .CountAsync(u => distinct.Contains(u.Id) && u.IsActive && u.Role == "Employee");

    if (validCount != distinct.Count)
    {
        ModelState.AddModelError(modelKey, "One or more selected assignees are invalid.");
    }
}

private List<TaskAssignmentSummaryViewModel> BuildAssignmentSummaries(TaskItem task)
{
    return task.Assignments
        .OrderByDescending(a => a.IsPrimaryAssignee)
        .ThenBy(a => a.User?.FullName)
        .Select(a => new TaskAssignmentSummaryViewModel
        {
            Name = a.User?.FullName ?? "Unknown",
            Status = a.Status,
            Progress = a.Progress,
            IsPrimaryAssignee = a.IsPrimaryAssignee
        })
        .ToList();
}

// Re-hydrates the ViewBag context Edit's GET action sets, for re-rendering
// the Edit form after a POST validation failure.
private async Task RepopulateEditContextAsync(TaskItem task)
{
    ViewBag.TaskCode = $"TASK-{task.Id:D4}";
    ViewBag.CurrentStatus = task.Status;
    ViewBag.CurrentProgress = task.Progress;
    ViewBag.CreatedAt = task.CreatedAt;
    ViewBag.AssignmentSummaries = BuildAssignmentSummaries(task);

    var suggestion = PrioritySuggestionService.Suggest(task.DueDate);
    ViewBag.SuggestedPriority = suggestion.Priority;
    ViewBag.SuggestedReason = suggestion.Reason;

    await PopulateEmployeesAsync();
}
// GET: /Tasks/Review/5  (submissionId)
[HttpGet]
public async Task<IActionResult> Review(int id)
{
    if (!IsElevated())
    {
        return StatusCode(403);
    }

    var submission = await _context.TaskSubmissions
        .Include(s => s.Task).ThenInclude(t => t!.Assignee)
        .Include(s => s.TaskAssignment).ThenInclude(a => a!.User)
        .FirstOrDefaultAsync(s => s.Id == id);

    if (submission == null || submission.Task == null || submission.TaskAssignment == null)
    {
        return NotFound();
    }

    // Only the currently-pending submission on an assignee whose own
    // assignment is ForReview can be decided — an old, already-superseded
    // submission has nothing to act on.
    if (submission.TaskAssignment.Status?.ToLowerInvariant() != TaskWorkflow.ForReview || submission.Decision != null)
    {
        TempData["ErrorMessage"] = "This submission is no longer awaiting review.";
        return RedirectToAction(nameof(Index));
    }

    return View(submission);
}

// POST: /Tasks/Review
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Review(TaskSubmissionDecisionViewModel model)
{
    if (!IsElevated())
    {
        return StatusCode(403);
    }

    var submission = await _context.TaskSubmissions
        .Include(s => s.Task).ThenInclude(t => t!.Assignments)
        .Include(s => s.TaskAssignment)
        .FirstOrDefaultAsync(s => s.Id == model.SubmissionId);

    if (submission == null || submission.Task == null || submission.TaskAssignment == null)
    {
        return NotFound();
    }

    var task = submission.Task;
    var assignment = submission.TaskAssignment;
    var currentAssignmentStatus = assignment.Status ?? TaskWorkflow.Pending;

    if (currentAssignmentStatus.ToLowerInvariant() != TaskWorkflow.ForReview || submission.Decision != null)
    {
        TempData["ErrorMessage"] = "This submission is no longer awaiting review.";
        return RedirectToAction(nameof(Index));
    }

    var decision = model.Decision.Trim().ToLowerInvariant();
    string nextAssignmentStatus;
    string submissionDecision;

    switch (decision)
    {
        case "approve":
            nextAssignmentStatus = TaskWorkflow.Completed;
            submissionDecision = "approved";
            break;
        case "return":
            // Remarks are mandatory on Return specifically — the employee
            // needs to know exactly what to fix. Approve has no such
            // requirement, since there's nothing to explain.
            if (string.IsNullOrWhiteSpace(model.AdminRemarks))
            {
                ModelState.AddModelError(nameof(model.AdminRemarks), "Remarks are required when returning a submission for correction.");
                return View(submission);
            }
            nextAssignmentStatus = TaskWorkflow.ReturnedForCorrection;
            submissionDecision = "returned";
            break;
        default:
            ModelState.AddModelError(nameof(model.Decision), "Choose Approve or Return.");
            return View(submission);
    }

    // Belt-and-suspenders: this should always be true given the switch above,
    // but routes every status change through the same guard as the rest of
    // the app so ForReview's allowed edges live in exactly one place.
    if (!TaskWorkflow.CanTransition(currentAssignmentStatus, nextAssignmentStatus))
    {
        TempData["ErrorMessage"] = "That decision isn't allowed from the task's current state.";
        return RedirectToAction(nameof(Index));
    }

    var validatorId = HttpContext.Session.GetInt32("UserId");

    submission.Decision = submissionDecision;
    submission.AdminRemarks = model.AdminRemarks;
    submission.DecidedAt = DateTime.UtcNow;
    submission.ValidatedByUserId = validatorId;

    assignment.Status = nextAssignmentStatus;
    if (nextAssignmentStatus == TaskWorkflow.Completed)
    {
        assignment.Progress = 100; // Completed implies fully progressed, for display consistency
    }

    // Requirement 8: recompute the task-level aggregate — this is what
    // actually decides whether the overall task moves to ForReview/Completed
    // now that every (or not every) assignee has weighed in.
    _taskAssignments.RecalculateOverallStatus(task);

    if (validatorId.HasValue)
    {
        TaskActivityLogger.Log(_context, task.Id, validatorId.Value, TaskActivityType.Validated,
            submissionDecision == "approved"
                ? "Submission approved — marked completed for this assignee."
                : $"Submission returned for correction: \"{model.AdminRemarks}\"",
            submission.Id);
    }

    await _context.SaveChangesAsync();

    TempData["SuccessMessage"] = nextAssignmentStatus == TaskWorkflow.Completed
        ? "Submission approved."
        : "Task returned for correction.";

    return RedirectToAction(nameof(Index));
}
// GET: /Tasks/SuggestPriority?dueDate=2026-09-10
// Called by JS whenever the due-date field changes on Create/Edit, so the
// suggestion always reflects the currently-typed date, not just the value
// that existed when the page first loaded.
[HttpGet]
public IActionResult SuggestPriority(DateTime dueDate)
{
    if (!IsElevated())
    {
        return StatusCode(403);
    }

    var suggestion = PrioritySuggestionService.Suggest(dueDate);
    return Json(new { priority = suggestion.Priority, reason = suggestion.Reason });
}
    }
}