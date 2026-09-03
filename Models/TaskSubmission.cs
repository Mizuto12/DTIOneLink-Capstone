using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTIOneLink.Models
{
    // One row per proof-of-completion attempt. A task can accumulate
    // several of these over time (submit -> returned -> resubmit -> ...),
    // which is why this isn't just columns on TaskItem.
    public class TaskSubmission
    {
        public int Id { get; set; }

        [ForeignKey(nameof(Task))]
        public int TaskId { get; set; }
        public TaskItem? Task { get; set; }

        public string ProofFileName { get; set; } = string.Empty;       // original name, for display
        public string ProofStoredFileName { get; set; } = string.Empty; // guid-based name on disk

        [StringLength(1000)]
        public string Remarks { get; set; } = string.Empty; // employee's remarks at submission time

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

        // null = awaiting review. "approved" or "returned" once an
        // Admin has acted on it (that write path is separate, not built here).
        public string? Decision { get; set; }

        [StringLength(1000)]
        public string? AdminRemarks { get; set; }

        public DateTime? DecidedAt { get; set; }

        // Who made the Approve/Return decision — the Admin/Supervisor account,
        // not the employee who submitted the proof. Nullable because it's only
        // ever set once a decision exists; pending submissions have this null.
        [ForeignKey(nameof(ValidatedBy))]
        public int? ValidatedByUserId { get; set; }
        public User? ValidatedBy { get; set; }
    }
}