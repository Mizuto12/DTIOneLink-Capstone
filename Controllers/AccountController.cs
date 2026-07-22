using DTIOneLink.Models;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DTIOneLink.Controllers
{
    public class AccountController : Controller
    {
        // ── DatabaseHelper is injected by ASP.NET Core automatically.
        // The connection string lives only in appsettings.json.
        private readonly DatabaseHelper _db;

        public AccountController(DatabaseHelper db)
        {
            _db = db;
        }

        // ── GET /Account/Login ─────────────────────────────
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // If already logged in, skip the login page
            if (HttpContext.Session.GetString("Username") != null)
            {
                return RedirectToDashboard(HttpContext.Session.GetString("Role"));
            }

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        // ── POST /Account/Login ────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // 1. Look up the user by username
            string? storedHash = null;
            string? role       = null;
            string? fullName   = null;
            bool    isActive   = false;

            using (var conn = _db.GetConnection())     // ← no connection string here
            {
                await conn.OpenAsync();

                const string sql = @"
                    SELECT PasswordHash, Role, FullName, IsActive
                    FROM   Users
                    WHERE  Username = @Username";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Username", model.Username);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    storedHash = reader["PasswordHash"].ToString();
                    role       = reader["Role"].ToString();
                    fullName   = reader["FullName"].ToString();
                    isActive   = Convert.ToBoolean(reader["IsActive"]);
                }
            }

            // 2. User not found or account disabled
            if (storedHash == null || !isActive)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            // 3. Verify the password against the stored hash
            var hasher = new PasswordHasher<object>();
            var result = hasher.VerifyHashedPassword(null!, storedHash, model.Password);

            if (result == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            // 4. Store user info in session
            HttpContext.Session.SetString("Username", model.Username);
            HttpContext.Session.SetString("Role",     role     ?? "Employee");
            HttpContext.Session.SetString("FullName", fullName ?? model.Username);

            // 5. Redirect to the correct dashboard based on role
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToDashboard(role);
        }

        // ── GET /Account/Logout ────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

            [HttpGet]
            public IActionResult Hash(string p)
            {
                var hasher = new PasswordHasher<object>();
                return Content(hasher.HashPassword(null!, p));
            }
        // ── Helper: route by role ──────────────────────────
        private IActionResult RedirectToDashboard(string? role)
        {
                    return role switch
            {
                "Admin"    => RedirectToAction("Index", "Dashboard"),
                "Employee" => RedirectToAction("Index", "Dashboard"),
                _          => RedirectToAction("Login")
            };
        }
    }
}
