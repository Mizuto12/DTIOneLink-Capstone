using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Models;

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
                .Include(t => t.Activities).ThenInclude(a => a.PerformedBy);

            var task = isElevated
                ? await query.FirstOrDefaultAsync(t => t.Id == id)
                : await query.FirstOrDefaultAsync(t => t.Id == id && t.AssigneeId == userId);

            if (task == null)
            {
                return NotFound();
            }
            // Reuses the same detail view built for the Employee Kanban board —
            // it only renders real TaskItem fields, so it's valid for either
            // role. See the note at the top of Details.cshtml about the
            // Layout line if Admins use a different shared layout file.
            return View("~/Views/Employee/Details.cshtml", task);
        }
    }
}