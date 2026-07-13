using Microsoft.AspNetCore.Mvc;
using DTIOneLink.Models;
using DTIOneLink.Services;

namespace DTIOneLink.Controllers
{
    public class TasksController : Controller
    {
        // GET: /Tasks/Index
        public IActionResult Index()
        {
            var tasks = InMemoryStore.GetAllTasks();
            return View(tasks);
        }

        // GET: /Tasks/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Tasks/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(TaskItem task)
        {
            if (!ModelState.IsValid)
            {
                return View(task);
            }

            InMemoryStore.AddTask(task);
            TempData["SuccessMessage"] = "Task created successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
