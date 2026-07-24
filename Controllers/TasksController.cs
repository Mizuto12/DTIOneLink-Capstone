using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.ViewModels;

namespace DTIOneLink.Controllers
{
    public class TasksController : Controller
    {
        private readonly AppDbContext _db;

        public TasksController(AppDbContext db)
        {
            _db = db;
        }

        // GET: /Tasks/Index
        public async Task<IActionResult> Index()
        {
            var vm = await BuildViewModelAsync();
            return View(vm);
        }

        // POST: /Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind(Prefix = "NewTask")] TaskItem task)
        {
            if (!ModelState.IsValid)
            {
                var vm = await BuildViewModelAsync();
                vm.NewTask = task;
                vm.OpenModal = true;
                return View("Index", vm);
            }

            task.CreatedAt = DateTime.UtcNow;
            _db.TaskItems.Add(task);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task created successfully!";
            return RedirectToAction(nameof(Index));
        }

        private async Task<TaskManagementViewModel> BuildViewModelAsync()
        {
            return new TaskManagementViewModel
            {
                Tasks = await _db.TaskItems.OrderByDescending(t => t.CreatedAt).ToListAsync(),
                Users = await _db.UserItems.OrderBy(u => u.FullName).ToListAsync()
            };
        }
    }
}
