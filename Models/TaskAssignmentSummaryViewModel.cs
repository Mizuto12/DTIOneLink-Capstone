namespace DTIOneLink.Models
{
    // Read-only per-assignee snapshot for the Admin Edit Task sidebar.
    // Never bound from a form — display only.
    public class TaskAssignmentSummaryViewModel
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        // False while their proof awaits review or once approved — the work
        // is theirs at that point, so it can't be handed to someone else.
        public bool CanReassign { get; set; }
        // Who had this assignment before it was reassigned to this person.
        public string? ReassignedFromName { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public bool IsPrimaryAssignee { get; set; }
    }
}
