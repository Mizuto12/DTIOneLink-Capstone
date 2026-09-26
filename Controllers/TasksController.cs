using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Models;
using DTIOneLink.Data;
using DTIOneLink.Services;
using DTIOneLink.Security;

namespace DTIOneLink.Controllers
{
    public class TasksController : Controller
    {
        private readonly AppDbContext _context;
        private readonly NotificationService _notifications;
        private readonly TaskAssignmentService _taskAssignments;
        private readonly OpdTaskService _opdTasks;

        public TasksController(AppDbContext context, NotificationService notifications,
            TaskAssignmentService taskAssignments, OpdTaskService opdTasks)
        {
            _context = context;
            _notifications = notifications;
            _taskAssignments = taskAssignments;
            _opdTasks = opdTasks;
        }

        // Allowed filter values; anything else falls back to the default, so
        // a tampered query string can never reach the database as-is.
        private static readonly string[] IndexStatuses =
            { "all", "pending", "in-progress", "for-review", "returned-for-correction", "completed", "overdue" };
        private static readonly string[] IndexPriorities = { "all", "high", "medium", "low" };
        // "Overdue" is deliberately not a due-date choice — it's already a
        // Status (and a workflow tile).
        private static readonly string[] IndexDueRanges = { "all", "today", "next7", "this-month" };
        private static readonly string[] IndexSorts = { "newest", "due-asc", "priority-desc" };

        private static string Pick(string? value, string[] allowed) =>
            allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? allowed[0];

