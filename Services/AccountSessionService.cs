using System.Security.Cryptography;
using System.Text;
using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services
{
    // Two related jobs, both about keeping someone signed in without asking
    // for their password again:
    //   1. EstablishSession — the actual "write the session keys" step,
    //      shared by AccountController.SignIn (a real login) and
    //      TryAutoSignInAsync below (a silent one from a remember-me cookie),
    //      so the two paths can never drift apart on what a signed-in session
    //      actually contains.
    //   2. The "remember me" cookie itself: issue, verify, rotate, revoke.
    //      Only a hash of the token is ever stored (same reasoning as
    //      OneTimeCodeHasher) — the real token lives only in the browser's
    //      cookie, so a stolen database dump alone can't be used to sign in
    //      as anyone.
    public class AccountSessionService
    {
        public const string CookieName = "RememberMe";

        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AccountSessionService> _logger;

        public AccountSessionService(AppDbContext db, IWebHostEnvironment env, ILogger<AccountSessionService> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // The session keys every signed-in page relies on (RecordsController,
        // ReportsController, Program.cs's per-request refresh, and their
        // shared views). Does not touch TimeLogs or LastLoginAtUtc — callers
        // that represent a real, fresh sign-in (not just re-establishing a
        // lost session silently) handle those themselves immediately after.
        public void EstablishSession(HttpContext httpContext, User user)
        {
            httpContext.Session.Clear();
            httpContext.Session.SetString("UserRole", user.Role);
            httpContext.Session.SetString("Username", user.FullName);
            httpContext.Session.SetString("FullName", user.FullName);
            httpContext.Session.SetString("LoginUsername", user.Username);
            httpContext.Session.SetString("UserEmail", user.Email);
            httpContext.Session.SetString("UserDepartment", user.Department ?? string.Empty);
            httpContext.Session.SetString(AccountController.SecurityStampKey, user.SecurityStamp);
            httpContext.Session.SetInt32("UserId", user.Id);
        }

        // Call after a successful sign-in (real password entry or OTP) when
        // the "Remember me" box was checked. Replaces any earlier token for
        // this user — one active "remembered" browser per account.
        public async Task IssueRememberMeCookieAsync(HttpContext httpContext, User user)
        {
            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresUtc = DateTime.UtcNow + AccountDefaults.RememberMeDuration;

            user.RememberTokenHash = HashToken(user.Id, user.SecurityStamp, rawToken);
            user.RememberTokenExpiresUtc = expiresUtc;
            await _db.SaveChangesAsync();

            WriteCookie(httpContext, user.Id, rawToken, expiresUtc);
        }

        // Called once per request (Program.cs) when there is no active
        // session. Returns true if the cookie named a valid, unexpired token
        // for an active account — the session is then fully established, the
        // same as a real login. Rotates the token on success, both to extend
        // the "remembered" window while the browser stays in use and to limit
        // how long a copied cookie would keep working if it were ever stolen.
        public async Task<bool> TryAutoSignInAsync(HttpContext httpContext)
        {
            if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieValue) || string.IsNullOrEmpty(cookieValue))
            {
                return false;
            }

            var parts = cookieValue.Split('.', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var userId))
            {
                DeleteCookie(httpContext);
                return false;
            }
            var rawToken = parts[1];

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || !user.IsActive
                || user.RememberTokenHash == null || user.RememberTokenExpiresUtc == null
                || user.RememberTokenExpiresUtc <= DateTime.UtcNow)
            {
                DeleteCookie(httpContext);
                return false;
            }

            var candidateHash = HashToken(user.Id, user.SecurityStamp, rawToken);
            var matches = CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(candidateHash),
                Encoding.ASCII.GetBytes(user.RememberTokenHash));
            if (!matches)
            {
                // Either a forged/guessed token, or a stale cookie from before
                // a password change (SecurityStamp is baked into the hash) —
                // either way, the stored token stops being usable.
                user.RememberTokenHash = null;
                user.RememberTokenExpiresUtc = null;
                await _db.SaveChangesAsync();
                DeleteCookie(httpContext);
                return false;
            }

            EstablishSession(httpContext, user);

            var now = DateTime.UtcNow;
            user.LastLoginAtUtc = now;
            user.LastSeenAtUtc = now;

            // Rotate: a new token replaces the one just used.
            var newRawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var newExpiresUtc = now + AccountDefaults.RememberMeDuration;
            user.RememberTokenHash = HashToken(user.Id, user.SecurityStamp, newRawToken);
            user.RememberTokenExpiresUtc = newExpiresUtc;
            await _db.SaveChangesAsync();
            WriteCookie(httpContext, user.Id, newRawToken, newExpiresUtc);

            // A session that ended by inactivity timeout or an app restart,
            // not the Logout button, leaves its TimeLog row's TimeOutUtc null
            // forever otherwise — same backstop AccountController.SignIn uses
            // for a real login.
            await _db.TimeLogs
                .Where(t => t.UserId == user.Id && t.TimeOutUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeOutUtc, now));
            _db.TimeLogs.Add(new TimeLog { UserId = user.Id, TimeInUtc = now });
            await _db.SaveChangesAsync();

            _logger.LogInformation("User {UserId} auto-signed-in via a remembered browser.", user.Id);
            return true;
        }

        // Revokes the stored token (so the cookie can never be replayed, even
        // if the browser still has it) and clears the cookie itself. Called
        // on Logout — signing out of a remembered browser should actually
        // sign it out, not silently sign back in on the next page load.
        public async Task RevokeRememberMeAsync(HttpContext httpContext, int userId)
        {
            // Cookie first: the browser stops offering it even if the
            // database write below somehow fails, so a revoke is never only
            // half-applied from the browser's own point of view.
            DeleteCookie(httpContext);
            await _db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.RememberTokenHash, (string?)null)
                    .SetProperty(u => u.RememberTokenExpiresUtc, (DateTime?)null));
        }

        // Binds the token to this specific user and their current
        // SecurityStamp, the same way OneTimeCodeHasher binds a code to a
        // user and purpose — a token copied onto another account's cookie,
        // or presented after a password change, simply won't match.
        private static string HashToken(int userId, string securityStamp, string rawToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{securityStamp}:{rawToken}")));

        private void WriteCookie(HttpContext httpContext, int userId, string rawToken, DateTime expiresUtc)
        {
            httpContext.Response.Cookies.Append(CookieName, $"{userId}.{rawToken}", new CookieOptions
            {
                HttpOnly = true,
                Secure = _env.IsProduction() ? true : httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = expiresUtc,
                IsEssential = true
            });
        }

        private static void DeleteCookie(HttpContext httpContext) =>
            httpContext.Response.Cookies.Delete(CookieName);
    }
}
