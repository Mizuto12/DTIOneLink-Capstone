// Helpers/DisplayHelpers.cs
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

        public static string GetReadableRole(string? role) => role?.Trim().ToLowerInvariant() switch
        {
            "superadmin" => "System Oversight",
            "admin" => "Super User",
            "employee" => "Staff",
            _ => string.IsNullOrWhiteSpace(role) ? "User" : role!
        };
    }
}