using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Models;

namespace DTIOneLink.Controllers
{
    public class UserManagementController : Controller
    {
        private readonly AppDbContext _db;

        public UserManagementController(AppDbContext db)
        {
            _db = db;
        }

        // GET: /UserManagement/Index
        public async Task<IActionResult> Index()
        {
            var users = await _db.UserItems
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();
            return View(users);
        }

        // POST: /UserManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserItem user)
        {
            if (!ModelState.IsValid)
            {
                var users = await _db.UserItems
                    .OrderByDescending(u => u.CreatedAt)
                    .ToListAsync();
                return View(nameof(Index), users);
            }

            user.CreatedAt = DateTime.UtcNow;
            _db.UserItems.Add(user);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "User created successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
