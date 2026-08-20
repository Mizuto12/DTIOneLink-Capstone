using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Models;
using DTIOneLink.Data;

namespace DTIOneLink.Controllers
{
    public class TasksController : Controller
    {
        private readonly AppDbContext _context;

        public TasksController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Tasks/Index
        public async Task<IActionResult> Index()
        {
            var tasks = await _context.TaskItems
                .Include(t => t.Assignee)
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

            TempData["SuccessMessage"] = "Task created successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}