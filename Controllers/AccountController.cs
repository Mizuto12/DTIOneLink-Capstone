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

        // A second, independent "pending" family for the login-OTP step on an
        // ordinary sign-in — deliberately not sharing PendingUserIdKey/
        // ContinuePendingSignIn with the first-time/reset password flow
        // above. Reusing those would mean GetPendingUserAsync and
        // ContinuePendingSignIn (which only ever gate on MustChangePassword)
        // could complete a sign-in before the Login OTP was ever entered.
        private const string PendingLoginUserIdKey = "PendingLoginUserId";
        private const string PendingLoginStampKey = "PendingLoginStamp";
        private const string PendingLoginReturnUrlKey = "PendingLoginReturnUrl";

        // Set only once a reset link has been clicked and verified
        // (AccountController.ResetLink) — there is no more "typed code"
        // state to track alongside it.
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
        private readonly AccountSessionService _sessions;
        private readonly IWebHostEnvironment _env;
        private readonly PasswordHasher<User> _passwordHasher = new();

        public AccountController(ILogger<AccountController> logger, AppDbContext db, OneTimeCodeService codes,
            AccountSessionService sessions, IWebHostEnvironment env)
        {
            _logger = logger;
            _db = db;
            _codes = codes;
            _sessions = sessions;
            _env = env;
        }

        // ═════════════════════════════ Login ═════════════════════════════

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Root ("/") and "/Account" both land here — someone already
            // signed in who navigates back to it (bookmark, browser back
            // button, typing the URL) gets their dashboard, not the form.
            if (HttpContext.Session.GetInt32("UserId") != null)
            {
                var home = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : RoleHomeUrl(HttpContext.Session.GetString("UserRole"));
                return Redirect(home);
            }

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

            // The integration test host (DTIOneLink.Tests) has no real inbox
            // to read a code from, the same reason Program.cs skips migrations
            // and stale-code cleanup in this one environment. Every other
            // environment (Development, Production) always goes through the
            // OTP step below.
            if (_env.IsEnvironment("Testing"))
            {
                SignIn(user);
                return Redirect(returnUrl ?? RoleHomeUrl(user.Role));
            }

            // "Remember this device" from a previous login-OTP step lets the
            // code itself be skipped on this browser — the password above is
            // still checked every time either way.
            if (_sessions.HasTrustedDevice(HttpContext, user))
            {
                SignIn(user);
                return Redirect(returnUrl ?? RoleHomeUrl(user.Role));
            }

            // Password is correct and nothing forces a password change — the
            // last step is a code emailed to confirm it's really them before
            // the session is actually established.
            HttpContext.Session.Clear();
            HttpContext.Session.SetInt32(PendingLoginUserIdKey, user.Id);
            HttpContext.Session.SetString(PendingLoginStampKey, user.SecurityStamp);
            if (returnUrl != null) HttpContext.Session.SetString(PendingLoginReturnUrlKey, returnUrl);

            var issuedLoginCode = await _codes.IssueAsync(user, OneTimeCodePurpose.Login);
            if (issuedLoginCode.Status != CodeIssueStatus.Sent)
            {
                TempData["CodeNotice"] = WaitMessage(issuedLoginCode.WaitSeconds);
            }

            return RedirectToAction(nameof(VerifyLoginCode));
        }

        // GET: /Account/VerifyLoginCode — second factor on an ordinary
        // sign-in, after the password has already been verified.
        [HttpGet]
        public async Task<IActionResult> VerifyLoginCode()
        {
            var user = await GetPendingLoginUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));

            var hasLiveCode = await _db.OneTimeCodes.AnyAsync(c =>
                c.UserId == user.Id && c.Purpose == OneTimeCodePurpose.Login &&
                c.ConsumedAtUtc == null && c.ExpiresAtUtc > DateTime.UtcNow);
            if (!hasLiveCode)
            {
                var issued = await _codes.IssueAsync(user, OneTimeCodePurpose.Login);
                if (issued.Status != CodeIssueStatus.Sent)
                {
                    TempData["CodeNotice"] = WaitMessage(issued.WaitSeconds);
                }
            }

            return CodeView(user.Email, new EnterCodeViewModel(), isLogin: true);
        }

        // POST: /Account/VerifyLoginCode
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> VerifyLoginCode(EnterCodeViewModel model)
        {
            var user = await GetPendingLoginUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
            {
                return CodeView(user.Email, model, isLogin: true);
            }

            var check = await _codes.VerifyAsync(user.Id, OneTimeCodePurpose.Login, model.Code);
            if (check.Status != CodeCheckStatus.Valid)
            {
                return CodeView(user.Email, new EnterCodeViewModel(), CodeErrorMessage(check), isLogin: true);
            }

            // Successfully verifying this code IS proof the account controls
            // that inbox — exactly what EmailConfirmed means elsewhere
            // (Forgot Password gates on it before emailing a reset link).
            // Accounts that predate the login-OTP step, or were ever created
            // with MustChangePassword already false, could otherwise never
            // set this themselves: VerifyEmail (the only other place this
            // gets set) only runs as part of a forced password change, so an
            // established account that's never been forced through one
            // would stay EmailConfirmed=false forever despite logging in
            // normally — silently blocking every notification email
            // NotificationService.CreateAsync gates on it (task assignments,
            // directive received, password-changed, record-disposal
            // reminders) while OTP emails themselves kept working fine,
            // since OtpEmailQueue never checks this flag. This self-heals
            // every affected account the next time they complete a login.
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
            }

            // Read before SignIn clears the session.
            var returnUrl = HttpContext.Session.GetString(PendingLoginReturnUrlKey);

            SignIn(user);
            if (model.RememberDevice)
            {
                await _sessions.IssueTrustedDeviceCookieAsync(HttpContext, user);
            }

            return Redirect(returnUrl != null && Url.IsLocalUrl(returnUrl) ? returnUrl : RoleHomeUrl(user.Role));
        }

        // POST: /Account/ResendLoginCode
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ResendLoginCode()
        {
            var user = await GetPendingLoginUserAsync();
            if (user == null) return RedirectToAction(nameof(Login));

            var issued = await _codes.IssueAsync(user, OneTimeCodePurpose.Login);
            TempData["CodeNotice"] = issued.Status == CodeIssueStatus.Sent
                ? "A new code was sent. Only the newest code works."
                : WaitMessage(issued.WaitSeconds);

            return RedirectToAction(nameof(VerifyLoginCode));
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

                // A trusted device's cookie is deliberately left alone here:
                // its whole point is to skip the OTP step on this browser's
                // *next* login — which still requires the password — the
                // same way other "remember this device" 2FA implementations
                // work. Logging out doesn't revoke it.
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

            return CodeView(user.Email, new EnterCodeViewModel());
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
                return CodeView(user.Email, model);
            }

            var check = await _codes.VerifyAsync(user.Id, OneTimeCodePurpose.ConfirmEmail, model.Code);
            if (check.Status != CodeCheckStatus.Valid)
            {
                return CodeView(user.Email, new EnterCodeViewModel(), CodeErrorMessage(check));
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

            var email = model.Email.Trim();
            var user = await FindResettableUserAsync(email);
            if (user != null)
            {
                // Too-soon / too-many requests send nothing, but say nothing
                // different either — same reasoning as the generic notice
                // below, just one layer deeper.
                await _codes.IssueLinkAsync(user, OneTimeCodePurpose.PasswordReset,
                    token => Url.Action(nameof(ResetLink), "Account", new { uid = user.Id, token }, Request.Scheme)!);
                _logger.LogInformation("Password reset link requested for user {UserId}.", user.Id);
            }

            // Same message whether or not the account exists, so this page
            // can't be used to find out who has one.
            TempData["ForgotPasswordNotice"] =
                "If an account with a confirmed email matches that address, we sent a password reset link to it. " +
                "Check your inbox (and Spam/Junk folder). The link is valid for 30 minutes.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        // GET: /Account/ResetLink — reached only by clicking the link emailed
        // from ForgotPassword. Verifies the token once, then hands off to the
        // ordinary ResetPassword GET/POST pair via session — exactly as if a
        // typed code had just been confirmed — so a page refresh on the set-
        // password form re-checks the session instead of re-consuming (and
        // invalidating) the one-time link in the URL.
        [HttpGet]
        [EnableRateLimiting(AuthRateLimit.Policy)]
        public async Task<IActionResult> ResetLink(int? uid, string? token)
        {
            if (uid == null || string.IsNullOrEmpty(token))
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid.Value && u.IsActive && u.EmailConfirmed);
            var check = user == null
                ? new CodeCheckResult(CodeCheckStatus.Invalid)
                : await _codes.VerifyLinkAsync(user.Id, OneTimeCodePurpose.PasswordReset, token);

            if (check.Status != CodeCheckStatus.Valid || user == null)
            {
                TempData["ForgotPasswordNotice"] = "This link is invalid, expired, or was already used. Request a new one below.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            HttpContext.Session.SetInt32(ResetVerifiedUserIdKey, user.Id);
            HttpContext.Session.SetString(ResetVerifiedAtKey, DateTime.UtcNow.Ticks.ToString());
            _logger.LogInformation("Password reset link verified for user {UserId}.", user.Id);

            return RedirectToAction(nameof(ResetPassword));
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
            // RecordsController, ReportsController, and their shared views
            // all read these same session keys to decide access and layout.
            _sessions.EstablishSession(HttpContext, user);

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

        // Same shape as GetPendingUserAsync, but for the independent
        // login-OTP "pending" family (see the PendingLogin* keys) — kept
        // separate so a mid-login-OTP session can never be completed by
        // anything that only checks MustChangePassword.
        private async Task<User?> GetPendingLoginUserAsync()
        {
            var id = HttpContext.Session.GetInt32(PendingLoginUserIdKey);
            if (id == null) return null;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id.Value);
            if (user == null || !user.IsActive || user.SecurityStamp != HttpContext.Session.GetString(PendingLoginStampKey))
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

        // Explicit project rule (overrides the earlier NIST SP 800-63B-only
        // framing, which allowed spaces for passphrases): no whitespace of
        // any kind, anywhere in the password, and no emoji/non-ASCII. Only
        // the printable ASCII range 0x21–0x7E ('!' through '~') is accepted
        // — that excludes every space/tab/newline, every control character,
        // and every emoji/non-Latin character in one check, since all of
        // those fall outside it. A bad password is refused outright, not
        // silently stripped of the offending characters.
        private static bool IsAsciiPrintableNoWhitespace(string value) =>
            value.Length > 0 && value.All(c => c is >= '!' and <= '~');

        private string? NewPasswordProblem(User user, string newPassword)
        {
            if (!IsAsciiPrintableNoWhitespace(newPassword))
                return "Password cannot contain spaces or emoji — letters, digits, and standard symbols only.";
            if (newPassword.Length < AccountDefaults.MinPasswordLength)
                return $"New password must be at least {AccountDefaults.MinPasswordLength} characters.";
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

        // codeError: show this message and empty the code box. Forgot
        // Password no longer uses this view at all — it verifies a link
        // instead of a typed code (see ResetLink) — so the only two modes
        // left are the login-OTP step (isLogin) and first-sign-in email
        // confirmation (the default).
        private IActionResult CodeView(string email, EnterCodeViewModel model, string? codeError = null, bool isLogin = false)
        {
            ViewData["Email"] = email;
            ViewData["IsLogin"] = isLogin;
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
