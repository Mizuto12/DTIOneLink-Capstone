using DTIOneLink.Data;
using DTIOneLink.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Controllers
{
    public class AccountController : Controller
    {
        private readonly ILogger<AccountController> _logger;
        private readonly AppDbContext _db;
        private readonly PasswordHasher<User> _passwordHasher = new();

        public AccountController(ILogger<AccountController> logger, AppDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            var model = new LoginViewModel { ReturnUrl = returnUrl };
            return View(model);
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == model.Username.ToLower());

            const string genericError = "Invalid username or password.";

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, genericError);
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty, genericError);
                return View(model);
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);

            if (result != PasswordVerificationResult.Success
                && result != PasswordVerificationResult.SuccessRehashNeeded)
            {
                ModelState.AddModelError(string.Empty, genericError);
                return View(model);
            }

           // Sign the user in via session — RecordsController, ReportsController, and
            // their shared views all read this same key to decide access and layout.
            HttpContext.Session.SetString("UserRole", user.Role);
            HttpContext.Session.SetString("Username", user.FullName);
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetInt32("UserId", user.Id);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            // Route by role — SuperAdmin gets its own dashboard, distinct from Admin
            if (string.Equals(user.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("SuperAdminDashboard", "Dashboard");
            }
            else if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("AdminDashboard", "Dashboard");
            }
            else
            {
                return RedirectToAction("Index", "Employee");
            }
        }
        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Account");
        }
    }
}