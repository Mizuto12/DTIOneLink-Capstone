using System.Security.Cryptography;
using System.Text;
using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using Microsoft.AspNetCore.Http;

namespace DTIOneLink.Services
{
    // Two related jobs:
    //   1. EstablishSession — the actual "write the session keys" step, used
    //      by every path that finishes signing someone in (AccountController.
    //      SignIn), so there's exactly one place that defines what a
    //      signed-in session contains.
    //   2. "Remember this device" on the login-OTP step: a short-lived
    //      cookie that lets a browser skip the emailed code next time, never
    //      the password. Only a hash of the token is ever stored (same
    //      reasoning as OneTimeCodeHasher) — the real token lives only in
    //      the browser's cookie, so a stolen database dump alone can't be
    //      used to skip anyone's OTP step.
    public class AccountSessionService
    {
        public const string CookieName = "TrustedDevice";

        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AccountSessionService(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // The session keys every signed-in page relies on (RecordsController,
        // ReportsController, Program.cs's per-request refresh, and their
        // shared views).
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

        // Call after the OTP is verified on the login step, when "Remember
        // this device" was checked. Replaces any earlier token for this
        // user — one trusted browser per account at a time.
        public async Task IssueTrustedDeviceCookieAsync(HttpContext httpContext, User user)
        {
            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresUtc = DateTime.UtcNow + AccountDefaults.TrustedDeviceDuration;

            user.RememberTokenHash = HashToken(user.Id, user.SecurityStamp, rawToken);
            user.RememberTokenExpiresUtc = expiresUtc;
            await _db.SaveChangesAsync();

            httpContext.Response.Cookies.Append(CookieName, $"{user.Id}.{rawToken}", new CookieOptions
            {
                HttpOnly = true,
                Secure = _env.IsProduction() ? true : httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = expiresUtc,
                IsEssential = true
            });
        }

        // Called right after the password is verified on an ordinary login,
        // before the OTP step would otherwise be issued — lets that step be
        // skipped entirely on a browser this same user trusted within the
        // last AccountDefaults.TrustedDeviceDuration. The password is still
        // required every time; this only ever shortcuts the emailed code, so
        // it deliberately does NOT sign anyone in or touch the session —
        // callers that get "true" back still call SignIn themselves.
        //
        // Synchronous and takes no database round trip of its own: `user` is
        // always the same freshly-loaded row the caller just verified the
        // password against, so its RememberTokenHash/Expires/SecurityStamp
        // are already current.
        public bool HasTrustedDevice(HttpContext httpContext, User user)
        {
            if (!httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieValue) || string.IsNullOrEmpty(cookieValue))
            {
                return false;
            }

            var parts = cookieValue.Split('.', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var cookieUserId) || cookieUserId != user.Id)
            {
                return false;
            }

            if (user.RememberTokenHash == null || user.RememberTokenExpiresUtc == null
                || user.RememberTokenExpiresUtc <= DateTime.UtcNow)
            {
                return false;
            }

            var candidateHash = HashToken(user.Id, user.SecurityStamp, parts[1]);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(candidateHash),
                Encoding.ASCII.GetBytes(user.RememberTokenHash));
        }

        // Binds the token to this specific user and their current
        // SecurityStamp, the same way OneTimeCodeHasher binds a code to a
        // user and purpose — a token copied onto another account's cookie,
        // or presented after a password change, simply won't match.
        private static string HashToken(int userId, string securityStamp, string rawToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{securityStamp}:{rawToken}")));
    }
}
