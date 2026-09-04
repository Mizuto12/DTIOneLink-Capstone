using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTIOneLink.Models
{
    // Append-only audit log. Nothing here is ever updated or deleted after
    // being written — each row is a fact about something that happened,
    // not a piece of current state (that's what TaskItem/TaskSubmission are for).
    public class TaskActivity
    {
        public int Id { get; set; }

        [ForeignKey(nameof(Task))]
        public int TaskId { get; set; }
        public TaskItem? Task { get; set; }

        // Who performed the action — the employee for progress/submission
        // events, the Admin/Supervisor for creation/edit/reassignment/validation.
        [ForeignKey(nameof(PerformedBy))]
        public int PerformedByUserId { get; set; }
        public User? PerformedBy { get; set; }

        [Required]
        [StringLength(50)]
        public string ActivityType { get; set; } = string.Empty; // see TaskActivityType constants

[StringLength(500)]
public string Details { get; set; } = string.Empty;

public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

// Set only for proof-submitted / validated events — lets the timeline
// render a download link and remarks inline instead of just plain text.
// Null for every other activity type (created, edited, progress, etc.).
[ForeignKey(nameof(RelatedSubmission))]
public int? RelatedSubmissionId { get; set; }
public TaskSubmission? RelatedSubmission { get; set; }
    }

    // Fixed vocabulary for ActivityType, so querying/filtering by type
    // (e.g. a reports page showing only "Completed" events) isn't guessing
    // at magic strings scattered across controllers.
    public static class TaskActivityType
    {
        public const string Created = "created";
        public const string Edited = "edited";
        public const string Reassigned = "reassigned";
        public const string ProgressUpdated = "progress-updated";
        public const string StatusChanged = "status-changed";
        public const string ProofSubmitted = "proof-submitted";
        public const string Validated = "validated";
        public const string Commented = "commented";
    }
}