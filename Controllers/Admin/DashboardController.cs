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
            var userRole = HttpContext.Session.GetString("UserRole");
            var userId = HttpContext.Session.GetInt32("UserId");

            IQueryable<TaskItem> query = _context.TaskItems.Include(t => t.Assignee);

            // Employees only see tasks assigned to them; Admins see everything
            if (userRole != "Admin" && userId.HasValue)
            {
                query = query.Where(t => t.AssigneeId == userId.Value);
            }

            var tasks = await query
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tasks);
        }

        // GET: /Dashboard/Details/5
        // Same visibility rule as AdminDashboard: Admins can open any task,
        // Employees can only open tasks assigned to them.
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userId = HttpContext.Session.GetInt32("UserId");

            IQueryable<TaskItem> query = _context.TaskItems.Include(t => t.Assignee);

            if (userRole != "Admin" && userId.HasValue)
            {
                query = query.Where(t => t.AssigneeId == userId.Value);
            }

            var task = await query.FirstOrDefaultAsync(t => t.Id == id);

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