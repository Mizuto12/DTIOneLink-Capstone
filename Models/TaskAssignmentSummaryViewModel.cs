namespace DTIOneLink.Models
{
    // Read-only per-assignee snapshot for the Admin Edit Task sidebar.
    // Never bound from a form — display only.
    public class TaskAssignmentSummaryViewModel
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        // Already submitted proof: can't be removed (keeps their history).
        public bool HasSubmitted { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public bool IsPrimaryAssignee { get; set; }
    }
}