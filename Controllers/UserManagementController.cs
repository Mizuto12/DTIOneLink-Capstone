using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;

namespace DTIOneLink.Controllers
{
    public class UserManagementController : Controller
    {
        private readonly AppDbContext _db;
        private readonly string _defaultPassword;

        public UserManagementController(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _defaultPassword = config.GetValue("Auth:DefaultPassword", "ChangeMe123!")!;
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
        public async Task<IActionResult> Create(
            [Bind("FullName,Email,Role,Department,Status")] UserItem user)
        {
            if (!ModelState.IsValid)
            {
                var users = await _db.UserItems
                    .OrderByDescending(u => u.CreatedAt)
                    .ToListAsync();
                return View(nameof(Index), users);
            }

            user.CreatedAt = DateTime.UtcNow;
            // New accounts start with the shared default password and must change it on first login.
            user.PasswordHash = PasswordHasher.Hash(_defaultPassword);
            user.MustChangePassword = true;
            _db.UserItems.Add(user);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "User created successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
