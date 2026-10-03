using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Filters;
using DTIOneLink.Security;
using DTIOneLink.Services;

namespace DTIOneLink.Controllers
{
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> AdminDashboard()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var userRole = HttpContext.Session.GetString("UserRole");
            var isDepartmentElevated = userRole == "Admin" || userRole == "Supervisor";

            // Office-wide (SuperAdmin) is its own permission check, same as
            // TasksController/DashboardController.Details — never granted by
            // the Admin/Supervisor role-string check above.
            var isOfficeWide = RolePermissions.Has(userRole, Permissions.ManageOfficeWideTasks);

            IQueryable<TaskItem> query = _context.TaskItems
                .Include(t => t.Assignee)
                .Include(t => t.ParentTask)
                .Include(t => t.Assignments).ThenInclude(a => a.User);

            if (isDepartmentElevated && !isOfficeWide)
            {
                // Same effective-department priority used throughout the app:
                // this task's own OwningDepartment first; if null, inherit the
                // parent Main Task's OwningDepartment; only if BOTH are null
                // (a legacy pre-OwningDepartment task) fall back to any
                // assignee's own Department. This is what this action was
                // previously missing entirely — every Admin/Supervisor saw
                // every department's tasks here regardless of their own
                // Department.
                var department = HttpContext.Session.GetString("UserDepartment");
                query = query.Where(t =>
                    (t.OwningDepartment != null && t.OwningDepartment == department) ||
                    (t.OwningDepartment == null && t.ParentTask != null && t.ParentTask.OwningDepartment == department) ||
                    (t.OwningDepartment == null && (t.ParentTask == null || t.ParentTask.OwningDepartment == null) &&
                        t.Assignments.Any(a => a.User != null && a.User.Department == department)));
            }
            else if (!isOfficeWide)
            {
                // Employee: only tasks they hold a TaskAssignment row on.
                // (Kept as t.Assignments rather than the legacy AssigneeId
                // column, matching the multi-assignee model used elsewhere —
                // AssigneeId only ever reflects the primary assignee.)
                query = query.Where(t => t.Assignments.Any(a => a.UserId == userId.Value));
            }
            // isOfficeWide: no filter — SuperAdmin sees every department.

