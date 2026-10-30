using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    public class TaskSubmissionDecisionViewModel
    {
        [Required]
        public int SubmissionId { get; set; }

        // "approve" or "return" — validated against a fixed set server-side,
        // never trusted as a free-text status string.
        [Required]
        public string Decision { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? AdminRemarks { get; set; }

        // Admin/SuperAdmin can correct the assignee's self-reported
        // percentage when returning a submission for correction (e.g. the
        // employee claimed 90% but the actual work looks more like 60%).
        // Only ever applied on Return — Approve always means 100%/Completed
        // regardless of what this holds, so a stray/stale value here can
        // never silently under-complete a task.
        [Range(0, 100, ErrorMessage = "Progress must be between 0 and 100.")]
        public int? Progress { get; set; }
    }
}