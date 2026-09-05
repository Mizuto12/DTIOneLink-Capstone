using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTIOneLink.Models
{
    // One row per proof-of-completion attempt. A task can accumulate
    // several of these over time (submit -> returned -> resubmit -> ...),
    // which is why this isn't just columns on TaskItem.
    //
    // With multi-employee assignment, a submission belongs to ONE
    // assignee's piece of work, not the task as a whole — that's what
    // TaskAssignmentId captures. TaskId is kept alongside it (redundant,
    // but harmless) so existing queries/Includes that go through
    // Task/Submissions still work unchanged.
    public class TaskSubmission
    {
        public int Id { get; set; }

        [ForeignKey(nameof(Task))]
        public int TaskId { get; set; }
        public TaskItem? Task { get; set; }

        // Nullable only so the column can exist before the data migration
        // backfills it for pre-existing rows; every new submission created
        // by SubmitProof always sets it.
        [ForeignKey(nameof(TaskAssignment))]
        public int? TaskAssignmentId { get; set; }
        public TaskAssignment? TaskAssignment { get; set; }

        public string ProofFileName { get; set; } = string.Empty;       // original name, for display
        public string ProofStoredFileName { get; set; } = string.Empty; // guid-based name on disk

        [StringLength(1000)]
        public string Remarks { get; set; } = string.Empty; // employee's remarks at submission time

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        // null = awaiting review. "approved" or "returned" once an
        // Admin has acted on it.
        public string? Decision { get; set; }

        [StringLength(1000)]
        public string? AdminRemarks { get; set; }

        public DateTime? DecidedAt { get; set; }

        [ForeignKey(nameof(ValidatedBy))]
        public int? ValidatedByUserId { get; set; }
        public User? ValidatedBy { get; set; }
    }
}