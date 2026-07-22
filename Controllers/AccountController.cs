using DTIOneLink.Models;
using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    public class AccountController : Controller
    {
        private readonly ILogger<AccountController> _logger;

        public AccountController(ILogger<AccountController> logger)
        {
            _logger = logger;
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
        public IActionResult Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // TODO: Replace with real authentication against SQL Server
            // (e.g. validate credentials via a UsersRepository / EF Core DbContext
            // backed by Microsoft SQL Server, managed through SSMS).
            bool isValidUser = ValidateCredentials(model.Username, model.Password);

            if (!isValidUser)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            // TODO: Sign the user in (cookie auth / session) here.

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            // Role by username convention: "admin" -> admin dashboard, everyone else -> employee.
            if (string.Equals(model.Username, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("AdminDashboard", "Dashboard");
            }

            return RedirectToAction("Index", "Employee");
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            // TODO: Clear auth cookie/session here when real auth is wired
            return RedirectToAction("Login", "Account");
        }

        private bool ValidateCredentials(string username, string password)
        {
            // Placeholder logic only — swap for a stored-procedure / EF Core
            // lookup against the Microsoft SQL Server database.
            return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
        }
    }
}
