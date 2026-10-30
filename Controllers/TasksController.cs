using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Models;
using DTIOneLink.Data;
using DTIOneLink.Services;
using DTIOneLink.Security;
using DTIOneLink.Filters;

namespace DTIOneLink.Controllers
{
    [RequireLogin]
    public class TasksController : Controller
    {
        private readonly AppDbContext _context;
        private readonly NotificationService _notifications;
        private readonly TaskAssignmentService _taskAssignments;
        private readonly OpdTaskService _opdTasks;
        private readonly ILogger<TasksController> _logger;

        public TasksController(AppDbContext context, NotificationService notifications,
            TaskAssignmentService taskAssignments, OpdTaskService opdTasks, ILogger<TasksController> logger)
        {
            _context = context;
            _notifications = notifications;
            _taskAssignments = taskAssignments;
            _opdTasks = opdTasks;
            _logger = logger;
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

            return View(await BuildIndexModelAsync(q, status, priority, department, employeeId, due, sort, page));
        }

        // The Task Management list (filters, counts, one page of tasks). Also
        // shown behind the Create Task form, so it overlays the real list.
        private async Task<TaskIndexViewModel> BuildIndexModelAsync(
            string? q, string? status, string? priority, string? department,
            int? employeeId, string? due, string? sort, int page)
        {
            var isOfficeWide = IsOfficeWideTaskManager();
            var ownDepartment = CurrentUserDepartment();
            var currentUserId = HttpContext.Session.GetInt32("UserId");
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
                        t.Assignments.Any(a => a.User != null && a.User.Department == ownDepartment)) ||
                    // A directive reassigned to this Admin across departments
                    // (ReassignResponsibleAdmin) still needs to show up here,
                    // or they'd have no way to find it at all.
                    (t.ResponsibleAdminUserId.HasValue && t.ResponsibleAdminUserId.Value == currentUserId) ||
                    (t.ParentTask != null && t.ParentTask.ResponsibleAdminUserId.HasValue &&
                        t.ParentTask.ResponsibleAdminUserId.Value == currentUserId));
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
            var today = TimeZoneHelper.PhilippineToday;
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

            // Subtask count per Department Directive on this page, shown under
            // the assignee ("Assignee · Subtasks: 3"). One small read-only query.
            var directiveIds = model.Tasks
                .Where(t => t.TaskLevel == TaskLevels.Main && t.TaskType == TaskTypes.DepartmentDirective)
                .Select(t => t.Id)
                .ToList();
            if (directiveIds.Count > 0)
            {
                var counts = await _context.TaskItems
                    .Where(s => s.ParentTaskId.HasValue && directiveIds.Contains(s.ParentTaskId.Value))
                    .GroupBy(s => s.ParentTaskId!.Value)
                    .Select(g => new { ParentId = g.Key, Count = g.Count() })
                    .ToListAsync();
                model.SubtaskCounts = directiveIds.ToDictionary(id => id, id => counts.FirstOrDefault(c => c.ParentId == id)?.Count ?? 0);
            }

            return model;
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
            ViewBag.TaskList = await BuildIndexModelAsync(null, null, null, null, null, null, null, 1);
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

            // [Required]+[StringLength] alone let a name/description made
            // only of symbols or punctuation through (e.g. "...", "---") —
            // same gap already fixed for User Management's FullName field.
            if (!HasRealContent(model.TaskName))
            {
                ModelState.AddModelError(nameof(model.TaskName), "Task name can't be made up of only symbols or punctuation.");
            }
            if (!HasRealContent(model.Description))
            {
                ModelState.AddModelError(nameof(model.Description), "Task description can't be made up of only symbols or punctuation.");
            }

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

            // Was only "[Required]" on the string — any non-empty value was
            // accepted and persisted, unlike every sibling field here
            // (TaskType, OwningDepartment, Recurrence above), which all
            // have their own allow-list check.
            if (!TaskPriorities.IsValid(model.Priority))
            {
                ModelState.AddModelError(nameof(model.Priority), "Select a valid priority.");
            }

