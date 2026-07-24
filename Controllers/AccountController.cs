using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Controllers
{
    public class AccountController : Controller
    {
        private const string SessionUserId = "Auth.UserId";
        private const string SessionUserName = "Auth.UserName";
        private const string SessionUserRole = "Auth.Role";

        private readonly ILogger<AccountController> _logger;
        private readonly AppDbContext _db;

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

            var email = model.Username.Trim();
            var user = await _db.UserItems
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

            if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(model);
            }

            if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "This account is inactive. Contact an administrator.");
                return View(model);
            }

            return SignInAndRedirect(user, model.ReturnUrl);
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }

        private IActionResult SignInAndRedirect(UserItem user, string? returnUrl)
        {
            HttpContext.Session.SetInt32(SessionUserId, user.Id);
            HttpContext.Session.SetString(SessionUserName, user.FullName);
            HttpContext.Session.SetString(SessionUserRole, user.Role);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("AdminDashboard", "Dashboard");
            }

            return RedirectToAction("Index", "Employee");
        }
    }
}
