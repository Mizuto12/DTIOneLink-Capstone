namespace DTIOneLink.Security
{
    public static class AccountDefaults
    {
        // Every new account starts with this password. Signing in with it
        // requires an emailed code and then a new password before anything
        // else, so knowing it is not enough to use someone else's account.
        public const string DefaultPassword = "dtionelink2026";

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

        // Common passwords refused as a new password (NIST SP 800-63B asks
        // for a check against commonly used values). Compared ignoring case.
        public static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
        {
            DefaultPassword, "dtionelink", "dtilaguna", "dtilaguna2026", "onelink2026",
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
