namespace DTIOneLink.Models
{
    // Weekly completion counts for the "Completion Trend" dashboard widget —
    // how many approved submissions landed in each of the last several weeks,
    // scoped to whatever the viewer is allowed to see (their own work, their
    // division, or office-wide). A separate concept from EmployeeWorkloads
    // (SuperAdminDashboardViewModel), which is a single current-moment
    // snapshot, not a series over time.
    public class CompletionTrendViewModel
    {
        // "You", a division name, or "Office-wide" — shown next to the chart
        // so the viewer knows what it covers.
        public string ScopeLabel { get; set; } = string.Empty;

        public List<CompletionTrendWeek> Weeks { get; set; } = new();
    }

    public class CompletionTrendWeek
    {
        // Monday of the week, in the office's own time zone.
        public DateTime WeekStart { get; set; }
        public int Count { get; set; }
    }
}