        // GET: /Tasks/Index?q=&status=&priority=&department=&employeeId=&due=&sort=&page=
        // Workflow monitoring: every filter is applied in the database query,
        // on top of the same department scope as before.
        public async Task<IActionResult> Index(
            string? q, string? status, string? priority, string? department,
            int? employeeId, string? due, string? sort, int page = 1)
        {
            if (!CanAccessTaskManagement())
            {
                return StatusCode(403);
            }

            var isOfficeWide = IsOfficeWideTaskManager();
            var ownDepartment = CurrentUserDepartment();
            var model = new TaskIndexViewModel
            {
                Search = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
                Status = Pick(status, IndexStatuses),
                Priority = Pick(priority, IndexPriorities),
                Due = Pick(due, IndexDueRanges),
                Sort = Pick(sort, IndexSorts),
                IsOfficeWide = isOfficeWide
            };
            if (model.Search?.Length > 100)
            {
                model.Search = model.Search[..100];
            }

            IQueryable<TaskItem> scoped = _context.TaskItems;

            // Office-wide (SuperAdmin, via ManageOfficeWideTasks) sees every
            // task regardless of department. Everyone else who can reach
            // this action (Admin/Supervisor) only sees tasks with at least
            // one assignee in their own department.
            if (!isOfficeWide)
            {
                // Effective-department priority: this task's own
                // OwningDepartment first; if null, inherit the parent Main
                // Task's OwningDepartment (covers a just-created OPD subtask
                // with zero assignees); only if BOTH are null (a legacy task
                // from before OwningDepartment existed) fall back to any
                // assignee's own Department. The legacy fallback never
                // overrides an explicit OwningDepartment.
                scoped = scoped.Where(t =>
                    (t.OwningDepartment != null && t.OwningDepartment == ownDepartment) ||
                    (t.OwningDepartment == null && t.ParentTask != null && t.ParentTask.OwningDepartment == ownDepartment) ||
                    (t.OwningDepartment == null && (t.ParentTask == null || t.ParentTask.OwningDepartment == null) &&
                        t.Assignments.Any(a => a.User != null && a.User.Department == ownDepartment)));
            }

            // ── Dropdown options (limited to what this user may see) ──
            var departmentsQuery = _context.Users
                .Where(u => u.IsActive && u.Department != null && u.Department != "");
            if (!isOfficeWide)
            {
                departmentsQuery = departmentsQuery.Where(u => u.Department == ownDepartment);
            }
            model.Departments = await departmentsQuery
                .Select(u => u.Department)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();

            // Division filter: SuperAdmin only, and only a real division.
            if (isOfficeWide && !string.IsNullOrWhiteSpace(department))
            {
                model.Department = model.Departments
                    .FirstOrDefault(d => string.Equals(d, department.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            // SuperAdmin also sees Admins here, since Admins carry their own
            // tasks (Direct Admin, Whole Office, and directives they lead).
            var employeesQuery = isOfficeWide
                ? _context.Users.Where(u => u.IsActive && (u.Role == "Employee" || u.Role == "Admin"))
                : _context.Users.Where(u => u.IsActive && u.Role == "Employee");
            if (!isOfficeWide)
            {
                employeesQuery = employeesQuery.Where(u => u.Department == ownDepartment);
            }
            else if (model.Department != null)
            {
                var selectedDivision = model.Department;
                employeesQuery = employeesQuery.Where(u => u.Department == selectedDivision);
            }
            model.Employees = await employeesQuery
                .OrderBy(u => u.FullName)
                .Select(u => new TaskIndexViewModel.EmployeeOption(u.Id, u.FullName, u.Department, u.Role == "Admin"))
                .ToListAsync();

            // Employee filter: only someone in the list above.
            if (employeeId.HasValue && model.Employees.Any(e => e.Id == employeeId.Value))
            {
                model.EmployeeId = employeeId.Value;
            }

            // ── Filters other than Status ──────────────────────────────
            var filtered = scoped;

            if (model.Search != null)
            {
                var term = model.Search;
                filtered = filtered.Where(t =>
                    t.TaskName.Contains(term) ||
                    t.Description.Contains(term) ||
                    (t.OwningDepartment != null && t.OwningDepartment.Contains(term)) ||
                    t.Assignments.Any(a => a.User != null && a.User.FullName.Contains(term)));
            }

            if (model.Priority != "all")
            {
                var wanted = model.Priority;
                filtered = filtered.Where(t => t.Priority == wanted);
            }

            if (model.Department != null)
            {
                // Same effective-department rule as the scope above.
                var division = model.Department;
                filtered = filtered.Where(t =>
                    (t.OwningDepartment != null && t.OwningDepartment == division) ||
                    (t.OwningDepartment == null && t.ParentTask != null && t.ParentTask.OwningDepartment == division) ||
                    (t.OwningDepartment == null && (t.ParentTask == null || t.ParentTask.OwningDepartment == null) &&
                        t.Assignments.Any(a => a.User != null && a.User.Department == division)));
            }

            if (model.EmployeeId.HasValue)
            {
                var empId = model.EmployeeId.Value;
                // Responsible Admin covers Department Directives an Admin leads
                // but isn't assigned to; never matches an Employee.
                filtered = filtered.Where(t =>
                    t.Assignments.Any(a => a.UserId == empId) ||
                    (t.ResponsibleAdminUserId.HasValue && t.ResponsibleAdminUserId.Value == empId));
            }

            // Same "today" as TaskWorkflow.IsOverdue, so Overdue here always
            // matches the Overdue badge shown in the table.
            var today = DateTime.UtcNow.Date;
            switch (model.Due)
            {
                case "today":
                    var tomorrow = today.AddDays(1);
                    filtered = filtered.Where(t => t.DueDate >= today && t.DueDate < tomorrow);
                    break;
                case "next7":
                    var in7 = today.AddDays(8);
                    filtered = filtered.Where(t => t.DueDate >= today && t.DueDate < in7);
                    break;
                case "this-month":
                    var monthStart = new DateTime(today.Year, today.Month, 1);
                    var nextMonth = monthStart.AddMonths(1);
                    filtered = filtered.Where(t => t.DueDate >= monthStart && t.DueDate < nextMonth);
                    break;
            }

            // ── Workflow indicators (every filter except Status) ───────
            var statusRows = await filtered
                .Select(t => new { t.Status, t.DueDate })
                .ToListAsync();
            model.StatusCounts = IndexStatuses.ToDictionary(s => s, _ => 0);
            foreach (var row in statusRows)
            {
                var display = TaskWorkflow.DisplayStatus(row.Status, row.DueDate);
                if (display != "all" && model.StatusCounts.ContainsKey(display))
                {
                    model.StatusCounts[display]++;
                }
            }
            model.StatusCounts["all"] = statusRows.Count;

            // ── Status filter (matches the badge shown in the table:
            //    Overdue overlays any non-completed status) ─────────────
            switch (model.Status)
            {
                case "overdue":
                    filtered = filtered.Where(t => t.Status != TaskWorkflow.Completed && t.DueDate < today);
                    break;
                case "completed":
                    filtered = filtered.Where(t => t.Status == TaskWorkflow.Completed);
                    break;
                case "all":
                    break;
                default:
                    var wantedStatus = model.Status;
                    filtered = filtered.Where(t => t.Status == wantedStatus && t.DueDate >= today);
                    break;
            }

            // ── Sort (Id as tie-breaker keeps paging stable) ────────────
            IOrderedQueryable<TaskItem> ordered = model.Sort switch
            {
                "due-asc" => filtered.OrderBy(t => t.DueDate),
                "priority-desc" => filtered.OrderByDescending(t =>
                    t.Priority == "high" ? 3 : t.Priority == "medium" ? 2 : 1),
                _ => filtered.OrderByDescending(t => t.CreatedAt)
            };
            ordered = ordered.ThenByDescending(t => t.Id);

            // ── Paging ──────────────────────────────────────────────────
            model.TotalCount = await filtered.CountAsync();
            model.Page = Math.Clamp(page, 1, model.TotalPages);

            model.Tasks = await ordered
                .Skip((model.Page - 1) * TaskIndexViewModel.PageSize)
                .Take(TaskIndexViewModel.PageSize)
                .Include(t => t.Assignee)
                .Include(t => t.Assignments).ThenInclude(a => a.User)
                .Include(t => t.Submissions)
                // Assignments and Submissions are independent collections.
                // Split them into separate SQL queries to avoid the Cartesian
                // product produced by a single JOIN-heavy query.
                .AsSplitQuery()
                .AsNoTracking() // read-only list
                .ToListAsync();

            return View(model);
        }

        // GET: /Tasks/ReceivedTasks
        // Read-only list of Main Tasks (OPD directives) formally assigned to
        // the logged-in Admin — either because they're the ResponsibleAdmin,
        // or because the task targets their own department. Admin/Supervisor
        // only; never shows a task from another department.
        public async Task<IActionResult> ReceivedTasks()
        {
            if (!IsDepartmentTaskManager())
            {
                return StatusCode(403);
            }

            var currentUserId = HttpContext.Session.GetInt32("UserId");
            var department = CurrentUserDepartment();

            var mainTasks = await _context.TaskItems
                .Include(t => t.Subtasks).ThenInclude(s => s.Assignments).ThenInclude(a => a.User)
                // Nested collections (Subtasks -> Assignments): split to avoid
                // the JOIN row explosion; read-only, so no tracking.
                .AsSplitQuery()
                .AsNoTracking()
                .Where(t => t.TaskLevel == TaskLevels.Main &&
                    ((t.ResponsibleAdminUserId.HasValue && currentUserId.HasValue && t.ResponsibleAdminUserId.Value == currentUserId.Value)
                     || t.OwningDepartment == department
                     // A Whole Office task is also "received" by each Admin it was given to.
                     || (currentUserId.HasValue && t.Assignments.Any(a => a.UserId == currentUserId.Value))))
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var model = mainTasks.Select(t => new TaskReceivedListItemViewModel
            {
                Id = t.Id,
                TaskName = t.TaskName,
                Description = t.Description,
                DueDate = t.DueDate,
                Priority = t.Priority,
                Department = t.OwningDepartment ?? string.Empty,
                Status = t.Status,
                AssigneeNames = t.Subtasks
                    .SelectMany(s => s.Assignments)
                    .Where(a => a.User != null)
                    .Select(a => a.User!.FullName)
                    .Distinct()
                    .ToList()
            }).ToList();

            return View(model);
        }

        // GET: /Tasks/Create
        // Only the OPD (SuperAdmin) creates tasks. Admins/Supervisors don't —
        // they receive OPD tasks and assign their employees through Edit.
        public async Task<IActionResult> Create()
        {
            if (!IsOfficeWideTaskManager())
            {
                return StatusCode(403);
            }

            // Always produces a Main Task (Direct Admin Task or Department
            // Directive) — no employee assignees are picked here.
            await PopulateResponsibleAdminsAsync();
            ViewBag.TargetDepartments = TargetDepartments.All;
            ViewBag.CanCreateMainTask = true;
            return View(new TaskCreateViewModel());
        }

        // POST: /Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TaskCreateViewModel model)
        {
            // Same rule as the GET: only the OPD (SuperAdmin) creates tasks.
            if (!IsOfficeWideTaskManager())
            {
                return StatusCode(403);
            }

            var createdByUserId = HttpContext.Session.GetInt32("UserId");

            // Always a Main Task (optionally with inline subtasks for a
            // Department Directive).
            var recurrence = string.IsNullOrWhiteSpace(model.Recurrence) ? null : model.Recurrence.Trim();
            if (recurrence != null && !TaskRecurrence.IsValid(recurrence))
            {
                ModelState.AddModelError(nameof(model.Recurrence), "Choose how often this task repeats.");
            }

            if (!TaskTypes.IsValid(model.TaskType))
            {
                ModelState.AddModelError(nameof(model.TaskType), "Select a task type.");
            }

            var isWholeOffice = model.TaskType == TaskTypes.WholeOffice;
            if (isWholeOffice)
            {
                // Goes to everyone, so no division or Responsible Admin —
                // whatever the form sent for those is ignored.
                model.OwningDepartment = WholeOfficeDepartment;
                model.ResponsibleAdminUserId = null;
                ModelState.Remove(nameof(model.OwningDepartment));
                ModelState.Remove(nameof(model.ResponsibleAdminUserId));
            }
            else if (!TargetDepartments.IsValid(model.OwningDepartment))
            {
                ModelState.AddModelError(nameof(model.OwningDepartment), "Select a target department.");
            }
            else
            {
                await ValidateResponsibleAdminAsync(model.ResponsibleAdminUserId, model.OwningDepartment!, nameof(model.ResponsibleAdminUserId));
            }

            if (!ModelState.IsValid)
            {
                await PopulateResponsibleAdminsAsync();
                ViewBag.TargetDepartments = TargetDepartments.All;
                ViewBag.CanCreateMainTask = true;
                return View(model);
            }

            var isDirect = model.TaskType == TaskTypes.DirectAdmin;
            var (created, subtaskCount) = await _opdTasks.CreateAsync(
                new OpdTaskService.NewOpdTask(
                    model.TaskName,
                    model.Description,
                    model.DueDate,
                    model.Priority,
                    model.TaskType!,
                    model.OwningDepartment!,
                    model.ResponsibleAdminUserId,
                    // Only a Department Directive has subtasks, whatever the client sent.
                    isDirect || isWholeOffice ? new List<string>() : (model.SubtaskNames ?? new List<string>()),
                    recurrence),
                createdByUserId,
                isWholeOffice ? "OPD gave this task to the whole office."
                    : isDirect ? "OPD created this Direct Admin Task." : "OPD created this Department Directive.");

            var repeatNote = recurrence == null ? "" : $" It repeats {TaskRecurrence.Label(recurrence).ToLowerInvariant()}.";
            TempData["SuccessMessage"] = isWholeOffice
                ? $"Task given to the whole office ({created.Assignments.Count} people)." + repeatNote
                : isDirect
                ? "Direct Admin task created and assigned." + repeatNote
                : (subtaskCount > 0
                    ? $"Main task created with {subtaskCount} subtask(s)."
                    : "Main task created successfully!") + repeatNote;
            return RedirectToAction(nameof(Index));
        }

        // POST: /Tasks/StopRepeating/5 — OPD only. Stops a repeating task from
        // sending out further copies. Copies already sent are untouched.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopRepeating(int id)
        {
            if (!IsOfficeWideTaskManager())
            {
                return StatusCode(403);
            }

            var task = await _context.TaskItems.FirstOrDefaultAsync(t => t.Id == id && t.TaskLevel == TaskLevels.Main);
            if (task == null)
            {
                return NotFound();
            }

            if (task.Recurrence != null)
            {
                task.Recurrence = null;
                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId.HasValue)
                {
                    TaskActivityLogger.Log(_context, task.Id, userId.Value, "updated", "Stopped repeating this task.");
                }
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "This task will no longer repeat.";
            }

            return RedirectToAction(nameof(MainTaskDetails), new { id });
        }
        // GET: /Tasks/Edit/5
 [HttpGet]
 public async Task<IActionResult> Edit(int id)
 {
     if (!CanAccessTaskManagement())
     {
        return StatusCode(403);
     }

     var task = await _context.TaskItems
         .Include(t => t.Assignments).ThenInclude(a => a.User)
         .Include(t => t.ParentTask)
         .Include(t => t.Subtasks)
         .AsSplitQuery() // Assignments + Subtasks are independent collections
         .FirstOrDefaultAsync(t => t.Id == id);
     if (task == null)
     {
         return NotFound();
     }

     var isDirectMainTaskAssignment = false;

     if (task.TaskLevel == TaskLevels.Main)
     {
         // This form doesn't carry Main Task fields (OwningDepartment,
         // ResponsibleAdminUserId, etc.) — not a scope decision, just
         // routing to the view that does. Scope is still checked first so
         // this never confirms an out-of-scope Main Task id exists.
         if (!IsWithinMainTaskScope(task))
         {
             return NotFound();
         }

         // SuperAdmin keeps the existing MainTaskDetails-only path. For
         // Admin/Supervisor, direct assignment to the Main Task itself is
         // only allowed while it has zero subtasks — the two assignment
         // models (direct vs. via subtasks) are mutually exclusive, so once
         // any subtask exists, assignment happens there instead and this
         // Main Task stays read-only here.
         if (IsOfficeWideTaskManager() || task.Subtasks.Any())
         {
             return RedirectToAction(nameof(MainTaskDetails), new { id = task.Id });
         }

         isDirectMainTaskAssignment = true;
     }
     else if (!IsWithinTaskScope(task))
     {
         // Department-scoped Admin/Supervisor hitting a task outside their
         // department by id — treat exactly like it doesn't exist, same as
         // any other out-of-scope lookup, rather than leaking a 403 that
         // confirms the task's existence.
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

    // True when the current user is a department-scoped Admin/Supervisor
    // editing either an OPD-issued subtask (created under a Main Task) or
    // a Main Task directly (isDirectMainTaskAssignment) — not SuperAdmin,
    // who retains full edit rights on anything. Every field except
    // AssigneeIds is locked in the view when this is true, and
    // re-enforced server-side in the POST action below.
    ViewBag.IsAssignmentOnly = (task.ParentTaskId.HasValue || isDirectMainTaskAssignment) && !IsOfficeWideTaskManager();

    // Read-only display context for the "Task Overview" panel — not part of
    // TaskEditViewModel on purpose (Status/Progress/CreatedAt aren't editable
    // here; they're owned by the employee-facing Update/SubmitProof flow and
    // the Admin Review decision).
    ViewBag.TaskCode = $"TASK-{task.Id:D4}";
    ViewBag.CurrentStatus = task.Status;      // aggregate across all assignees
    ViewBag.CurrentProgress = task.Progress;  // aggregate across all assignees
    ViewBag.CreatedAt = task.CreatedAt;
    ViewBag.AssignmentSummaries = await BuildAssignmentSummariesAsync(task);

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
    if (!CanAccessTaskManagement())
    {
       return StatusCode(403);
    }

    var task = await _context.TaskItems
        .Include(t => t.Assignments).ThenInclude(a => a.User)
        .Include(t => t.ParentTask)
        .Include(t => t.Subtasks)
        .AsSplitQuery() // Assignments + Subtasks are independent collections
        .FirstOrDefaultAsync(t => t.Id == model.Id);
    if (task == null)
    {
        return NotFound();
    }

    var isDirectMainTaskAssignment = false;

    if (task.TaskLevel == TaskLevels.Main)
    {
        if (!IsWithinMainTaskScope(task))
        {
            return NotFound();
        }

        // Same reasoning as the GET action. SuperAdmin never posts through
        // this form for a Main Task. Admin/Supervisor may only post an
        // assignee change here while the Main Task still has zero
        // subtasks — re-checked here, not trusted from the client, so a
        // tampered POST can't sneak assignments onto a Main Task that
        // gained subtasks after the page was loaded.
        if (IsOfficeWideTaskManager() || task.Subtasks.Any())
        {
            return Forbid();
        }

        isDirectMainTaskAssignment = true;
    }
    else if (!IsWithinTaskScope(task))
    {
        return NotFound();
    }

    // Current assignees stay exactly as they are (progress included) unless
    // explicitly removed; only the newly added people are validated.
    var currentAssigneeIds = task.Assignments.Select(a => a.UserId).ToList();
    var removeIds = (model.RemoveAssigneeIds ?? new()).Where(currentAssigneeIds.Contains).Distinct().ToList();
    var addIds = (model.AddAssigneeIds ?? new()).Where(id => !currentAssigneeIds.Contains(id)).Distinct().ToList();
    var desiredAssigneeIds = currentAssigneeIds.Except(removeIds).Concat(addIds).ToList();

    if (addIds.Count > 0)
    {
        await ValidateAssigneeIdsAsync(addIds, nameof(model.AssigneeIds));
    }
    if (desiredAssigneeIds.Count == 0)
    {
        ModelState.AddModelError(nameof(model.AssigneeIds), "At least one person must stay assigned to this task.");
    }

    if (!ModelState.IsValid)
    {
        model.AssigneeIds = currentAssigneeIds;
        await RepopulateEditContextAsync(task);
        return View(model);
    }

    // new — snapshot the pre-edit values before they get overwritten below,
    // so we know what actually changed once the save is done.
    var oldDueDate = task.DueDate;
    var oldPriority = task.Priority;
    var oldAssigneeUserIds = task.Assignments.Select(a => a.UserId).ToList();

    // Same rule as the GET action. Re-checked here rather than trusted from
    // a hidden form field, so a tampered POST can't re-enable these fields —
    // if this is an OPD-issued subtask, or a Main Task being assigned to
    // directly, and the caller isn't office-wide, TaskName/DueDate/Priority/
    // Description are simply never written, regardless of what the client
    // submitted.
    var isAssignmentOnly = (task.ParentTaskId.HasValue || isDirectMainTaskAssignment) && !IsOfficeWideTaskManager();

    // Only the editable fields — Progress, Status, CreatedAt, Submissions
    // are untouched, same discipline as Employee.Update's comment block.
    if (!isAssignmentOnly)
    {
        task.TaskName = model.TaskName;
        task.DueDate = model.DueDate;
        task.Priority = model.Priority;
        task.Description = model.Description;
    }

    var changedByUserId = HttpContext.Session.GetInt32("UserId");
    var sync = await _taskAssignments.SyncAssignmentsAsync(task, desiredAssigneeIds, changedByUserId);

    if (sync.BlockedRemovals.Count > 0)
    {
        ModelState.AddModelError(nameof(model.AssigneeIds),
            "Can't unassign someone who has already submitted proof for this task.");
        model.AssigneeIds = currentAssigneeIds;
        await RepopulateEditContextAsync(task);
        return View(model);
    }

    _taskAssignments.RecalculateOverallStatus(task);

    // If this subtask belongs to an OPD Main Task, roll the change up so
    // the Main Task's own Status/Progress reflects it too.
    await _taskAssignments.PropagateToParentMainTaskAsync(task);

    // Accountability trail for who distributed this task — one row per
    // affected employee per action, added to the tracker here so it rides
    // along in the same SaveChangesAsync as the assignment change itself
    // (never a separate save, so the two can't get out of sync). Old rows
    // are never touched — TaskActivity is append-only by design.
    if ((sync.Added.Count > 0 || sync.Removed.Count > 0) && changedByUserId.HasValue)
    {
        var affectedUserIds = sync.Added.Concat(sync.Removed).Distinct().ToList();
        var affectedUserNames = await _context.Users
            .Where(u => affectedUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        foreach (var addedUserId in sync.Added)
        {
            var name = affectedUserNames.TryGetValue(addedUserId, out var n) ? n : $"User #{addedUserId}";
            TaskActivityLogger.Log(_context, task.Id, changedByUserId.Value, TaskActivityType.Assigned,
                $"Assigned {name} to this task.");
        }

        foreach (var removedUserId in sync.Removed)
        {
            var name = affectedUserNames.TryGetValue(removedUserId, out var n) ? n : $"User #{removedUserId}";
            TaskActivityLogger.Log(_context, task.Id, changedByUserId.Value, TaskActivityType.Removed,
                $"Removed {name} from this task.");
        }
    }

    await _context.SaveChangesAsync();

    // new — notify only the affected employees, and only for what actually
    // changed. Newly added assignees get one "assigned to you" notice;
    // everyone who was already on the task before AND after this edit gets
    // notified about due-date/priority changes (each is independent, so
    // both can fire).
    foreach (var newUserId in sync.Added)
    {
        // Someone was swapped out in the same edit = reassignment.
        // Otherwise it's a plain new assignment.
        if (sync.Removed.Count > 0)
        {
            await _notifications.NotifyTaskReassignedAsync(newUserId, task.Id, task.TaskName);
        }
        else
        {
            await _notifications.NotifyTaskAssignedAsync(newUserId, task.Id, task.TaskName);
        }
    }

    foreach (var removedUserId in sync.Removed)
    {
        await _notifications.NotifyTaskRemovedAsync(removedUserId, task.Id, task.TaskName);
    }

    if (oldDueDate != task.DueDate)
    {
        await _notifications.ResetDeadlineRemindersAsync(task.Id);
    }

    // Only OPD/SuperAdmin edits alert the department's Admins — an Admin
    // editing within their own department doesn't need to notify their peers.
    if (IsOfficeWideTaskManager())
    {
        if (oldDueDate != task.DueDate)
        {
            await _notifications.NotifyAdminsOpdDueDateChangedAsync(task.Id, task.TaskName, task.DueDate);
        }
        if (oldPriority != task.Priority)
        {
            await _notifications.NotifyAdminsOpdPriorityChangedAsync(task.Id, task.TaskName, task.Priority);
        }
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

// Admin/Supervisor: the original "elevated" role check this controller has
// always used, unchanged — still department-scoped by every method below.
// Deliberately NOT how SuperAdmin gets in (see IsOfficeWideTaskManager) —
// SuperAdmin is never added to this string list.
private bool IsDepartmentTaskManager()
{
    var role = HttpContext.Session.GetString("UserRole");
    return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase);
}

// SuperAdmin: gated by its own dedicated permission (ManageOfficeWideTasks),
// not by role-string comparison and not by ManageRecords/ViewConfidentialRecords.
// Grants access to every department's tasks, never just SuperAdmin's own.
private bool IsOfficeWideTaskManager()
{
    var role = HttpContext.Session.GetString("UserRole");
    return RolePermissions.Has(role, Permissions.ManageOfficeWideTasks);
}

// Combined gate for every action in this controller: department-scoped
// Admin/Supervisor OR office-wide SuperAdmin. Which of the two decides how
// far each action's data is then filtered/validated below.
private bool CanAccessTaskManagement() => IsDepartmentTaskManager() || IsOfficeWideTaskManager();

private string? CurrentUserDepartment() => HttpContext.Session.GetString("UserDepartment");

// Office-wide task managers can act on any task. Department-scoped
// managers can only act on a task that has at least one assignee in their
// own department. Call with task.Assignments (+ .User) already loaded.
private bool IsWithinTaskScope(TaskItem task)
{
    if (IsOfficeWideTaskManager())
    {
        return true;
    }

    var department = CurrentUserDepartment();

    // Effective-department priority: this task's own OwningDepartment
    // wins whenever it's set — it must NOT be overridden by an assignee's
    // department (e.g. a cross-department edge case). Requires
    // task.ParentTask to be loaded by the caller's query.
    if (!string.IsNullOrWhiteSpace(task.OwningDepartment))
    {
        return string.Equals(task.OwningDepartment, department, StringComparison.OrdinalIgnoreCase);
    }

    if (task.ParentTask != null && !string.IsNullOrWhiteSpace(task.ParentTask.OwningDepartment))
    {
        return string.Equals(task.ParentTask.OwningDepartment, department, StringComparison.OrdinalIgnoreCase);
    }

    // Legacy fallback: only reached when neither this task's own
    // OwningDepartment nor its parent's is populated.
    return task.Assignments.Any(a => a.User != null &&
        string.Equals(a.User.Department, department, StringComparison.OrdinalIgnoreCase));
}

// Main Tasks have no Assignments of their own, so IsWithinTaskScope above
// (which checks Assignments) can never admit a department-scoped Admin —
// this checks OwningDepartment/ResponsibleAdminUserId instead. Office-wide
// (SuperAdmin) can always see it, same as any other task.
private bool IsWithinMainTaskScope(TaskItem mainTask)
{
    if (IsOfficeWideTaskManager())
    {
        return true;
    }

    var currentUserId = HttpContext.Session.GetInt32("UserId");
    if (mainTask.ResponsibleAdminUserId.HasValue && currentUserId.HasValue &&
        mainTask.ResponsibleAdminUserId.Value == currentUserId.Value)
    {
        return true;
    }

    var department = CurrentUserDepartment();
    return string.Equals(mainTask.OwningDepartment, department, StringComparison.OrdinalIgnoreCase);
}

// The department a submission's task is scoped to, for Review's access
// check. OwningDepartment is the real boundary — it's meant to be
// populated on every task now (see Create). Falling back to the parent
// Main Task's OwningDepartment covers a subtask whose own value is
// unexpectedly null; falling back further to the assignee's own
// Department is ONLY for tasks created before this rule existed, never a
// general substitute for the task's own department. Requires
// task.ParentTask (when task.ParentTaskId is set) to be loaded by the
// caller's query.
private static string? GetEffectiveDepartment(TaskItem task, TaskAssignment? assignment)
{
    if (!string.IsNullOrWhiteSpace(task.OwningDepartment))
    {
        return task.OwningDepartment;
    }

    if (task.ParentTaskId.HasValue && !string.IsNullOrWhiteSpace(task.ParentTask?.OwningDepartment))
    {
        return task.ParentTask!.OwningDepartment;
    }

    return assignment?.User?.Department;
}

// Review authorization split by task type: SuperAdmin (office-wide) may
// only review Direct Admin Task submissions — the Admin who did the work
// is never the one who approves it. Department-scoped Admin/Supervisor
// may only review everything else (ordinary employee tasks and Department
// Directive subtasks) within their own department, and can never reach a
// Direct Admin Task's submission — so an Admin can never review their own
// Direct Admin submission.
private bool CanReviewSubmission(TaskSubmission submission)
{
    var isDirectAdminSubmission = TaskTypes.IsAssignedByOpd(submission.Task!.TaskType);

    if (isDirectAdminSubmission)
    {
        return IsOfficeWideTaskManager();
    }

    if (IsOfficeWideTaskManager())
    {
        return false; // SuperAdmin/OPD only reviews Direct Admin Task submissions.
    }

    if (!IsDepartmentTaskManager())
    {
        return false;
    }

    var department = CurrentUserDepartment();
    var effectiveDepartment = GetEffectiveDepartment(submission.Task, submission.TaskAssignment);
    return string.Equals(effectiveDepartment, department, StringComparison.OrdinalIgnoreCase);
}

// requiredDepartment, when given, always wins — this is what keeps a
// subtask's assignee list from ever crossing into a department other than
// its parent Main Task's, regardless of whether the caller would
// otherwise be office-wide (SuperAdmin) or department-scoped. Existing
// callers (Edit GET, RepopulateEditContextAsync) pass nothing and keep
// their original behavior via the default.
private async Task PopulateEmployeesAsync(string? requiredDepartment = null)
{
    var employeesQuery = _context.Users
        .Where(u => u.IsActive && u.Role == "Employee");

    if (requiredDepartment != null)
    {
        employeesQuery = employeesQuery.Where(u => u.Department == requiredDepartment);
    }
    else if (!IsOfficeWideTaskManager())
    {
        // Office-wide task managers may assign anyone. Department-scoped
        // managers may only pick employees from their own department — this
        // is what actually keeps Admin/Supervisor task assignment
        // department-scoped, not just the Index listing.
        var department = CurrentUserDepartment();
        employeesQuery = employeesQuery.Where(u => u.Department == department);
    }

    var employees = await employeesQuery.OrderBy(u => u.FullName).ToListAsync();
    ViewBag.Employees = new SelectList(employees, "Id", "FullName");

    // Client-side only — lets Create.cshtml narrow visible checkboxes to
    // one department the instant a Parent Main Task is picked, with no
    // round trip. Never the authorization boundary: ValidateAssigneeIdsAsync
    // re-checks server-side regardless of what this dictionary says.
    ViewBag.EmployeeDepartments = employees.ToDictionary(e => e.Id.ToString(), e => e.Department ?? string.Empty);
}

// Populates the server-rendered Responsible Admin dropdown. The client
// refreshes it when the department changes, while this covers initial and
// validation-error renders. The complete list must be rendered so the
// browser can filter it as soon as a target department is selected.
// OwningDepartment of a Whole Office task (it belongs to the OPD).
private const string WholeOfficeDepartment = "Office of the Provincial Director";

private async Task PopulateResponsibleAdminsAsync()
{
    // Shown on the "Whole Office" option: how many people would get it.
    ViewBag.WholeOfficeCount = await _context.Users
        .CountAsync(u => u.IsActive && (u.Role == "Admin" || u.Role == "Employee"));

    var admins = await _context.Users
        .Where(u => u.IsActive && u.Role == "Admin")
        .OrderBy(u => u.FullName)
        .ToListAsync();

    // Use the User objects, rather than SelectList items, because Create.cshtml
    // needs Department to tag each option for the client-side filter.
    ViewBag.ResponsibleAdmins = admins;
}

// Confirms the posted ResponsibleAdminUserId is an active Admin belonging
// to the task's own target department — the core rule this feature adds.
private async Task ValidateResponsibleAdminAsync(int? responsibleAdminUserId, string targetDepartment, string modelKey)
{
    if (responsibleAdminUserId == null)
    {
        ModelState.AddModelError(modelKey, "Select the Admin responsible for this task.");
        return;
    }

    var isValid = await _context.Users.AnyAsync(u =>
        u.Id == responsibleAdminUserId.Value &&
        u.IsActive &&
        u.Role == "Admin" &&
        u.Department == targetDepartment);

    if (!isValid)
    {
        ModelState.AddModelError(modelKey, "Selected Admin must be an active Admin in the target department.");
    }
}

private async Task ValidateAssigneeIdsAsync(List<int> assigneeIds, string modelKey)
{
    if (assigneeIds == null || assigneeIds.Count == 0)
    {
        ModelState.AddModelError(modelKey, "Select at least one assignee.");
        return;
    }

    var distinct = assigneeIds.Distinct().ToList();
    var validQuery = _context.Users
        .Where(u => distinct.Contains(u.Id) && u.IsActive && u.Role == "Employee");

    if (!IsOfficeWideTaskManager())
    {
        var department = CurrentUserDepartment();
        validQuery = validQuery.Where(u => u.Department == department);
    }

    var validCount = await validQuery.CountAsync();

    if (validCount != distinct.Count)
    {
        ModelState.AddModelError(modelKey, "One or more selected assignees are invalid.");
    }
}
private async Task<List<TaskAssignmentSummaryViewModel>> BuildAssignmentSummariesAsync(TaskItem task)
{
    var assignmentIds = task.Assignments.Select(a => a.Id).ToList();
    var submittedAssignmentIds = await _context.TaskSubmissions
        .Where(s => s.TaskAssignmentId != null && assignmentIds.Contains(s.TaskAssignmentId.Value))
        .Select(s => s.TaskAssignmentId!.Value)
        .Distinct()
        .ToListAsync();

    return task.Assignments
        .OrderByDescending(a => a.IsPrimaryAssignee)
        .ThenBy(a => a.User?.FullName)
        .Select(a => new TaskAssignmentSummaryViewModel
        {
            UserId = a.UserId,
            HasSubmitted = submittedAssignmentIds.Contains(a.Id),
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
    ViewBag.IsAssignmentOnly = (task.ParentTaskId.HasValue || task.TaskLevel == TaskLevels.Main) && !IsOfficeWideTaskManager();
    ViewBag.TaskCode = $"TASK-{task.Id:D4}";
    ViewBag.CurrentStatus = task.Status;
    ViewBag.CurrentProgress = task.Progress;
    ViewBag.CreatedAt = task.CreatedAt;
    ViewBag.AssignmentSummaries = await BuildAssignmentSummariesAsync(task);

    var suggestion = PrioritySuggestionService.Suggest(task.DueDate);
    ViewBag.SuggestedPriority = suggestion.Priority;
    ViewBag.SuggestedReason = suggestion.Reason;

    await PopulateEmployeesAsync();
}
// GET: /Tasks/Review/5  (submissionId)
[HttpGet]
public async Task<IActionResult> Review(int id)
{
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var submission = await _context.TaskSubmissions
        .Include(s => s.Task).ThenInclude(t => t!.Assignee)
        .Include(s => s.Task).ThenInclude(t => t!.ParentTask)
        .Include(s => s.TaskAssignment).ThenInclude(a => a!.User)
        .FirstOrDefaultAsync(s => s.Id == id);

    if (submission == null || submission.Task == null || submission.TaskAssignment == null)
    {
        return NotFound();
    }

    if (!CanReviewSubmission(submission))
    {
        TempData["ErrorMessage"] = "You cannot review this submission. It must be reviewed by the task's authorized manager.";
        return RedirectToAction(nameof(Index));
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
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var submission = await _context.TaskSubmissions
        .Include(s => s.Task).ThenInclude(t => t!.Assignments)
        .Include(s => s.Task).ThenInclude(t => t!.ParentTask)
        .Include(s => s.TaskAssignment).ThenInclude(a => a!.User)
        .FirstOrDefaultAsync(s => s.Id == model.SubmissionId);

    if (submission == null || submission.Task == null || submission.TaskAssignment == null)
    {
        return NotFound();
    }

    if (!CanReviewSubmission(submission))
    {
        TempData["ErrorMessage"] = "You cannot review this submission. It must be reviewed by the task's authorized manager.";
        return RedirectToAction(nameof(Index));
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

    // If this subtask belongs to an OPD Main Task, roll the change up so
    // the Main Task's own Status/Progress reflects it too.
    await _taskAssignments.PropagateToParentMainTaskAsync(task);

    if (validatorId.HasValue)
    {
        TaskActivityLogger.Log(_context, task.Id, validatorId.Value, TaskActivityType.Validated,
            submissionDecision == "approved"
                ? "Submission approved — marked completed for this assignee."
                : $"Submission returned for correction: \"{model.AdminRemarks}\"",
            submission.Id);
    }

    await _context.SaveChangesAsync();

    if (nextAssignmentStatus == TaskWorkflow.Completed)
    {
        await _notifications.NotifyTaskApprovedAsync(assignment.UserId, task.Id, task.TaskName);

        // This approval may have completed the last open subtask of a
        // Department Directive. The service re-reads the Main Task's status
        // from the database and only notifies OPD if it's actually Completed.
        if (task.ParentTaskId.HasValue)
        {
            await _notifications.NotifyOpdDirectiveCompletedAsync(task.ParentTaskId.Value);
        }
    }
    else
    {
        await _notifications.NotifyTaskReturnedAsync(assignment.UserId, task.Id, task.TaskName, model.AdminRemarks!);
    }

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
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var suggestion = PrioritySuggestionService.Suggest(dueDate);
    return Json(new { priority = suggestion.Priority, reason = suggestion.Reason });
}

// GET: /Tasks/MainTaskDetails/5
// Read-only rollup view for OPD: linked subtasks, their assignees,
// per-subtask status/progress/due date/overdue state, and the Main Task's
// own Status/Progress as computed by TaskAssignmentService.RecalculateMainTaskFromSubtasks.
[HttpGet]
public async Task<IActionResult> MainTaskDetails(int id)
{
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var mainTask = await _context.TaskItems
        .Include(t => t.CreatedBy)
        .Include(t => t.ResponsibleAdmin)
        .Include(t => t.Subtasks).ThenInclude(s => s.Assignments).ThenInclude(a => a.User)
        .Include(t => t.Subtasks).ThenInclude(s => s.Assignments).ThenInclude(a => a.AssignedBy)
        .Include(t => t.Assignments).ThenInclude(a => a.User)
        .Include(t => t.Assignments).ThenInclude(a => a.AssignedBy)
        // Pending proofs, so the OPD can review each person of a Whole Office task.
        .Include(t => t.Submissions)
        // This page loads the main task's assignments and its subtasks (each
        // with assignments). A single SQL query multiplies those rows.
        .AsSplitQuery()
        .FirstOrDefaultAsync(t => t.Id == id && t.TaskLevel == TaskLevels.Main);

    if (mainTask == null)
    {
        return NotFound();
    }

    if (!IsWithinMainTaskScope(mainTask))
    {
        return NotFound();
    }

    return View(mainTask);
}
    }
}
