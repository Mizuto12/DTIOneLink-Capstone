using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;

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
        public IActionResult TaskManagement()
        {
            return View();
        }
    }
}