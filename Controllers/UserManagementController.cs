using Microsoft.AspNetCore.Mvc;
using DTIOneLink.Models;
using DTIOneLink.Services;

namespace DTIOneLink.Controllers
{
    public class UserManagementController : Controller
    {
        // GET: /UserManagement/Index
        public IActionResult Index()
        {
            var users = InMemoryStore.GetAllUsers();
            return View(users);
        }

        // GET: /UserManagement/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /UserManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(UserItem user)
        {
            if (!ModelState.IsValid)
            {
                return View(user);
            }

            InMemoryStore.AddUser(user);
            TempData["SuccessMessage"] = "User created successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
