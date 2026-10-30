namespace DTIOneLink.Security
{
    public static class AccountDefaults
    {
        public const int MinPasswordLength = 8;

        // Login lockout (NIST SP 800-63B rate limiting).
        public const int MaxFailedLogins = 5;
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        // Emailed codes (OWASP Forgot Password Cheat Sheet).
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
        public const int MaxCodeAttempts = 5;
        public static readonly TimeSpan CodeResendDelay = TimeSpan.FromSeconds(60);
        public const int MaxCodesPerWindow = 3;
        public static readonly TimeSpan CodeWindow = TimeSpan.FromMinutes(15);

        // How long after entering a correct reset code the new password
        // must be saved.
        public static readonly TimeSpan ResetWindow = TimeSpan.FromMinutes(10);

        // Password reset emails a link instead of a code — a link sits in an
        // inbox longer before it's clicked, so it gets a more generous window
        // than CodeLifetime.
        public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromMinutes(30);

        // A brand-new account's activation link. Longer than a password
        // reset link because there's no existing working password to fall
        // back on while it sits in an inbox — if it expires unused, an Admin
        // has to resend it (which invalidates the old one) rather than the
        // owner simply trying again a minute later.
        public static readonly TimeSpan ActivationLinkLifetime = TimeSpan.FromHours(24);

        // "Remember this device" cookie lifetime on the login-OTP step, and
        // how long the token it names stays valid for in the database — the
        // two are always set together. OTP-only on purpose: the password is
        // still required every time, this only skips the emailed code on a
        // browser the user just proved they're using. One work week: long
        // enough to actually cut daily friction, short enough to bound the
        // exposure if a device is ever lost or improperly shared, given this
        // system holds records up to "Confidential" and the office has no
        // IT staff to react quickly if that happens.
        public static readonly TimeSpan TrustedDeviceDuration = TimeSpan.FromDays(7);

        // Common passwords refused as a new password (NIST SP 800-63B asks
        // for a check against commonly used values). Compared ignoring case.
        public static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
        {
            // "dtionelink2026" was this system's old default password, from
            // before new accounts moved to the activation-link flow (no
            // password exists until the owner sets their own) — kept banned
            // here since it's still documented in old materials somewhere.
            "dtionelink2026", "dtionelink", "dtilaguna", "dtilaguna2026", "onelink2026",
            "password", "password1", "password12", "password123", "password1234", "passw0rd",
            "p@ssword", "p@ssw0rd", "12345678", "123456789", "1234567890", "0123456789",
            "87654321", "11111111", "00000000", "88888888", "12341234", "11223344",
            "qwertyui", "qwerty123", "qwertyuiop", "1q2w3e4r", "1qaz2wsx", "asdfghjk",
            "zxcvbnm1", "abcd1234", "abc12345", "abcdefgh", "iloveyou", "iloveyou1",
            "letmein1", "welcome1", "welcome123", "sunshine", "princess", "football",
            "baseball", "dragon12", "monkey12", "superman", "trustno1", "changeme",
            "admin123", "administrator", "employee", "laguna123", "philippines",
            "pilipinas", "mahalkita", "manila123",
        };
    }
}
