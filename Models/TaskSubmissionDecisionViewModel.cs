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
    }
}