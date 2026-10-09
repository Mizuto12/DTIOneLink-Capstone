using DTIOneLink.Data;
using DTIOneLink.Filters;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services;
using DTIOneLink.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Controllers
{
    // Sign-in, first-sign-in setup, Forgot Password and Change Password.
    //
    // Sign-in has up to three steps: password, then — only when the password
    // must be changed (default or temporary password) — a code sent to the
    // email if it isn't confirmed yet, then a new password. Until every step
    // is done the session holds only "PendingUserId" — not "UserId" — so
    // every page that requires login still treats the person as signed out.
    public class AccountController : Controller
    {
        private const string PendingUserIdKey = "PendingUserId";
        private const string PendingStampKey = "PendingStamp";
        private const string PendingReturnUrlKey = "PendingReturnUrl";
        private const string ResetEmailKey = "ResetEmail";
        private const string ResetUserIdKey = "ResetUserId";
        private const string ResetFakeAttemptsKey = "ResetFakeAttempts";
        private const string ResetRequestedAtKey = "ResetRequestedAt";
        private const string ResetVerifiedUserIdKey = "ResetVerifiedUserId";
        private const string ResetVerifiedAtKey = "ResetVerifiedAt";
        public const string SecurityStampKey = "SecurityStamp";

        private const string GenericLoginError = "Invalid username or password.";

        // Checked when the username doesn't exist, so a wrong username takes
        // as long as a wrong password and response time can't reveal which
        // usernames exist.
        private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), "not-a-real-password");

        private readonly ILogger<AccountController> _logger;
        private readonly AppDbContext _db;
        private readonly OneTimeCodeService _codes;
        private readonly PasswordHasher<User> _passwordHasher = new();

        public AccountController(ILogger<AccountController> logger, AppDbContext db, OneTimeCodeService codes)
        {
            _logger = logger;
            _db = db;
            _codes = codes;
        }

        // ═════════════════════════════ Login ═════════════════════════════

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
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return LoginView(model, null);
            }

            var username = model.Username.Trim();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

            if (user == null || !user.IsActive)
            {
                _passwordHasher.VerifyHashedPassword(new User(), DummyHash, model.Password);
                return LoginView(model, GenericLoginError);
            }

            var now = DateTime.UtcNow;
            if (user.LockoutEndUtc > now)
            {
                return LoginView(model, LockedMessage(user.LockoutEndUtc.Value - now));
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
            if (result != PasswordVerificationResult.Success
                && result != PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= AccountDefaults.MaxFailedLogins)
                {
                    user.FailedLoginCount = 0;
                    user.LockoutEndUtc = now + AccountDefaults.LockoutDuration;
                    await _db.SaveChangesAsync();
                    _logger.LogWarning("User {UserId} locked out after {Count} wrong passwords.", user.Id, AccountDefaults.MaxFailedLogins);
                    return LoginView(model, LockedMessage(AccountDefaults.LockoutDuration));
                }
                await _db.SaveChangesAsync();
                return LoginView(model, GenericLoginError);
            }

            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);
            }
            // Covers accounts created before MustChangePassword existed.
            if (string.Equals(model.Password, AccountDefaults.DefaultPassword, StringComparison.Ordinal))
            {
                user.MustChangePassword = true;
            }
            if (string.IsNullOrEmpty(user.SecurityStamp))
            {
                user.SecurityStamp = NewSecurityStamp();
            }
            await _db.SaveChangesAsync();

            var returnUrl = !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : null;

            if (user.MustChangePassword)
            {
                HttpContext.Session.Clear();
                HttpContext.Session.SetInt32(PendingUserIdKey, user.Id);
                HttpContext.Session.SetString(PendingStampKey, user.SecurityStamp);
                if (returnUrl != null) HttpContext.Session.SetString(PendingReturnUrlKey, returnUrl);
                return RedirectToAction(user.EmailConfirmed ? nameof(SetPassword) : nameof(VerifyEmail));
            }

            SignIn(user);
            return Redirect(returnUrl ?? RoleHomeUrl(user.Role));
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            // Shown as "Signed out" in User Management.
            if (HttpContext.Session.GetInt32("UserId") is int userId)
            {
                var now = DateTime.UtcNow;
                await _db.Users.Where(u => u.Id == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLogoutAtUtc, now));

                // Close out this session's Time Log row. Ordered by TimeInUtc
                // descending in case something left more than one open (should
                // not normally happen since each Logout closes the latest one).
                var openLog = await _db.TimeLogs
                    .Where(t => t.UserId == userId && t.TimeOutUtc == null)
                    .OrderByDescending(t => t.TimeInUtc)
                    .FirstOrDefaultAsync();
                if (openLog != null)
                {
                    openLog.TimeOutUtc = now;
                    await _db.SaveChangesAsync();
                }
            }
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Account");
        }

        // ═════════════ First sign-in: confirm email, set password ═════════════

        // GET: /Account/VerifyEmail — a code is sent automatically the first
        // time; "Resend code" sends another.
        [HttpGet]
        public async Task<IActionResult> VerifyEmail()
        {
            var user = await GetPendingUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));
            if (user.EmailConfirmed || !user.MustChangePassword) return ContinuePendingSignIn(user);

            var hasLiveCode = await _db.OneTimeCodes.AnyAsync(c =>
                c.UserId == user.Id && c.Purpose == OneTimeCodePurpose.ConfirmEmail &&
                c.ConsumedAtUtc == null && c.ExpiresAtUtc > DateTime.UtcNow);
            if (!hasLiveCode)
            {
                var issued = await _codes.IssueAsync(user, OneTimeCodePurpose.ConfirmEmail);
                if (issued.Status != CodeIssueStatus.Sent)
                {
                    TempData["CodeNotice"] = WaitMessage(issued.WaitSeconds);
                }
            }

            return CodeView(user.Email, isReset: false, new EnterCodeViewModel());
        }

        // POST: /Account/VerifyEmail
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> VerifyEmail(EnterCodeViewModel model)
        {
            var user = await GetPendingUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));
            if (user.EmailConfirmed || !user.MustChangePassword) return ContinuePendingSignIn(user);

            if (!ModelState.IsValid)
            {
                return CodeView(user.Email, isReset: false, model);
            }

            var check = await _codes.VerifyAsync(user.Id, OneTimeCodePurpose.ConfirmEmail, model.Code);
            if (check.Status != CodeCheckStatus.Valid)
            {
                return CodeView(user.Email, isReset: false, new EnterCodeViewModel(), CodeErrorMessage(check));
            }

            user.EmailConfirmed = true;
            await _db.SaveChangesAsync();
            _logger.LogInformation("User {UserId} confirmed their email.", user.Id);

            return ContinuePendingSignIn(user);
        }

        // POST: /Account/ResendVerifyEmail
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ResendVerifyEmail()
        {
            var user = await GetPendingUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));
            if (user.EmailConfirmed || !user.MustChangePassword) return ContinuePendingSignIn(user);

            var issued = await _codes.IssueAsync(user, OneTimeCodePurpose.ConfirmEmail);
            TempData["CodeNotice"] = issued.Status == CodeIssueStatus.Sent
                ? "A new code was sent. Only the newest code works."
                : WaitMessage(issued.WaitSeconds);

            return RedirectToAction(nameof(VerifyEmail));
        }

        // GET: /Account/SetPassword — default or temporary password: the user
        // chooses their own before reaching the rest of the system.
        [HttpGet]
        public async Task<IActionResult> SetPassword()
        {
            var user = await GetPendingUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));
            if (!user.EmailConfirmed || !user.MustChangePassword) return ContinuePendingSignIn(user);

            return SetPasswordView(user, isReset: false, new SetPasswordViewModel());
        }

        // POST: /Account/SetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            var user = await GetPendingUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));
            if (!user.EmailConfirmed || !user.MustChangePassword) return ContinuePendingSignIn(user);

            if (ModelState.IsValid && NewPasswordProblem(user, model.NewPassword) is string problem)
            {
                ModelState.AddModelError(nameof(model.NewPassword), problem);
            }
            if (!ModelState.IsValid)
            {
                return SetPasswordView(user, isReset: false, new SetPasswordViewModel());
            }

            await SavePasswordAsync(user, model.NewPassword);
            _logger.LogInformation("User {UserId} replaced their default/temporary password.", user.Id);

            return ContinuePendingSignIn(user);
        }

        // ═══════════════════════════ Forgot Password ═══════════════════════════
        // Every response looks the same whether or not the email belongs to an
        // account, so this page can't be used to find out who has one.

        // GET: /Account/ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordViewModel());
        }

        // POST: /Account/ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            ClearResetState();
            var email = model.Email.Trim();
            HttpContext.Session.SetString(ResetEmailKey, email);
            HttpContext.Session.SetString(ResetRequestedAtKey, DateTime.UtcNow.Ticks.ToString());

            var user = await FindResettableUserAsync(email);
            if (user != null)
            {
                HttpContext.Session.SetInt32(ResetUserIdKey, user.Id);
                // Too-soon / too-many requests send nothing, but say nothing
                // different either.
                await _codes.IssueAsync(user, OneTimeCodePurpose.PasswordReset);
                _logger.LogInformation("Password reset requested for user {UserId}.", user.Id);
            }

            return RedirectToAction(nameof(ResetCode));
        }

        // GET: /Account/ResetCode
        [HttpGet]
        public IActionResult ResetCode()
        {
            var email = HttpContext.Session.GetString(ResetEmailKey);
            if (email == null) return RedirectToAction(nameof(ForgotPassword));

            return CodeView(email, isReset: true, new EnterCodeViewModel());
        }

        // POST: /Account/ResetCode
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ResetCode(EnterCodeViewModel model)
        {
            var email = HttpContext.Session.GetString(ResetEmailKey);
            if (email == null) return RedirectToAction(nameof(ForgotPassword));

            if (!ModelState.IsValid)
            {
                return CodeView(email, isReset: true, model);
            }

            var userId = HttpContext.Session.GetInt32(ResetUserIdKey);
            CodeCheckResult check;
            if (userId == null)
            {
                // No such account: answer exactly as a real code would —
                // wrong, then out of tries, and expired after the same time.
                var requestedAt = long.TryParse(HttpContext.Session.GetString(ResetRequestedAtKey), out var t)
                    ? new DateTime(t, DateTimeKind.Utc) : DateTime.MinValue;
                var tries = HttpContext.Session.GetInt32(ResetFakeAttemptsKey) ?? 0;
                if (DateTime.UtcNow - requestedAt >= AccountDefaults.CodeLifetime)
                {
                    check = new CodeCheckResult(CodeCheckStatus.Expired);
                }
                else if (tries >= AccountDefaults.MaxCodeAttempts)
                {
                    check = new CodeCheckResult(CodeCheckStatus.TooManyAttempts);
                }
                else
                {
                    HttpContext.Session.SetInt32(ResetFakeAttemptsKey, ++tries);
                    var left = AccountDefaults.MaxCodeAttempts - tries;
                    check = left > 0
                        ? new CodeCheckResult(CodeCheckStatus.Invalid, left)
                        : new CodeCheckResult(CodeCheckStatus.TooManyAttempts);
                }
            }
            else
            {
                check = await _codes.VerifyAsync(userId.Value, OneTimeCodePurpose.PasswordReset, model.Code);
            }

            if (check.Status != CodeCheckStatus.Valid)
            {
                return CodeView(email, isReset: true, new EnterCodeViewModel(), CodeErrorMessage(check));
            }

            ClearResetState();
            HttpContext.Session.SetInt32(ResetVerifiedUserIdKey, userId!.Value);
            HttpContext.Session.SetString(ResetVerifiedAtKey, DateTime.UtcNow.Ticks.ToString());
            _logger.LogInformation("Password reset code verified for user {UserId}.", userId);

            return RedirectToAction(nameof(ResetPassword));
        }

        // POST: /Account/ResendResetCode
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ResendResetCode()
        {
            var email = HttpContext.Session.GetString(ResetEmailKey);
            if (email == null) return RedirectToAction(nameof(ForgotPassword));

            var userId = HttpContext.Session.GetInt32(ResetUserIdKey);
            if (userId != null)
            {
                var user = await FindResettableUserAsync(email);
                if (user != null && user.Id == userId)
                {
                    await _codes.IssueAsync(user, OneTimeCodePurpose.PasswordReset);
                }
            }
            else
            {
                // Like a real new code: fresh tries and a fresh expiry.
                HttpContext.Session.Remove(ResetFakeAttemptsKey);
                HttpContext.Session.SetString(ResetRequestedAtKey, DateTime.UtcNow.Ticks.ToString());
            }

            TempData["CodeNotice"] = "If an account matches, a new code was sent. Only the newest code works.";
            return RedirectToAction(nameof(ResetCode));
        }

        // GET: /Account/ResetPassword
        [HttpGet]
        public async Task<IActionResult> ResetPassword()
        {
            var user = await GetVerifiedResetUserAsync();
            if (user == null) return RedirectToAction(nameof(ForgotPassword));

            return SetPasswordView(user, isReset: true, new SetPasswordViewModel());
        }

        // POST: /Account/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ResetPassword(SetPasswordViewModel model)
        {
            var user = await GetVerifiedResetUserAsync();
            if (user == null) return RedirectToAction(nameof(ForgotPassword));

            if (ModelState.IsValid && NewPasswordProblem(user, model.NewPassword) is string problem)
            {
                ModelState.AddModelError(nameof(model.NewPassword), problem);
            }
            if (!ModelState.IsValid)
            {
                return SetPasswordView(user, isReset: true, new SetPasswordViewModel());
            }

            // Proving ownership of the email also clears a login lockout.
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            await SavePasswordAsync(user, model.NewPassword);
            _logger.LogInformation("User {UserId} reset their password with an emailed code.", user.Id);

            // Not signed in automatically: they log in with the new password.
            HttpContext.Session.Clear();
            TempData["LoginNotice"] = "Your password has been changed. Please log in with your new password.";
            return RedirectToAction(nameof(Login));
        }

        // ══════════════ Change Password (signed in, profile menu) ══════════════

        // GET: /Account/ChangePassword
        [HttpGet]
        [RequireLogin]
        public IActionResult ChangePassword()
        {
            ViewData["HomeUrl"] = RoleHomeUrl(HttpContext.Session.GetString("UserRole"));
            ViewData["Succeeded"] = TempData["PasswordChanged"] is true;
            return View(new ChangePasswordViewModel());
        }

        // POST: /Account/ChangePassword
        [HttpPost]
        [RequireLogin]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            ViewData["HomeUrl"] = RoleHomeUrl(HttpContext.Session.GetString("UserRole"));

            if (!ModelState.IsValid)
            {
                return View(new ChangePasswordViewModel());
            }

            var userId = HttpContext.Session.GetInt32("UserId")!.Value;
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || !user.IsActive)
            {
                HttpContext.Session.Clear();
                return RedirectToAction(nameof(Login));
            }

            var check = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.CurrentPassword);
            if (check != PasswordVerificationResult.Success
                && check != PasswordVerificationResult.SuccessRehashNeeded)
            {
                ModelState.AddModelError(nameof(model.CurrentPassword), "Current password is incorrect.");
                return View(new ChangePasswordViewModel());
            }

            if (NewPasswordProblem(user, model.NewPassword) is string problem)
            {
                ModelState.AddModelError(nameof(model.NewPassword), problem);
                return View(new ChangePasswordViewModel());
            }

            await SavePasswordAsync(user, model.NewPassword);
            // This session stays signed in; every other one is signed out.
            HttpContext.Session.SetString(SecurityStampKey, user.SecurityStamp);
            _logger.LogInformation("User {UserId} changed their password.", user.Id);

            TempData["PasswordChanged"] = true;
            return RedirectToAction(nameof(ChangePassword));
        }

        // ═══════════════════════════════ Helpers ═══════════════════════════════

        private void SignIn(User user)
        {
            HttpContext.Session.Clear();
            // RecordsController, ReportsController, and their shared views all
            // read these same keys to decide access and layout.
            HttpContext.Session.SetString("UserRole", user.Role);
            HttpContext.Session.SetString("Username", user.FullName); // other controllers rely on this key holding FullName
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("LoginUsername", user.Username);
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetString("UserDepartment", user.Department ?? string.Empty);
            HttpContext.Session.SetString(SecurityStampKey, user.SecurityStamp);
            HttpContext.Session.SetInt32("UserId", user.Id);

            // Shown in User Management ("Last signed in ..."). One direct
            // UPDATE, so it never saves other pending changes by accident.
            var now = DateTime.UtcNow;
            _db.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdate(s => s
                    .SetProperty(u => u.LastLoginAtUtc, now)
                    .SetProperty(u => u.LastSeenAtUtc, now));

            // Close out any row Logout never got to reach — a session that
            // ended by inactivity timeout or just closing the browser, not
            // the Logout button, leaves TimeOutUtc null forever otherwise.
            // Signing back in is the clearest sign that old session is over,
            // so this is the backstop; the session store itself no longer
            // disappearing mid-use (see Program.cs, AddDistributedSqlServerCache)
            // is the actual fix for why these piled up in the first place.
            _db.TimeLogs
                .Where(t => t.UserId == user.Id && t.TimeOutUtc == null)
                .ExecuteUpdate(s => s.SetProperty(t => t.TimeOutUtc, now));

            // One row per sign-in session, for the user's own Time Logs page.
            // Logout fills in TimeOutUtc on whichever row this leaves open.
            _db.TimeLogs.Add(new TimeLog { UserId = user.Id, TimeInUtc = now });
            _db.SaveChanges();

            _logger.LogInformation("User {UserId} signed in.", user.Id);
        }

        // The pending sign-in only counts if the account is still active and
        // nothing (password reset, email change) has happened to it since.
        private async Task<User?> GetPendingUserAsync()
        {
            var id = HttpContext.Session.GetInt32(PendingUserIdKey);
            if (id == null) return null;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id.Value);
            if (user == null || !user.IsActive || user.SecurityStamp != HttpContext.Session.GetString(PendingStampKey))
            {
                HttpContext.Session.Clear();
                return null;
            }
            return user;
        }

        // Next unfinished step, or the finished sign-in.
        private IActionResult ContinuePendingSignIn(User user)
        {
            if (user.MustChangePassword)
            {
                return RedirectToAction(user.EmailConfirmed ? nameof(SetPassword) : nameof(VerifyEmail));
            }

            var returnUrl = HttpContext.Session.GetString(PendingReturnUrlKey);
            SignIn(user);
            return Redirect(returnUrl != null && Url.IsLocalUrl(returnUrl) ? returnUrl : RoleHomeUrl(user.Role));
        }

        // Only active accounts with a confirmed email can reset by email —
        // a code must never go to an address the owner hasn't proven is theirs.
        private async Task<User?> FindResettableUserAsync(string email)
        {
            var lowered = email.ToLower();
            return await _db.Users.FirstOrDefaultAsync(u =>
                u.IsActive && u.EmailConfirmed &&
                (u.Email.ToLower() == lowered || u.Username.ToLower() == lowered));
        }

        private async Task<User?> GetVerifiedResetUserAsync()
        {
            var id = HttpContext.Session.GetInt32(ResetVerifiedUserIdKey);
            var at = HttpContext.Session.GetString(ResetVerifiedAtKey);
            if (id == null || !long.TryParse(at, out var ticks)
                || DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > AccountDefaults.ResetWindow)
            {
                ClearResetState();
                return null;
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id.Value && u.IsActive);
            if (user == null) ClearResetState();
            return user;
        }

        private void ClearResetState()
        {
            HttpContext.Session.Remove(ResetEmailKey);
            HttpContext.Session.Remove(ResetUserIdKey);
            HttpContext.Session.Remove(ResetFakeAttemptsKey);
            HttpContext.Session.Remove(ResetRequestedAtKey);
            HttpContext.Session.Remove(ResetVerifiedUserIdKey);
            HttpContext.Session.Remove(ResetVerifiedAtKey);
        }

        // Saves the new password, signs the account out everywhere else (new
        // security stamp), and emails the owner that it changed.
        private async Task SavePasswordAsync(User user, string newPassword)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
            user.MustChangePassword = false;
            user.SecurityStamp = NewSecurityStamp();

            if (user.EmailConfirmed && !string.IsNullOrWhiteSpace(user.Email))
            {
                var email = EmailTemplates.PasswordChanged(user.Email, user.FullName, TimeZoneHelper.ToPhilippineTime(DateTime.UtcNow));
                _db.EmailOutbox.Add(new EmailOutboxMessage
                {
                    ToEmail = email.ToEmail,
                    ToName = email.ToName,
                    Subject = email.Subject,
                    TextBody = email.TextBody,
                    HtmlBody = email.HtmlBody
                });
            }

            await _db.SaveChangesAsync();
        }

        // Rules beyond the length check on the view model (NIST SP 800-63B:
        // a minimum length and a check against common passwords; no forced
        // symbols).
        private string? NewPasswordProblem(User user, string newPassword)
        {
            if (string.Equals(newPassword, AccountDefaults.DefaultPassword, StringComparison.OrdinalIgnoreCase))
                return "You cannot use the default password. Please choose a new one.";
            if (AccountDefaults.CommonPasswords.Contains(newPassword))
                return "This password is too common and easy to guess. Please choose another.";

            var same = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, newPassword);
            if (same is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded)
                return "New password must be different from your current password.";

            return null;
        }

        private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

        private IActionResult LoginView(LoginViewModel model, string? error)
        {
            if (error != null) ModelState.AddModelError(string.Empty, error);
            // Never send a typed password back to the browser.
            ModelState.Remove(nameof(model.Password));
            model.Password = string.Empty;
            return View(nameof(Login), model);
        }

        // codeError: show this message and empty the code box.
        private IActionResult CodeView(string email, bool isReset, EnterCodeViewModel model, string? codeError = null)
        {
            ViewData["Email"] = email;
            ViewData["IsReset"] = isReset;
            if (codeError != null)
            {
                ModelState.Remove(nameof(model.Code));
                ModelState.AddModelError(nameof(model.Code), codeError);
            }
            return View("EnterCode", model);
        }

        private IActionResult SetPasswordView(User user, bool isReset, SetPasswordViewModel model)
        {
            ViewData["IsReset"] = isReset;
            ViewData["AccountName"] = user.FullName;
            ViewData["AccountEmail"] = user.Email;
            return View("SetPassword", model);
        }

        private static string LockedMessage(TimeSpan remaining)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return $"Too many wrong passwords. Sign-in for this account is locked for {minutes} minute{(minutes == 1 ? "" : "s")}. You can also use Forgot Password.";
        }

        private static string WaitMessage(int seconds) =>
            seconds <= 90
                ? $"Please wait {seconds} seconds before asking for another code."
                : $"Too many codes were requested. Please wait {Math.Ceiling(seconds / 60.0)} minutes before asking for another code.";

        private static string CodeErrorMessage(CodeCheckResult check) => check.Status switch
        {
            CodeCheckStatus.Invalid => $"That code is incorrect. {check.AttemptsLeft} {(check.AttemptsLeft == 1 ? "try" : "tries")} left.",
            CodeCheckStatus.TooManyAttempts => "Too many wrong tries. Press \"Send a new code\" to get a new one.",
            _ => "This code has expired or was already used. Press \"Send a new code\" to get a new one."
        };

        // Route by role — SuperAdmin gets its own dashboard, distinct from Admin
        private string RoleHomeUrl(string? role)
        {
            if (string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                return Url.Action("SuperAdminDashboard", "Dashboard")!;
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                return Url.Action("AdminDashboard", "Dashboard")!;
            return Url.Action("Index", "Employee")!;
        }
    }
}
