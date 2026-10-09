// Helpers/DisplayHelpers.cs
using System.Globalization;
using System.Linq;

namespace DTIOneLink.Helpers
{
    public static class DisplayHelpers
    {
        // "Juan Dela Cruz" -> "JD", falls back to first letter of username, then "??"
        public static string GetInitials(string? fullName, string? username)
        {
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                var parts = fullName.Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 0)
                    .Take(2)
                    .Select(w => char.ToUpper(w[0]));
                var initials = string.Concat(parts);
                if (!string.IsNullOrEmpty(initials)) return initials;
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                return char.ToUpper(username.Trim()[0]).ToString();
            }

            return "??";
        }

        // Formal date for display only: "October 05, 2026". The stored value,
        // deadlines, overdue checks and sorting are untouched (no time zone math).
        public static string FormalDate(DateTime date) =>
            date.ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture);

        // Compact form for narrow board cards: "October 05".
        public static string FormalMonthDay(DateTime date) =>
            date.ToString("MMMM dd", CultureInfo.InvariantCulture);

        public static string GetReadableRole(string? role) => role?.Trim().ToLowerInvariant() switch
        {
            "superadmin" => "System Oversight",
            "admin" => "Super User",
            "employee" => "Staff",
            _ => string.IsNullOrWhiteSpace(role) ? "User" : role!
        };
    }
}