using System.Security.Cryptography;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services.Email;
using DTIOneLink.Services.Outlook;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Controllers
{
    public class AccountController : Controller
    {
        private const string SessionOtpUserId = "Otp.UserId";
        private const string SessionOtpHash = "Otp.Hash";
        private const string SessionOtpExpiry = "Otp.Expiry";
        private const string SessionOtpEmail = "Otp.Email";
        private const string SessionReturnUrl = "Otp.ReturnUrl";
        private const string SessionPwdChangeUserId = "Pwd.UserId";

        private const string SessionUserId = "Auth.UserId";
        private const string SessionUserName = "Auth.UserName";
        private const string SessionUserRole = "Auth.Role";

        private readonly ILogger<AccountController> _logger;
        private readonly AppDbContext _db;
        private readonly IEmailSender _email;
        private readonly IOutlookProfileService _profiles;
        private readonly int _otpExpiryMinutes;

        public AccountController(
            ILogger<AccountController> logger,
            AppDbContext db,
            IEmailSender email,
            IOutlookProfileService profiles,
            IConfiguration config)
        {
            _logger = logger;
            _db = db;
            _email = email;
            _profiles = profiles;
            _otpExpiryMinutes = config.GetValue("Auth:OtpExpiryMinutes", 10);
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

            await StartOtpChallengeAsync(user);

            HttpContext.Session.SetString(SessionReturnUrl, model.ReturnUrl ?? string.Empty);

            return RedirectToAction(nameof(VerifyOtp));
        }

        // GET: /Account/VerifyOtp
        [HttpGet]
        public IActionResult VerifyOtp()
        {
            var email = HttpContext.Session.GetString(SessionOtpEmail);
            if (HttpContext.Session.GetInt32(SessionOtpUserId) is null || string.IsNullOrEmpty(email))
            {
                return RedirectToAction(nameof(Login));
            }

            return View(new OtpViewModel { MaskedEmail = MaskEmail(email) });
        }

        // POST: /Account/VerifyOtp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(OtpViewModel model)
        {
            var pendingId = HttpContext.Session.GetInt32(SessionOtpUserId);
            var email = HttpContext.Session.GetString(SessionOtpEmail);

            if (pendingId is null || string.IsNullOrEmpty(email))
            {
                return RedirectToAction(nameof(Login));
            }

            model.MaskedEmail = MaskEmail(email);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var expiryTicks = HttpContext.Session.GetString(SessionOtpExpiry);
            var storedHash = HttpContext.Session.GetString(SessionOtpHash);

            if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(expiryTicks)
                || !long.TryParse(expiryTicks, out var ticks)
                || DateTime.UtcNow.Ticks > ticks)
            {
                ModelState.AddModelError(string.Empty, "The code has expired. Please request a new one.");
                return View(model);
            }

            if (!PasswordHasher.Verify(model.Code, storedHash))
            {
                ModelState.AddModelError(string.Empty, "Incorrect code. Please try again.");
                return View(model);
            }

            var user = await _db.UserItems.FindAsync(pendingId.Value);
            if (user is null)
            {
                ClearOtpSession();
                return RedirectToAction(nameof(Login));
            }

            ClearOtpSession();

            await SyncOutlookProfileAsync(user);

            if (user.MustChangePassword)
            {
                HttpContext.Session.SetInt32(SessionPwdChangeUserId, user.Id);
                return RedirectToAction(nameof(ChangePassword));
            }

            return SignInAndRedirect(user);
        }

        // POST: /Account/ResendOtp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp()
        {
            var pendingId = HttpContext.Session.GetInt32(SessionOtpUserId);
            if (pendingId is null)
            {
                return RedirectToAction(nameof(Login));
            }

            var user = await _db.UserItems.FindAsync(pendingId.Value);
            if (user is null)
            {
                ClearOtpSession();
                return RedirectToAction(nameof(Login));
            }

            await StartOtpChallengeAsync(user);
            TempData["OtpMessage"] = "A new code has been sent to your email.";
            return RedirectToAction(nameof(VerifyOtp));
        }

        // GET: /Account/ChangePassword
        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (HttpContext.Session.GetInt32(SessionPwdChangeUserId) is null)
            {
                return RedirectToAction(nameof(Login));
            }

            return View(new ChangePasswordViewModel());
        }

        // POST: /Account/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var userId = HttpContext.Session.GetInt32(SessionPwdChangeUserId);
            if (userId is null)
            {
                return RedirectToAction(nameof(Login));
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _db.UserItems.FindAsync(userId.Value);
            if (user is null)
            {
                HttpContext.Session.Remove(SessionPwdChangeUserId);
                return RedirectToAction(nameof(Login));
            }

            if (PasswordHasher.Verify(model.NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Please choose a password different from the default.");
                return View(model);
            }

            user.PasswordHash = PasswordHasher.Hash(model.NewPassword);
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();

            HttpContext.Session.Remove(SessionPwdChangeUserId);
            TempData["SuccessMessage"] = "Password updated successfully.";
            return SignInAndRedirect(user);
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }

        private async Task StartOtpChallengeAsync(UserItem user)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

            HttpContext.Session.SetInt32(SessionOtpUserId, user.Id);
            HttpContext.Session.SetString(SessionOtpHash, PasswordHasher.Hash(code));
            HttpContext.Session.SetString(SessionOtpEmail, user.Email);
            HttpContext.Session.SetString(
                SessionOtpExpiry,
                DateTime.UtcNow.AddMinutes(_otpExpiryMinutes).Ticks.ToString());

            await _email.SendOtpAsync(user.Email, code);
        }

        private async Task SyncOutlookProfileAsync(UserItem user)
        {
            var profile = await _profiles.GetProfileAsync(user.Email);
            if (profile is null)
            {
                return;
            }

            var changed = false;
            if (!string.IsNullOrWhiteSpace(profile.DisplayName) && profile.DisplayName != user.FullName)
            {
                user.FullName = profile.DisplayName;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(profile.Department) && profile.Department != user.Department)
            {
                user.Department = profile.Department;
                changed = true;
            }

            if (changed)
            {
                await _db.SaveChangesAsync();
            }
        }

        private IActionResult SignInAndRedirect(UserItem user)
        {
            HttpContext.Session.SetInt32(SessionUserId, user.Id);
            HttpContext.Session.SetString(SessionUserName, user.FullName);
            HttpContext.Session.SetString(SessionUserRole, user.Role);

            var returnUrl = HttpContext.Session.GetString(SessionReturnUrl);
            HttpContext.Session.Remove(SessionReturnUrl);

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

        private void ClearOtpSession()
        {
            HttpContext.Session.Remove(SessionOtpUserId);
            HttpContext.Session.Remove(SessionOtpHash);
            HttpContext.Session.Remove(SessionOtpExpiry);
            HttpContext.Session.Remove(SessionOtpEmail);
        }

        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 1)
            {
                return email;
            }

            var name = email[..at];
            var domain = email[at..];
            var visible = name[0];
            return $"{visible}{new string('*', Math.Max(1, name.Length - 1))}{domain}";
        }
    }
}
