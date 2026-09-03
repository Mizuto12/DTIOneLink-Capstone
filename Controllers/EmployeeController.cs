using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Services;
using DTIOneLink.Filters;
using DTIOneLink.Models;   // <-- add this if it's missing


namespace DTIOneLink.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly AppDbContext _context;

        public EmployeeController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Employee or /Employee/Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            var tasks = await _context.TaskItems
                .Include(t => t.Assignee)
                .Where(t => t.AssigneeId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }

        // GET: /Employee/TaskManagement
        [HttpGet]
        public async Task<IActionResult> TaskManagement()
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            var tasks = await _context.TaskItems
                .Include(t => t.Assignee)
                .Where(t => t.AssigneeId == userId)
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
    
   var task = await GetAccessibleTaskAsync(model.Id);
    if (task == null)
    {
        return NotFound();
    }

    var currentStatus = (task.Status ?? "pending").Trim().ToLowerInvariant();

if (currentStatus == "completed")
{
    TempData["ErrorMessage"] = "This task is already completed and can no longer be updated.";
    return RedirectToAction(nameof(TaskManagement));
}

// Locked while awaiting Admin review — progress/status are frozen until
// a decision comes back (Completed or Returned for Correction).
if (currentStatus == "for-review")
{
    TempData["ErrorMessage"] = "This task is awaiting review and can't be updated right now.";
    return RedirectToAction(nameof(TaskManagement));
}

if (!ModelState.IsValid)
{
    return View(task);
}

var requestedStatus = (model.RequestedStatus ?? currentStatus).Trim().ToLowerInvariant();
var nextStatus = currentStatus;

// The only manual transition allowed from here: Pending -> In Progress.
if (currentStatus == TaskWorkflow.Pending && requestedStatus == TaskWorkflow.InProgress)
{
    nextStatus = TaskWorkflow.InProgress;
}

// Moving progress off zero implies work has started, even if the
// checkbox wasn't also ticked.
if (model.Progress > 0 && nextStatus == TaskWorkflow.Pending)
{
    nextStatus = TaskWorkflow.InProgress;
}

// Defense in depth: reject a tampered RequestedStatus that doesn't match
// a legal transition, even though the UI never offers one. Reaching
// Completed happens only via TasksController.Review, never from here.
if (!TaskWorkflow.CanTransition(currentStatus, nextStatus) || nextStatus == TaskWorkflow.Completed)
{
    TempData["ErrorMessage"] = "That status change isn't allowed.";
    return RedirectToAction(nameof(TaskManagement));
}

task.Progress = model.Progress;
task.Status = nextStatus;
    // AssigneeId, DueDate, Priority, TaskName, Description, CreatedAt:
    // untouched, because nothing above ever assigns to them.

    await _context.SaveChangesAsync();

    TempData["SuccessMessage"] = "Progress updated successfully!";
    return RedirectToAction(nameof(TaskManagement));
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

var status = (task.Status ?? "pending").Trim().ToLowerInvariant();
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

var status = (task.Status ?? "pending").Trim().ToLowerInvariant();

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
    // rather than overwriting the same fields on every resubmit.
    var submission = new TaskSubmission
    {
        TaskId = task.Id,
        ProofFileName = Path.GetFileName(model.ProofFile.FileName),
        ProofStoredFileName = storedFileName,
        Remarks = model.Remarks,
        SubmittedAt = DateTime.UtcNow
    };

    _context.TaskSubmissions.Add(submission);
    task.Status = TaskWorkflow.ForReview;
    await _context.SaveChangesAsync();

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
// _context.TaskItems directly. Admin/Supervisor bypass the AssigneeId
// check entirely; everyone else only ever sees their own tasks.
private async Task<TaskItem?> GetAccessibleTaskAsync(int taskId)
{
    var userId = HttpContext.Session.GetInt32("UserId");
    if (userId == null)
    {
        return null; // RequireLoginAttribute should already have caught this
    }

    var role = HttpContext.Session.GetString("UserRole");
    var isElevated = role == "Admin" || role == "Supervisor";

    var query = _context.TaskItems
        .Include(t => t.Assignee)
        .Include(t => t.Submissions).ThenInclude(s => s.ValidatedBy)
        .AsQueryable();

    return isElevated
        ? await query.FirstOrDefaultAsync(t => t.Id == taskId)
        : await query.FirstOrDefaultAsync(t => t.Id == taskId && t.AssigneeId == userId);
}
// Same pattern for submission history / proof downloads, since those
// are scoped through the parent Task's AssigneeId, not their own field.
private async Task<TaskSubmission?> GetAccessibleSubmissionAsync(int submissionId)
{
    var userId = HttpContext.Session.GetInt32("UserId");
    if (userId == null)
    {
        return null;
    }

    var role = HttpContext.Session.GetString("UserRole");
    var isElevated = role == "Admin" || role == "Supervisor";

    var query = _context.TaskSubmissions.Include(s => s.Task).AsQueryable();

    return isElevated
        ? await query.FirstOrDefaultAsync(s => s.Id == submissionId)
        : await query.FirstOrDefaultAsync(s => s.Id == submissionId && s.Task!.AssigneeId == userId);
}
    }
}