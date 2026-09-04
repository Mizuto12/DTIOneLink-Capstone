using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Filters;
using DTIOneLink.Security;

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
            var isElevated = userRole == "Admin" || userRole == "Supervisor";

            IQueryable<TaskItem> query = _context.TaskItems.Include(t => t.Assignee);

            if (!isElevated)
            {
                query = query.Where(t => t.AssigneeId == userId.Value);
            }
            var tasks = await query
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }

        [HttpGet]
        [RequirePermission(Permissions.ViewOfficeWideSummaries)]
        public async Task<IActionResult> SuperAdminDashboard()
        {
            // Office-wide, live query — no AssigneeId filter, unlike AdminDashboard.
            var tasks = await _context.TaskItems
                .Include(t => t.Assignee)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            // Same status classification convention as _DashboardContent.cshtml:
            // Status is free text ("pending" / "ongoing"|"in-progress" / "completed"),
            // matched case-insensitively by keyword rather than exact value.
            static string Norm(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();
            static bool IsCompleted(TaskItem t) => Norm(t.Status).Contains("complet") || Norm(t.Status) == "done";
            static bool IsInProgress(TaskItem t) => !IsCompleted(t) && (Norm(t.Status).Contains("progress") || Norm(t.Status).Contains("ongoing"));
            static bool IsTodo(TaskItem t) => !IsCompleted(t) && !IsInProgress(t);

            var today = DateTime.Today;
            bool IsOverdue(TaskItem t) => !IsCompleted(t) && t.DueDate.Date < today;

            var vm = new SuperAdminDashboardViewModel
            {
                Tasks = tasks,
                TodoCount = tasks.Count(IsTodo),
                InProgressCount = tasks.Count(IsInProgress),
                CompletedCount = tasks.Count(IsCompleted),
                OverdueTasks = tasks.Where(IsOverdue)
                                     .OrderBy(t => t.DueDate)
                                     .ToList(),
            };

            vm.EmployeeWorkloads = tasks
                .Where(t => t.Assignee != null)
                .GroupBy(t => t.Assignee)
                .Select(g =>
                {
                    var total = g.Count();
                    var completed = g.Count(IsCompleted);
                    return new EmployeeWorkloadSummary
                    {
                        FullName = g.Key!.FullName,
                        TotalAssigned = total,
                        ToDo = g.Count(IsTodo),
                        InProgress = g.Count(IsInProgress),
                        Completed = completed,
                        Overdue = g.Count(IsOverdue),
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
            var isElevated = userRole == "Admin" || userRole == "Supervisor";

            IQueryable<TaskItem> query = _context.TaskItems
                .Include(t => t.Assignee)
                .Include(t => t.Submissions).ThenInclude(s => s.ValidatedBy)
                .Include(t => t.Activities).ThenInclude(a => a.PerformedBy)
                .Include(t => t.Activities).ThenInclude(a => a.RelatedSubmission)
                .Include(t => t.Comments).ThenInclude(c => c.Author);

            var task = isElevated
                ? await query.FirstOrDefaultAsync(t => t.Id == id)
                : await query.FirstOrDefaultAsync(t => t.Id == id && t.AssigneeId == userId);

            if (task == null)
            {
                return NotFound();
            }

            return View("~/Views/Employee/Details.cshtml", task);
        }
    }
}