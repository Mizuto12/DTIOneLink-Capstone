using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // Maps to the existing "Users" table — this is the credentials table used
    // for login, distinct from UserItems (the User Management listing page).
    public class User
    {
        public int Id { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string Department { get; set; } = string.Empty;

        // ── Account security ────────────────────────────────────
        // True once the owner has entered a code sent to Email. Recovery
        // codes and notification emails only go to confirmed addresses, so a
        // mistyped email can't hand the account (or task details) to a stranger.
        public bool EmailConfirmed { get; set; }

        // Set for new accounts (default password) and after an admin reset
        // (temporary password). The user must choose their own before going on.
        public bool MustChangePassword { get; set; }

        // Login lockout: AccountDefaults.MaxFailedLogins wrong passwords in a
        // row locks sign-in until LockoutEndUtc.
        public int FailedLoginCount { get; set; }
        public DateTime? LockoutEndUtc { get; set; }

        // Sign-in status shown in User Management (all UTC; null = never, since
        // recording started). Signed in = they logged in after their last
        // Sign Out and the app heard from them within the session timeout.
        public DateTime? LastLoginAtUtc { get; set; }   // finished signing in
        public DateTime? LastLogoutAtUtc { get; set; }  // pressed Sign Out
        public DateTime? LastSeenAtUtc { get; set; }    // last page load (Program.cs, at most once a minute)

        // Changes whenever the password (or email) changes. Every signed-in
        // session holds the value it started with; Program.cs signs out any
        // session whose value no longer matches.
        [MaxLength(64)]
        public string SecurityStamp { get; set; } = string.Empty;

        // "Remember me": lets Program.cs's per-request check silently
        // re-establish a session after the in-memory session store loses it
        // (idle timeout, app restart/deploy) without asking for the password
        // or OTP again. Only a hash is stored (AccountSessionService), the
        // same reasoning as OneTimeCode.CodeHash — the real token lives only
        // in the browser's cookie. Rotated on every successful auto sign-in;
        // one active token per user (signing in with "remember me" on a
        // second device replaces the first device's). Hashed together with
        // SecurityStamp, so a password change invalidates it automatically,
        // the same way it already signs out active sessions.
        [MaxLength(64)]
        public string? RememberTokenHash { get; set; }
        public DateTime? RememberTokenExpiresUtc { get; set; }
    }
}