            var tasks = await query
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }

        [HttpGet]
        [RequirePermission(Permissions.ViewOfficeWideSummaries)]
        public async Task<IActionResult> SuperAdminDashboard()
        {
            // Count TaskAssignment rows. TaskItem.AssigneeId is only the
            // legacy/primary assignee and undercounts Whole Office work.
            var assignments = await _context.TaskAssignments
                .AsNoTracking()
                .Include(a => a.Task)
                .Include(a => a.User)
                .OrderByDescending(a => a.Task!.CreatedAt)
                .ToListAsync();

            var workItems = assignments.Where(a => a.Task != null && a.User != null)
                .Select(a => new DashboardAssignmentSummary
                {
                    TaskName = a.Task!.TaskName, AssigneeName = a.User!.FullName,
                    DueDate = a.Task.DueDate, Progress = a.Progress,
                    Status = TaskWorkflow.Normalize(a.Status), CreatedAt = a.Task.CreatedAt
                }).ToList();
            static bool IsStatus(DashboardAssignmentSummary item, string status) => item.Status == status;
            static bool IsOverdue(DashboardAssignmentSummary item) => TaskWorkflow.IsOverdue(item.Status, item.DueDate);
            static bool IsAtRisk(DashboardAssignmentSummary item) =>
                TaskWorkflow.DelayRisk(item.Status, item.Progress, item.CreatedAt, item.DueDate) is { AtRisk: true };

            var vm = new SuperAdminDashboardViewModel
            {
                WorkItems = workItems,
                TodoCount = workItems.Count(i => IsStatus(i, TaskWorkflow.Pending)),
                InProgressCount = workItems.Count(i => IsStatus(i, TaskWorkflow.InProgress)),
                ForReviewCount = workItems.Count(i => IsStatus(i, TaskWorkflow.ForReview)),
                ReturnedForCorrectionCount = workItems.Count(i => IsStatus(i, TaskWorkflow.ReturnedForCorrection)),
                CompletedCount = workItems.Count(i => IsStatus(i, TaskWorkflow.Completed)),
                OverdueItems = workItems.Where(IsOverdue).OrderBy(i => i.DueDate).ToList(),
                AtRiskCount = workItems.Count(IsAtRisk),
            };

            vm.EmployeeWorkloads = workItems
                .GroupBy(i => i.AssigneeName)
                .Select(g =>
                {
                    var total = g.Count();
                    var completed = g.Count(i => IsStatus(i, TaskWorkflow.Completed));
                    return new EmployeeWorkloadSummary
                    {
                        FullName = g.Key,
                        TotalAssigned = total,
                        ToDo = g.Count(i => IsStatus(i, TaskWorkflow.Pending)),
                        InProgress = g.Count(i => IsStatus(i, TaskWorkflow.InProgress)),
                        ForReview = g.Count(i => IsStatus(i, TaskWorkflow.ForReview)),
                        ReturnedForCorrection = g.Count(i => IsStatus(i, TaskWorkflow.ReturnedForCorrection)),
                        Completed = completed,
                        Overdue = g.Count(IsOverdue),
                        AtRisk = g.Count(IsAtRisk),
                        EfficiencyPercent = total == 0 ? 0 : (int)Math.Round(completed * 100.0 / total),
                    };
                })
                .OrderByDescending(w => w.TotalAssigned)
                .ToList();

            // Explicit path: the view lives at Views/SuperAdmin.cshtml, not the
            // conventional Views/Dashboard/SuperAdminDashboard.cshtml location.
            return View("~/Views/SuperAdmin.cshtml", vm);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return NotFound();
            }

            var userRole = HttpContext.Session.GetString("UserRole");
            var isDepartmentElevated = userRole == "Admin" || userRole == "Supervisor";

            // Office-wide (SuperAdmin) is a separate check from the
            // department-elevated one above — granted only by
            // ManageOfficeWideTasks, never by adding "SuperAdmin" to the
            // Admin/Supervisor role list.
            var isOfficeWide = RolePermissions.Has(userRole, Permissions.ManageOfficeWideTasks);

            IQueryable<TaskItem> query = _context.TaskItems
                // The detail page is read-only, so avoid change-tracker work.
                .AsNoTracking()
                .Include(t => t.Assignee)
                .Include(t => t.CreatedBy)
                .Include(t => t.Activities).ThenInclude(a => a.PerformedBy)
                .Include(t => t.Activities).ThenInclude(a => a.RelatedSubmission)
                .Include(t => t.Comments).ThenInclude(c => c.Author)
                // Needed by the Details view to find the viewer's own
                // assignment for the progress slider.
                .Include(t => t.Assignments)
                // Activities and Comments are independent collections. Split
                // them to prevent a JOIN from multiplying their rows.
                .AsSplitQuery();

            query = query.Include(t => t.ParentTask);

            TaskItem? task;
            if (isOfficeWide)
            {
                task = await query.FirstOrDefaultAsync(t => t.Id == id);
            }
            else if (isDepartmentElevated)
            {
                // Same effective-department priority as AdminDashboard/
                // TasksController — previously this only checked the legacy
                // assignee-department fallback, so it could wrongly 404 an
                // in-department subtask with an out-of-department assignee
                // (edge case), and had no OwningDepartment check at all.
                var department = HttpContext.Session.GetString("UserDepartment");
                task = await query.FirstOrDefaultAsync(t => t.Id == id &&
                    ((t.OwningDepartment != null && t.OwningDepartment == department) ||
                     (t.OwningDepartment == null && t.ParentTask != null && t.ParentTask.OwningDepartment == department) ||
                     (t.OwningDepartment == null && (t.ParentTask == null || t.ParentTask.OwningDepartment == null) &&
                        t.Assignments.Any(a => a.User != null && a.User.Department == department))));
            }
            else
            {
                task = await query.FirstOrDefaultAsync(t => t.Id == id && t.Assignments.Any(a => a.UserId == userId));
            }

            if (task == null)
            {
                return NotFound();
            }

            return View("~/Views/Employee/Details.cshtml", task);
        }
    }
}
