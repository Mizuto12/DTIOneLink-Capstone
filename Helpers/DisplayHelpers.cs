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

        // "3 days late" / "Due today" / "Due October 05" relative to the
        // given reference date (always TimeZoneHelper.PhilippineToday at
        // every call site). Extracted from three byte-identical copies
        // (Views/Employee/Index.cshtml, Views/Shared/_DashboardContent.cshtml,
        // Views/SuperAdmin.cshtml) found during the 2026-10-30 cross-role
        // consistency audit — correct and in sync today, but copy-pasted
        // three times rather than shared.
        public static string DueText(DateTime due, DateTime today)
        {
            var days = (due.Date - today).Days;
            return days switch
            {
                < -1 => $"{-days} days late",
                -1 => "1 day late",
                0 => "Due today",
                1 => "Due tomorrow",
                _ => "Due " + FormalMonthDay(due)
            };
        }

        // Maps a raw assignment/task status to one of TaskWorkflow's five
        // canonical values, falling back to keyword matching for older
        // free-text values ("ongoing", "done", ...) so no task drops off a
        // board. Extracted from two byte-identical copies
        // (Views/Employee/Index.cshtml's StatusOf, Views/Shared/
        // _DashboardContent.cshtml's StatusOf) found during the same audit —
        // callers resolve "whose status" (own assignment vs. the task's own)
        // before calling this, since that resolution legitimately differs
        // per page and isn't part of the duplicated logic.
        public static string DisplayStatus(string? rawStatus)
        {
            var s = DTIOneLink.Services.TaskWorkflow.Normalize(rawStatus);
            if (s is DTIOneLink.Services.TaskWorkflow.Pending or DTIOneLink.Services.TaskWorkflow.InProgress
                or DTIOneLink.Services.TaskWorkflow.ForReview or DTIOneLink.Services.TaskWorkflow.ReturnedForCorrection
                or DTIOneLink.Services.TaskWorkflow.Completed) return s;
            if (s.Contains("complet") || s == "done") return DTIOneLink.Services.TaskWorkflow.Completed;
            if (s.Contains("review")) return DTIOneLink.Services.TaskWorkflow.ForReview;
            if (s.Contains("return")) return DTIOneLink.Services.TaskWorkflow.ReturnedForCorrection;
            if (s.Contains("progress") || s.Contains("ongoing")) return DTIOneLink.Services.TaskWorkflow.InProgress;
            return DTIOneLink.Services.TaskWorkflow.Pending;
        }
    }
}