            // The inline subtask list's <input> has maxlength=200 and the
            // JS caps how many rows can be added, but neither is re-checked
            // server-side — a direct POST could bypass both.
            var subtaskNames = (model.SubtaskNames ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (subtaskNames.Count > MaxSubtasksOnCreate)
            {
                ModelState.AddModelError(nameof(model.SubtaskNames), $"A task can have at most {MaxSubtasksOnCreate} subtasks.");
            }
            else if (subtaskNames.Any(s => s.Trim().Length > 200))
            {
                ModelState.AddModelError(nameof(model.SubtaskNames), "Each subtask name must be at most 200 characters.");
            }

            // Nothing previously checked this at all — a task could be
            // created already overdue, which the UI would immediately
            // badge as "Overdue" with no actual work having been skipped.
            if (model.DueDate.Date < TimeZoneHelper.PhilippineToday)
            {
                ModelState.AddModelError(nameof(model.DueDate), "The due date can't be in the past.");
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
                ViewBag.TaskList = await BuildIndexModelAsync(null, null, null, null, null, null, null, 1);
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
                    recurrence,
                    model.RequiresReview),
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

        // POST: /Tasks/ReassignResponsibleAdmin/5 — OPD only. Hands a Main
        // Task to a different Admin (e.g. covering for one on leave). The new
        // Admin must be in the task's own OwningDepartment — same rule
        // ValidateResponsibleAdminAsync enforces at creation.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReassignResponsibleAdmin(int id, int newResponsibleAdminUserId)
        {
            if (!IsOfficeWideTaskManager())
            {
                return StatusCode(403);
            }

            var task = await _context.TaskItems
                .Include(t => t.Assignments).ThenInclude(a => a.User)
                .FirstOrDefaultAsync(t => t.Id == id && t.TaskLevel == TaskLevels.Main);
            if (task == null)
            {
                return NotFound();
            }

            if (task.TaskType == TaskTypes.WholeOffice || !task.ResponsibleAdminUserId.HasValue)
            {
                TempData["ErrorMessage"] = "This task has no single Responsible Admin to reassign.";
                return RedirectToAction(nameof(MainTaskDetails), new { id });
            }

            var newAdmin = await _context.Users.FirstOrDefaultAsync(u =>
                u.Id == newResponsibleAdminUserId && u.IsActive && u.Role == "Admin");
            if (newAdmin == null)
            {
                TempData["ErrorMessage"] = "Select an active Admin to hand this task to.";
                return RedirectToAction(nameof(MainTaskDetails), new { id });
            }

            if (newAdmin.Department != task.OwningDepartment)
            {
                TempData["ErrorMessage"] = $"Select an Admin from {task.OwningDepartment} — the Responsible Admin must belong to the task's own department.";
                return RedirectToAction(nameof(MainTaskDetails), new { id });
            }

            if (newAdmin.Id == task.ResponsibleAdminUserId.Value)
            {
                TempData["ErrorMessage"] = "This Admin is already responsible for this task.";
                return RedirectToAction(nameof(MainTaskDetails), new { id });
            }

            var oldAdminId = task.ResponsibleAdminUserId.Value;
            var changedByUserId = HttpContext.Session.GetInt32("UserId");

            if (task.TaskType == TaskTypes.DirectAdmin)
            {
                var assignment = task.Assignments.FirstOrDefault(a => a.UserId == oldAdminId);
                if (assignment == null)
                {
                    TempData["ErrorMessage"] = "Could not find this task's current assignment to reassign.";
                    return RedirectToAction(nameof(MainTaskDetails), new { id });
                }
                if (!TaskAssignmentService.CanReassign(assignment))
                {
                    TempData["ErrorMessage"] = "This task is awaiting review or already completed, so it can't be reassigned.";
                    return RedirectToAction(nameof(MainTaskDetails), new { id });
                }
                _taskAssignments.Reassign(task, assignment, newAdmin, changedByUserId);
                _taskAssignments.RecalculateOverallStatus(task);
            }

            task.ResponsibleAdminUserId = newAdmin.Id;

            if (changedByUserId.HasValue)
            {
                TaskActivityLogger.Log(_context, task.Id, changedByUserId.Value, "updated",
                    $"Reassigned the responsible Admin to {newAdmin.FullName}.");
            }

            await _notifications.NotifyAdminDirectiveReceivedAsync(newAdmin.Id, task.Id, task.TaskName);

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"This task is now the responsibility of {newAdmin.FullName}.";
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
         .Include(t => t.CreatedBy)
         .Include(t => t.Comments).ThenInclude(c => c.Author)
         .Include(t => t.Activities).ThenInclude(a => a.PerformedBy)
         .Include(t => t.Activities).ThenInclude(a => a.RelatedSubmission)
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

         // SuperAdmin created every Main Task, so SuperAdmin can always edit
         // its own fields (TaskName/DueDate/Priority/Description) here,
         // regardless of type or subtask count — that used to redirect
         // SuperAdmin away to the read-only MainTaskDetails with no way to
         // ever change them. Admin/Supervisor keeps the existing, narrower
         // rules: direct assignment to the Main Task itself is only allowed
         // while it has zero subtasks (the two assignment models — direct
         // vs. via subtasks — are mutually exclusive, so once any subtask
         // exists, assignment happens there instead), and a task the OPD
         // gave straight to people (Direct Admin or Whole Office) is theirs
         // to work, not the Admin's to touch at all.
         if (!IsOfficeWideTaskManager() && (task.Subtasks.Any() || TaskTypes.IsAssignedByOpd(task.TaskType)))
         {
             return RedirectToAction(nameof(MainTaskDetails), new { id = task.Id });
         }

         // The Assignees section (current-assignee list + Add/Reassign) only
         // makes sense when this Main Task actually carries its own
         // TaskAssignments — never while it has subtasks, which is where
         // assignment happens instead, for every role including SuperAdmin.
         isDirectMainTaskAssignment = !task.Subtasks.Any();
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
    // Narrower still than the local isDirectMainTaskAssignment above: this
    // specifically drives the "no subtasks yet" note, which only makes
    // sense on a zero-subtask Department Directive — not a Direct Admin /
    // Whole Office task (those never have subtasks at all, so the broader
    // flag is true there too, but the note's wording wouldn't fit), and
    // never when editing one of its subtasks — AddSubtask takes a Main Task
    // id, so the "Add Subtask" button must only ever show up here.
    ViewBag.IsDirectMainTaskAssignment = isDirectMainTaskAssignment
        && task.TaskLevel == TaskLevels.Main
        && TaskTypes.DepartmentDirective == task.TaskType;
    // The People tab itself: always shown for an ordinary subtask, and for
    // a Main Task only while it has no subtasks of its own — a Main Task
    // with subtasks has no direct-assignment model at all, for any role.
    ViewBag.ShowAssigneeSection = task.TaskLevel != TaskLevels.Main || isDirectMainTaskAssignment;
    ViewBag.OpdDueDate = ViewBag.IsAssignmentOnly ? task.ParentTask?.DueDate : null;
    // Due soon or overdue: the OPD deadline is shown but no longer a limit.
    ViewBag.CanPassOpdDueDate = TaskWorkflow.IsDueSoonOrOverdue(task.Status, task.DueDate);

    // Read-only display context for the "Task Overview" panel — not part of
    // TaskEditViewModel on purpose (Status/Progress/CreatedAt aren't editable
    // here; they're owned by the employee-facing Update/SubmitProof flow and
    // the Admin Review decision).
    ViewBag.TaskCode = $"TASK-{task.Id:D4}";
    ViewBag.CurrentStatus = task.Status;      // aggregate across all assignees
    ViewBag.CurrentProgress = task.Progress;  // aggregate across all assignees
    ViewBag.CreatedAt = task.CreatedAt;
    ViewBag.AssignmentSummaries = await BuildAssignmentSummariesAsync(task);

    // This page previously had no way to show or post comments, nor any
    // activity history, even though the backend (AddComment/GetAccessibleTaskAsync)
    // already allowed an elevated Admin/SuperAdmin to do both on a subtask
    // reached from Task Management — only the view was missing it.
    ViewBag.Comments = task.Comments?.OrderByDescending(c => c.CreatedAt).ToList()
        ?? new List<TaskComment>();
    ViewBag.Activities = task.Activities ?? new List<TaskActivity>();
    ViewBag.CreatorLabel = task.CreatedBy?.FullName ?? "Unknown";

    // Initial suggestion for page load, before any JS runs. Recalculated
    // live via SuggestPriority whenever the due-date field changes.
    var suggestion = PrioritySuggestionService.Suggest(task.DueDate, task.TaskType);
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

        // Same reasoning as the GET action. SuperAdmin may always post a
        // field change for any Main Task. Admin/Supervisor may only post an
        // assignee change here while the Main Task still has zero
        // subtasks — re-checked here, not trusted from the client, so a
        // tampered POST can't sneak assignments onto a Main Task that
        // gained subtasks after the page was loaded.
        if (!IsOfficeWideTaskManager() && (task.Subtasks.Any() || TaskTypes.IsAssignedByOpd(task.TaskType)))
        {
            return Forbid();
        }

        isDirectMainTaskAssignment = !task.Subtasks.Any();
    }
    else if (!IsWithinTaskScope(task))
    {
        return NotFound();
    }

    // A Main Task with subtasks has no assignment model of its own — work
    // (and so assignment) happens at the subtask level instead, for every
    // role including SuperAdmin. Only this check, never trusted from the
    // client, decides whether AddAssigneeIds/ReassignTo apply at all; a
    // tampered POST can't sneak direct assignments onto a task that has
    // subtasks just because the client still sent that data.
    var hasAssignmentModel = task.TaskLevel != TaskLevels.Main || isDirectMainTaskAssignment;

    // Nobody is removed outright. Current assignees stay as they are unless
    // reassigned, in which case their assignment (progress included) moves
    // to the person taking over. Only incoming people are validated.
    var currentAssigneeIds = task.Assignments.Select(a => a.UserId).ToList();
    var addIds = hasAssignmentModel
        ? (model.AddAssigneeIds ?? new()).Where(id => !currentAssigneeIds.Contains(id)).Distinct().ToList()
        : new List<int>();
    var reassignments = hasAssignmentModel
        ? (model.ReassignTo ?? new())
            .Where(r => r.Value.HasValue && r.Value.Value != r.Key && currentAssigneeIds.Contains(r.Key))
            .ToDictionary(r => r.Key, r => r.Value!.Value)
        : new Dictionary<int, int>();
    var desiredAssigneeIds = currentAssigneeIds.Concat(addIds).ToList();

    if (hasAssignmentModel)
    {
        var incomingIds = addIds.Concat(reassignments.Values).ToList();
        if (incomingIds.Count != incomingIds.Distinct().Count()
            || reassignments.Values.Any(currentAssigneeIds.Contains))
        {
            ModelState.AddModelError(nameof(model.AssigneeIds),
                "Each person can only be picked once, and only someone not already on this task can take over.");
        }
        else if (incomingIds.Count > 0)
        {
            await ValidateAssigneeIdsAsync(incomingIds, nameof(model.AssigneeIds));
        }

        foreach (var fromUserId in reassignments.Keys)
        {
            var assignment = task.Assignments.First(a => a.UserId == fromUserId);
            if (!TaskAssignmentService.CanReassign(assignment))
            {
                ModelState.AddModelError(nameof(model.AssigneeIds),
                    $"{assignment.User?.FullName ?? "This person"}'s work is awaiting review or already approved, so it can't be reassigned.");
            }
        }

        if (desiredAssigneeIds.Count == 0)
        {
            ModelState.AddModelError(nameof(model.AssigneeIds), "Select at least one person for this task.");
        }

        // Completed is a terminal state everywhere else in the app
        // (TaskWorkflow.AllowedTransitions[Completed] is empty, and every
        // direct status write goes through CanTransition) — but adding a
        // new assignee here used to bypass that entirely: the new row
        // starts Pending, and RecalculateOverallStatus (below) recomputes
        // the task's overall status purely from the assignment list, with
        // no awareness that it had already finished. The task would
        // silently flip back to "In Progress" with no transition check and
        // no audit trail entry explaining why.
        if (addIds.Count > 0 && task.Status == TaskWorkflow.Completed)
        {
            ModelState.AddModelError(nameof(model.AssigneeIds),
                "This task is already completed, so a new person can't be added to it.");
        }
    }

    // Same rule as the GET action. Re-checked here rather than trusted from
    // a hidden form field, so a tampered POST can't re-enable these fields —
    // if this is an OPD-issued subtask, or a Main Task being assigned to
    // directly, and the caller isn't office-wide, TaskName/Priority/
    // Description are simply never written, regardless of what the client
    // submitted. The due date is the exception: the Admin may adjust it.
    var isAssignmentOnly = (task.ParentTaskId.HasValue || isDirectMainTaskAssignment) && !IsOfficeWideTaskManager();

    // An Admin may move an OPD subtask's due date, but not past the OPD's
    // own deadline for the Main Task — unless the subtask is due soon or
    // already overdue, when that deadline usually leaves no room to extend
    // (the OPD is notified of the change below). Only checked when the date
    // changes, so an older subtask already past it can still be saved as-is.
    if (isAssignmentOnly && task.ParentTask != null
        && !TaskWorkflow.IsDueSoonOrOverdue(task.Status, task.DueDate)
        && model.DueDate.Date != task.DueDate.Date
        && model.DueDate.Date > task.ParentTask.DueDate.Date)
    {
        ModelState.AddModelError(nameof(model.DueDate),
            $"The due date can't be later than the OPD's deadline ({task.ParentTask.DueDate:MMM d, yyyy}).");
    }

    // Nothing previously checked this at all. Only when the date actually
    // changes — same "only checked when it changes" rule as the OPD-deadline
    // check above — so an older task whose due date is already in the past
    // can still be saved as-is without being forced to also fix the date.
    if (model.DueDate.Date != task.DueDate.Date && model.DueDate.Date < TimeZoneHelper.PhilippineToday)
    {
        ModelState.AddModelError(nameof(model.DueDate), "The due date can't be in the past.");
    }

    // Priority is only ever written below when !isAssignmentOnly (an
    // assignment-only edit never touches it), so there's nothing to
    // validate when it won't be used — same reasoning as the GET action's
    // "these fields are never written" comment above.
    if (!isAssignmentOnly && !TaskPriorities.IsValid(model.Priority))
    {
        ModelState.AddModelError(nameof(model.Priority), "Select a valid priority.");
    }

    // Same gap already fixed for User Management's FullName field — only
    // checked when these fields are actually written below (isAssignmentOnly
    // never touches them), same reasoning as the Priority check above.
    if (!isAssignmentOnly && !HasRealContent(model.TaskName))
    {
        ModelState.AddModelError(nameof(model.TaskName), "Task name can't be made up of only symbols or punctuation.");
    }
    if (!isAssignmentOnly && !HasRealContent(model.Description))
    {
        ModelState.AddModelError(nameof(model.Description), "Task description can't be made up of only symbols or punctuation.");
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

    // Only the editable fields — Progress, Status, CreatedAt, Submissions
    // are untouched, same discipline as Employee.Update's comment block.
    task.DueDate = model.DueDate;
    if (!isAssignmentOnly)
    {
        task.TaskName = model.TaskName;
        task.Priority = model.Priority;
        task.Description = model.Description;
    }

    var changedByUserId = HttpContext.Session.GetInt32("UserId");

    // desiredAssigneeIds only ever adds, so nothing is removed here.
    var sync = await _taskAssignments.SyncAssignmentsAsync(task, desiredAssigneeIds, changedByUserId);

    // Hand each reassigned person's assignment to whoever takes over. Their
    // progress (e.g. Bong's 40%) stays on the row, so the new person
    // continues from it, and the previous person loses access.
    var affectedUserIds = sync.Added.Concat(reassignments.Keys).Concat(reassignments.Values).Distinct().ToList();
    var affectedUsers = await _context.Users
        .Where(u => affectedUserIds.Contains(u.Id))
        .ToDictionaryAsync(u => u.Id);
    string NameOf(int userId) => affectedUsers.TryGetValue(userId, out var u) ? u.FullName : $"User #{userId}";

    var reassignedProgress = new Dictionary<int, int>();
    foreach (var (fromUserId, toUserId) in reassignments)
    {
        var assignment = task.Assignments.First(a => a.UserId == fromUserId);
        reassignedProgress[fromUserId] = assignment.Progress;
        _taskAssignments.Reassign(task, assignment, affectedUsers[toUserId], changedByUserId);
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
    if (changedByUserId.HasValue)
    {
        foreach (var addedUserId in sync.Added)
        {
            TaskActivityLogger.Log(_context, task.Id, changedByUserId.Value, TaskActivityType.Assigned,
                $"Assigned {NameOf(addedUserId)} to this task.");
        }

        foreach (var (fromUserId, toUserId) in reassignments)
        {
            TaskActivityLogger.Log(_context, task.Id, changedByUserId.Value, TaskActivityType.Reassigned,
                $"Reassigned from {NameOf(fromUserId)} to {NameOf(toUserId)}. Progress carried over ({reassignedProgress[fromUserId]}%).");
        }

        if (oldDueDate.Date != task.DueDate.Date)
        {
            TaskActivityLogger.Log(_context, task.Id, changedByUserId.Value, TaskActivityType.Edited,
                $"Due date changed from {oldDueDate:MMM d, yyyy} to {task.DueDate:MMM d, yyyy}.");
        }
    }

    await _context.SaveChangesAsync();

    // Notify only the affected people, and only for what actually changed.
    // Newly added assignees get "assigned to you"; whoever takes over gets
    // "reassigned to you" and the previous person is told who has it now.
    // Everyone on the task before AND after this edit gets notified about
    // due-date/priority changes (each is independent, so both can fire).
    foreach (var newUserId in sync.Added)
    {
        await _notifications.NotifyTaskAssignedAsync(newUserId, task.Id, task.TaskName);
    }

    foreach (var (fromUserId, toUserId) in reassignments)
    {
        await _notifications.NotifyTaskReassignedAsync(toUserId, task.Id, task.TaskName);
        await _notifications.NotifyTaskReassignedAwayAsync(fromUserId, task.Id, task.TaskName, NameOf(toUserId));
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
    else if (oldDueDate.Date != task.DueDate.Date)
    {
        // An Admin moved the due date of an OPD-issued task — the OPD is told.
        // (Returns no recipients for a task the OPD didn't issue.)
        var adminName = HttpContext.Session.GetString("FullName") ?? "An Admin";
        await _notifications.NotifyOpdDueDateChangedByAdminAsync(task.Id, task.TaskName, adminName, oldDueDate, task.DueDate);
    }

    var stillAssignedUserIds = oldAssigneeUserIds.Except(reassignments.Keys).ToList();
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

    // A subtask's own Main Task may have been handed to this Admin across
    // departments (ReassignResponsibleAdmin deliberately allows this, e.g.
    // covering for an Admin on leave) — same bypass IsWithinMainTaskScope
    // already gives the Main Task page itself. Without it, the department
    // check below would silently lock the new Responsible Admin out of
    // every subtask under their own directive. Requires task.ParentTask to
    // be loaded by the caller's query.
    var currentUserId = HttpContext.Session.GetInt32("UserId");
    var responsibleAdminUserId = task.ResponsibleAdminUserId ?? task.ParentTask?.ResponsibleAdminUserId;
    if (responsibleAdminUserId.HasValue && currentUserId.HasValue &&
        responsibleAdminUserId.Value == currentUserId.Value)
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

    // Same cross-department bypass as IsWithinTaskScope/IsWithinMainTaskScope:
    // a directive reassigned to this Admin outside their own department
    // still needs to be reviewable by them, not just visible.
    var currentUserId = HttpContext.Session.GetInt32("UserId");
    var responsibleAdminUserId = submission.Task.ResponsibleAdminUserId ?? submission.Task.ParentTask?.ResponsibleAdminUserId;
    if (responsibleAdminUserId.HasValue && currentUserId.HasValue &&
        responsibleAdminUserId.Value == currentUserId.Value)
    {
        return true;
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
private const int MaxSubtasksOnCreate = 50;

// A task name/description made only of symbols or punctuation (e.g. "...",
// "---") passed [Required]+[StringLength] unnoticed — same gap already
// fixed for User Management's FullName (UserManagementController.
// HasRealNameContent). Letters OR digits count here (unlike FullName,
// which required a letter specifically) since a legitimate task name can
// be numeric-only, e.g. "Q3 2026 Report".
private static bool HasRealContent(string value) => value.Any(char.IsLetterOrDigit);

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
    var previousIds = task.Assignments
        .Where(a => a.ReassignedFromUserId.HasValue)
        .Select(a => a.ReassignedFromUserId!.Value)
        .Distinct()
        .ToList();
    var previousNames = await _context.Users
        .Where(u => previousIds.Contains(u.Id))
        .ToDictionaryAsync(u => u.Id, u => u.FullName);

    return task.Assignments
        .OrderByDescending(a => a.IsPrimaryAssignee)
        .ThenBy(a => a.User?.FullName)
        .Select(a => new TaskAssignmentSummaryViewModel
        {
            UserId = a.UserId,
            CanReassign = TaskAssignmentService.CanReassign(a),
            ReassignedFromName = a.ReassignedFromUserId.HasValue
                && previousNames.TryGetValue(a.ReassignedFromUserId.Value, out var previous) ? previous : null,
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
    // A Main Task with subtasks has no assignment model of its own, same
    // rule the Edit POST action enforces — mirrored here so a validation
    // failure redisplay doesn't wrongly show (or hide) the Assignees
    // section/note. Reachable at all for a Main Task only means it's
    // already past the GET/POST actions' redirect-away check.
    var isMainLevelDirectAssignment = task.TaskLevel == TaskLevels.Main && !task.Subtasks.Any();
    ViewBag.ShowAssigneeSection = task.TaskLevel != TaskLevels.Main || isMainLevelDirectAssignment;
    ViewBag.IsDirectMainTaskAssignment = isMainLevelDirectAssignment && TaskTypes.DepartmentDirective == task.TaskType;
    ViewBag.IsAssignmentOnly = (task.ParentTaskId.HasValue || task.TaskLevel == TaskLevels.Main) && !IsOfficeWideTaskManager();
    ViewBag.OpdDueDate = ViewBag.IsAssignmentOnly ? task.ParentTask?.DueDate : null;
    // Due soon or overdue: the OPD deadline is shown but no longer a limit.
    ViewBag.CanPassOpdDueDate = TaskWorkflow.IsDueSoonOrOverdue(task.Status, task.DueDate);
    ViewBag.TaskCode = $"TASK-{task.Id:D4}";
    ViewBag.CurrentStatus = task.Status;
    ViewBag.CurrentProgress = task.Progress;
    ViewBag.CreatedAt = task.CreatedAt;
    ViewBag.AssignmentSummaries = await BuildAssignmentSummariesAsync(task);

    var suggestion = PrioritySuggestionService.Suggest(task.DueDate, task.TaskType);
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

    // Shown dimmed behind the Review dialog, same as Create Task's overlay.
    ViewBag.TaskList = await BuildIndexModelAsync(null, null, null, null, null, null, null, 1);

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
                ViewBag.TaskList = await BuildIndexModelAsync(null, null, null, null, null, null, null, 1);
                return View(submission);
            }
            // [Range(0, 100)] on the ViewModel is never enforced here —
            // this action never checks ModelState.IsValid as a whole, only
            // these specific hand-rolled conditions (same as every other
            // check in this switch) — so the bound value needs its own
            // explicit guard, same way AdminRemarks gets one above.
            if (model.Progress is < 0 or > 100)
            {
                ModelState.AddModelError(nameof(model.Progress), "Progress must be between 0 and 100.");
                ViewBag.TaskList = await BuildIndexModelAsync(null, null, null, null, null, null, null, 1);
                return View(submission);
            }
            nextAssignmentStatus = TaskWorkflow.ReturnedForCorrection;
            submissionDecision = "returned";
            break;
        default:
            ModelState.AddModelError(nameof(model.Decision), "Choose Approve or Return.");
            ViewBag.TaskList = await BuildIndexModelAsync(null, null, null, null, null, null, null, 1);
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
    submission.RowVersion = Guid.NewGuid().ToByteArray();

    assignment.Status = nextAssignmentStatus;
    if (nextAssignmentStatus == TaskWorkflow.Completed)
    {
        assignment.Progress = 100; // Completed implies fully progressed, for display consistency
    }
    else if (nextAssignmentStatus == TaskWorkflow.ReturnedForCorrection && model.Progress.HasValue)
    {
        // Admin's correction to the assignee's self-reported percentage —
        // only reachable on Return (see TaskSubmissionDecisionViewModel.
        // Progress's comment for why Approve is never affected by this).
        assignment.Progress = model.Progress.Value;
    }
    assignment.RowVersion = Guid.NewGuid().ToByteArray();

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

    try
    {
        await _context.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        // Two Review decisions on the same submission (two tabs, two
        // reviewers) racing: submission.Decision != null above only
        // catches a SEQUENTIAL double-click, not two requests that both
        // read Decision == null before either saved. The RowVersion
        // tokens on TaskAssignment/TaskSubmission catch that case here.
        TempData["ErrorMessage"] = "This submission was just reviewed (maybe from another tab or by someone else). Please refresh and try again.";
        return RedirectToAction(nameof(Index));
    }

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
// GET: /Tasks/SuggestPriority?dueDate=2026-09-10&taskType=whole-office
// Called by JS whenever the due-date field (or, on Create, the task-type
// field) changes, so the suggestion always reflects what's currently typed,
// not just the values that existed when the page first loaded. taskType is
// optional — Edit's own task type never changes after creation, so only
// Create's JS ever sends it.
[HttpGet]
public IActionResult SuggestPriority(DateTime dueDate, string? taskType = null)
{
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var suggestion = PrioritySuggestionService.Suggest(dueDate, taskType);
    return Json(new { priority = suggestion.Priority, reason = suggestion.Reason });
}

// POST: /Tasks/AddSubtask/5 — adds one more subtask to an already-created
// Department Directive. Only the OPD (SuperAdmin) creates tasks or adds
// subtasks — an Admin/Supervisor only assigns the subtasks already there,
// same rule as Create.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> AddSubtask(int id, string subtaskName, bool requiresReview = true)
{
    if (!IsOfficeWideTaskManager())
    {
        return StatusCode(403);
    }

    var mainTask = await _context.TaskItems
        .FirstOrDefaultAsync(t => t.Id == id && t.TaskLevel == TaskLevels.Main);
    if (mainTask == null)
    {
        return NotFound();
    }

    if (mainTask.TaskType != TaskTypes.DepartmentDirective)
    {
        TempData["ErrorMessage"] = "Subtasks can only be added to a Department Directive.";
        return RedirectToAction(nameof(MainTaskDetails), new { id });
    }

    if (string.IsNullOrWhiteSpace(subtaskName))
    {
        TempData["ErrorMessage"] = "Type a name for the subtask.";
        return RedirectToAction(nameof(MainTaskDetails), new { id });
    }
    if (subtaskName.Trim().Length > 200)
    {
        TempData["ErrorMessage"] = "Subtask name is too long (maximum 200 characters).";
        return RedirectToAction(nameof(MainTaskDetails), new { id });
    }

    var createdByUserId = HttpContext.Session.GetInt32("UserId");
    await _opdTasks.AddSubtaskAsync(mainTask, subtaskName.Trim(), createdByUserId, requiresReview);

    TempData["SuccessMessage"] = "Subtask added.";
    return RedirectToAction(nameof(MainTaskDetails), new { id });
}

// GET: /Tasks/Checklist/5 — per-assignee checklist management. Each
// assignee gets their own checklist (TaskChecklistItem.TaskAssignmentId),
// same granularity as their own Progress — a shared task's assignees never
// see or affect each other's checklist.
[HttpGet]
public async Task<IActionResult> Checklist(int id)
{
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var task = await _context.TaskItems
        .Include(t => t.Assignments).ThenInclude(a => a.User)
        .Include(t => t.Assignments).ThenInclude(a => a.ChecklistItems)
        .Include(t => t.ParentTask)
        .AsSplitQuery()
        .FirstOrDefaultAsync(t => t.Id == id);
    if (task == null || !IsWithinTaskScope(task))
    {
        return NotFound();
    }

    return View(task);
}

// POST: /Tasks/AddChecklistItem — adds one item to one assignee's
// checklist. The moment an assignment has any items, they (not the manual
// progress slider) decide its Progress — RecalculateProgressFromChecklist
// is a no-op until then.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> AddChecklistItem(int taskAssignmentId, string label, int weight = 1)
{
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var assignment = await _context.TaskAssignments
        .Include(a => a.Task).ThenInclude(t => t!.ParentTask)
        .Include(a => a.ChecklistItems)
        .FirstOrDefaultAsync(a => a.Id == taskAssignmentId);
    if (assignment?.Task == null || !IsWithinTaskScope(assignment.Task))
    {
        return NotFound();
    }

    if (string.IsNullOrWhiteSpace(label))
    {
        TempData["ErrorMessage"] = "Type a label for the checklist item.";
        return RedirectToAction(nameof(Checklist), new { id = assignment.TaskId });
    }
    if (label.Trim().Length > 200)
    {
        TempData["ErrorMessage"] = "Checklist item label is too long (maximum 200 characters).";
        return RedirectToAction(nameof(Checklist), new { id = assignment.TaskId });
    }

    if (assignment.ChecklistItems.Count >= TaskWorkflow.MaxChecklistItemsPerAssignment)
    {
        TempData["ErrorMessage"] = $"A checklist can have at most {TaskWorkflow.MaxChecklistItemsPerAssignment} items.";
        return RedirectToAction(nameof(Checklist), new { id = assignment.TaskId });
    }

    _context.TaskChecklistItems.Add(new TaskChecklistItem
    {
        TaskAssignmentId = assignment.Id,
        Label = label.Trim(),
        Weight = Math.Clamp(weight, 1, 100)
    });

    var userId = HttpContext.Session.GetInt32("UserId");
    if (userId.HasValue)
    {
        TaskActivityLogger.Log(_context, assignment.TaskId, userId.Value, "updated",
            $"Added a checklist item for {assignment.User?.FullName ?? "an assignee"}: \"{label.Trim()}\".");
    }

    await _context.SaveChangesAsync();
    TempData["SuccessMessage"] = "Checklist item added.";
    return RedirectToAction(nameof(Checklist), new { id = assignment.TaskId });
}

// POST: /Tasks/RemoveChecklistItem — if this empties the assignment's
// checklist, RecalculateProgressFromChecklist becomes a no-op again on the
// next write, so manual progress control resumes on its own; this action
// itself doesn't touch Progress (no checklist change has occurred until a
// remaining item is toggled, so there's nothing to recompute yet).
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> RemoveChecklistItem(int id)
{
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    var item = await _context.TaskChecklistItems
        .Include(c => c.TaskAssignment).ThenInclude(a => a!.Task).ThenInclude(t => t!.ParentTask)
        .FirstOrDefaultAsync(c => c.Id == id);
    if (item?.TaskAssignment?.Task == null || !IsWithinTaskScope(item.TaskAssignment.Task))
    {
        return NotFound();
    }

    var taskId = item.TaskAssignment.TaskId;
    _context.TaskChecklistItems.Remove(item);

    var userId = HttpContext.Session.GetInt32("UserId");
    if (userId.HasValue)
    {
        TaskActivityLogger.Log(_context, taskId, userId.Value, "updated",
            $"Removed a checklist item: \"{item.Label}\".");
    }

    await _context.SaveChangesAsync();
    TempData["SuccessMessage"] = "Checklist item removed.";
    return RedirectToAction(nameof(Checklist), new { id = taskId });
}

// POST: /Tasks/UpdateAssignmentProgress — panelist item T10: lets an
// authorized Admin/SuperAdmin correct an assignee's progress from the
// general Task Details page (Views/Employee/_TaskDetails.cshtml, shown for
// an elevated viewer who isn't the assignee), not just at the moment of a
// Review decision (TasksController.Review's own Progress field, scoped to
// Return only, is unaffected by this — a separate, narrower capability).
// Same lock rules as the assignee's own slider (TaskAssignmentService.
// ApplyProgressChangeAsync): editable on Pending/In Progress/Returned for
// Correction, frozen on Completed/ForReview — an Admin correcting a task
// that's actively awaiting review belongs in the Review decision, not here.
// JSON response (not TempData/redirect) so the page can update in place,
// matching EmployeeController.UpdateProgress's own pattern for the same UI
// concept.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> UpdateAssignmentProgress(int taskAssignmentId, int progress)
{
    if (!CanAccessTaskManagement())
    {
        return StatusCode(403);
    }

    if (progress < 0 || progress > 100)
    {
        return BadRequest(new { message = "Progress must be between 0 and 100." });
    }

    var assignment = await _context.TaskAssignments
        .Include(a => a.Task).ThenInclude(t => t!.ParentTask)
        .Include(a => a.ChecklistItems)
        .Include(a => a.User)
        .FirstOrDefaultAsync(a => a.Id == taskAssignmentId);
    if (assignment?.Task == null || !IsWithinTaskScope(assignment.Task))
    {
        return NotFound(new { message = "That assignment could not be found." });
    }

    var previousProgress = assignment.Progress;
    var error = await _taskAssignments.ApplyProgressChangeAsync(assignment.Task, assignment, progress, requestedStatus: null);
    if (error != null)
    {
        return BadRequest(new { message = error });
    }

    var userId = HttpContext.Session.GetInt32("UserId");
    if (userId.HasValue)
    {
        var adminName = HttpContext.Session.GetString("FullName") ?? "An administrator";
        TaskActivityLogger.Log(_context, assignment.TaskId, userId.Value, TaskActivityType.ProgressUpdated,
            $"{adminName} adjusted {assignment.User?.FullName ?? "the assignee"}'s progress from {previousProgress}% to {progress}%.");
    }

    try
    {
        await _context.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return BadRequest(new { message = "This task just changed (maybe it was reviewed, or updated from another tab). Please refresh and try again." });
    }

    return Json(new
    {
        progress = assignment.Progress,
        status = assignment.Status,
        taskProgress = assignment.Task.Progress,
        taskStatus = assignment.Task.Status
    });
}

// POST: /Tasks/Delete/5 — OPD only, same symmetry as Create/AddSubtask
// (only the OPD creates a Main Task or its subtasks, so only the OPD
// removes them). Permanently deletes a Main Task — a Direct Admin Task,
// Department Directive, or Whole Office task — and, for a Department
// Directive, every one of its subtasks with it. Refused once any real work
// exists anywhere in that tree (any assignment past 0% or any proof ever
// submitted), the same protection EmployeeController.Delete gives a shared
// task's other assignees — a mistaken or no-longer-needed directive can
// still be removed, but real employee work never silently disappears.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Delete(int id)
{
    if (!IsOfficeWideTaskManager())
    {
        return StatusCode(403);
    }

    var mainTask = await _context.TaskItems
        .Include(t => t.Assignments)
        .Include(t => t.Submissions)
        .Include(t => t.Activities)
        .Include(t => t.Comments)
        .Include(t => t.Subtasks).ThenInclude(s => s.Assignments)
        .Include(t => t.Subtasks).ThenInclude(s => s.Submissions)
        .Include(t => t.Subtasks).ThenInclude(s => s.Activities)
        .Include(t => t.Subtasks).ThenInclude(s => s.Comments)
        .AsSplitQuery()
        .FirstOrDefaultAsync(t => t.Id == id && t.TaskLevel == TaskLevels.Main);

    if (mainTask == null)
    {
        return NotFound();
    }

    var wholeTree = new List<TaskItem> { mainTask };
    wholeTree.AddRange(mainTask.Subtasks);

    var hasStarted = wholeTree.Any(t =>
        t.Submissions.Any() ||
        t.Assignments.Any(a => a.Progress > 0 || !string.Equals(a.Status, TaskWorkflow.Pending, StringComparison.OrdinalIgnoreCase)));

    if (hasStarted)
    {
        TempData["ErrorMessage"] = mainTask.Subtasks.Any()
            ? "This task can't be deleted because work has already started on it or one of its subtasks."
            : "This task can't be deleted because work has already started on it.";
        return RedirectToAction(nameof(MainTaskDetails), new { id });
    }

    var taskName = mainTask.TaskName;
    var deletedByUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
    await _taskAssignments.PermanentlyDeleteTasksAsync(wholeTree, deletedByUserId, _logger);

    TempData["SuccessMessage"] = $"\"{taskName}\" was deleted.";
    return RedirectToAction(nameof(Index));
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
        .Include(t => t.Subtasks).ThenInclude(s => s.Assignments).ThenInclude(a => a.ReassignedFrom)
        .Include(t => t.Subtasks).ThenInclude(s => s.Submissions)
        .Include(t => t.Assignments).ThenInclude(a => a.User)
        .Include(t => t.Assignments).ThenInclude(a => a.AssignedBy)
        .Include(t => t.Assignments).ThenInclude(a => a.ReassignedFrom)
        // Pending proofs, so the OPD can review each person of a Whole Office task.
        .Include(t => t.Submissions)
        // Comments were previously never loaded here at all, so this page
        // (unlike Employee/Details.cshtml) had no way to show or post them —
        // Admin/SuperAdmin genuinely could not comment on a Main Task.
        .Include(t => t.Comments).ThenInclude(c => c.Author)
        // Same gap as Comments above — Activity History wasn't shown here
        // either. Subtask activities are pulled in too so the one timeline
        // covers everything that happened under this directive, not just
        // the Main Task row itself.
        .Include(t => t.Activities).ThenInclude(a => a.PerformedBy)
        .Include(t => t.Activities).ThenInclude(a => a.RelatedSubmission)
        .Include(t => t.Subtasks).ThenInclude(s => s.Activities).ThenInclude(a => a.PerformedBy)
        .Include(t => t.Subtasks).ThenInclude(s => s.Activities).ThenInclude(a => a.RelatedSubmission)
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

    if (IsOfficeWideTaskManager())
    {
        await PopulateResponsibleAdminsAsync();
    }

    // Same rule Delete enforces server-side — surfaced here so the view can
    // disable the Delete button before showing an irreversible-delete
    // warning the server would refuse anyway.
    var wholeTree = new List<TaskItem> { mainTask };
    wholeTree.AddRange(mainTask.Subtasks);
    ViewBag.HasStarted = wholeTree.Any(t =>
        t.Submissions.Any() ||
        t.Assignments.Any(a => a.Progress > 0 || !string.Equals(a.Status, TaskWorkflow.Pending, StringComparison.OrdinalIgnoreCase)));

    return View(mainTask);
}
    }
}
