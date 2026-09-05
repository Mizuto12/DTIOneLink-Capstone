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

        public TasksController(AppDbContext context, NotificationService notifications)
        {
            _context = context;
             _notifications = notifications;
        }

        // GET: /Tasks/Index
        public async Task<IActionResult> Index()
        {
            var tasks = await _context.TaskItems
                .Include(t => t.Assignee)
                .Include(t => t.Submissions)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }

        // GET: /Tasks/Create
        public async Task<IActionResult> Create()
        {
            var employees = await _context.Users
                .Where(u => u.IsActive && u.Role == "Employee")
                .OrderBy(u => u.FullName)
                .ToListAsync();
            ViewBag.Employees = new SelectList(employees, "Id", "FullName");
            return View();
        }

        // POST: /Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TaskItem task)
        {
            if (!ModelState.IsValid)
            {
                var employees = await _context.Users
                    .Where(u => u.IsActive)
                    .OrderBy(u => u.FullName)
                    .ToListAsync();

                ViewBag.Employees = new SelectList(employees, "Id", "FullName");
                return View(task);
            }

            _context.TaskItems.Add(task);
            await _context.SaveChangesAsync();

            // new — notify the assignee once the task has an Id to link to
            if (task.AssigneeId != 0)
            {
                await _notifications.NotifyTaskAssignedAsync(task.AssigneeId, task.Id, task.TaskName);
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

     var task = await _context.TaskItems.FirstOrDefaultAsync(t => t.Id == id);
     if (task == null)
     {
         return NotFound();
     }

     var model = new TaskEditViewModel
     {
         Id = task.Id,
         TaskName = task.TaskName,
         AssigneeId = task.AssigneeId,
         DueDate = task.DueDate,
         Priority = task.Priority,
         Description = task.Description
     };

    // Read-only display context for the "Task Overview" panel — not part of
    // TaskEditViewModel on purpose (Status/Progress/CreatedAt aren't editable
    // here; they're owned by the employee-facing Update/SubmitProof flow).
    ViewBag.TaskCode = $"TASK-{task.Id:D4}";
    ViewBag.CurrentStatus = task.Status;
    ViewBag.CurrentProgress = task.Progress;
    ViewBag.CreatedAt = task.CreatedAt;

    // Initial suggestion for page load, before any JS runs. Recalculated
    // live via SuggestPriority whenever the due-date field changes.
    var suggestion = PrioritySuggestionService.Suggest(task.DueDate);
    ViewBag.SuggestedPriority = suggestion.Priority;
    ViewBag.SuggestedReason = suggestion.Reason;

    await PopulateEmployeesAsync();
    return View(model);
 }
// POST: /Tasks/Edit
// POST: /Tasks/Edit
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Edit(TaskEditViewModel model)
{
    if (!IsElevated())
    {
       return StatusCode(403);
    }

    var task = await _context.TaskItems.FirstOrDefaultAsync(t => t.Id == model.Id);
    if (task == null)
    {
        return NotFound();
    }

    // Assignee must be an active Employee, same rule as Create's dropdown.
    var assigneeExists = await _context.Users
        .AnyAsync(u => u.Id == model.AssigneeId && u.IsActive && u.Role == "Employee");
    if (!assigneeExists)
    {
        ModelState.AddModelError(nameof(model.AssigneeId), "Select a valid assignee.");
    }

    if (!ModelState.IsValid)
    {
        await PopulateEmployeesAsync();
        return View(model);
    }

    // new — snapshot the pre-edit values before they get overwritten below,
    // so we know what actually changed once the save is done.
    var oldAssigneeId = task.AssigneeId;
    var oldDueDate     = task.DueDate;
    var oldPriority    = task.Priority;

    // Only the editable fields — Progress, Status, CreatedAt, Submissions
    // are untouched, same discipline as Employee.Update's comment block.
    task.TaskName = model.TaskName;
    task.AssigneeId = model.AssigneeId;
    task.DueDate = model.DueDate;
    task.Priority = model.Priority;
    task.Description = model.Description;

    await _context.SaveChangesAsync();

    // new — notify only the affected employee, and only for what actually changed.
    // Reassignment takes priority: if the assignee changed, the new assignee
    // gets one "reassigned to you" notice, not a stack of separate ones.
    bool wasReassigned = oldAssigneeId != task.AssigneeId;

    if (wasReassigned)
    {
        await _notifications.NotifyTaskReassignedAsync(task.AssigneeId, task.Id, task.TaskName);
    }
    else
    {
        if (oldDueDate != task.DueDate)
        {
            await _notifications.NotifyTaskDueDateChangedAsync(task.AssigneeId, task.Id, task.TaskName, task.DueDate);
        }
        if (oldPriority != task.Priority)
        {
            await _notifications.NotifyTaskPriorityChangedAsync(task.AssigneeId, task.Id, task.TaskName, task.Priority);
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
        .FirstOrDefaultAsync(s => s.Id == id);

    if (submission == null || submission.Task == null)
    {
        return NotFound();
    }

    // Only the currently-pending submission on a ForReview task can be
    // decided — an old, already-superseded submission has nothing to act on.
    if (submission.Task.Status?.ToLowerInvariant() != TaskWorkflow.ForReview || submission.Decision != null)
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
        .Include(s => s.Task)
        .FirstOrDefaultAsync(s => s.Id == model.SubmissionId);

    if (submission == null || submission.Task == null)
    {
        return NotFound();
    }

    var task = submission.Task;
    var currentStatus = task.Status ?? TaskWorkflow.Pending;

    if (currentStatus.ToLowerInvariant() != TaskWorkflow.ForReview || submission.Decision != null)
    {
        TempData["ErrorMessage"] = "This submission is no longer awaiting review.";
        return RedirectToAction(nameof(Index));
    }

    var decision = model.Decision.Trim().ToLowerInvariant();
    string nextStatus;
    string submissionDecision;

    switch (decision)
    {
        case "approve":
            nextStatus = TaskWorkflow.Completed;
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
            nextStatus = TaskWorkflow.ReturnedForCorrection;
            submissionDecision = "returned";
            break;
        default:
            ModelState.AddModelError(nameof(model.Decision), "Choose Approve or Return.");
            return View(submission);
    }

    // Belt-and-suspenders: this should always be true given the switch above,
    // but routes every status change through the same guard as the rest of
    // the app so ForReview's allowed edges live in exactly one place.
    if (!TaskWorkflow.CanTransition(currentStatus, nextStatus))
    {
        TempData["ErrorMessage"] = "That decision isn't allowed from the task's current state.";
        return RedirectToAction(nameof(Index));
    }

    var validatorId = HttpContext.Session.GetInt32("UserId");

    submission.Decision = submissionDecision;
    submission.AdminRemarks = model.AdminRemarks;
    submission.DecidedAt = DateTime.UtcNow;
    submission.ValidatedByUserId = validatorId;

    task.Status = nextStatus;
    if (nextStatus == TaskWorkflow.Completed)
    {
        task.Progress = 100; // Completed implies fully progressed, for display consistency
    }

    if (validatorId.HasValue)
    {
        TaskActivityLogger.Log(_context, task.Id, validatorId.Value, TaskActivityType.Validated,
            submissionDecision == "approved"
                ? "Submission approved — task marked completed."
                : $"Submission returned for correction: \"{model.AdminRemarks}\"",
            submission.Id);
    }

    await _context.SaveChangesAsync();

    TempData["SuccessMessage"] = nextStatus == TaskWorkflow.Completed
        ? "Task marked as completed."
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