using DTIOneLink.Services;

namespace DTIOneLink.Models
{
    // The dashboards' "Records Overview" card (Views/Shared/_RecordsOverview).
    public class RecordsOverviewViewModel
    {
        // Who the totals cover, e.g. "Planning Department" or "Whole office".
        public string ScopeLabel { get; set; } = string.Empty;

        // Null when the totals couldn't be loaded.
        public RecordsSummaryService.RecordsSummary? Summary { get; set; }
    }
}
