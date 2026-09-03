using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // Deliberately narrow: this is everything an employee is allowed to
    // submit from the update form. Nothing else (AssigneeId, DueDate,
    // Priority, TaskName...) is bindable here, so a tampered request
    // can't touch those fields even if extra inputs are added client-side.
    public class TaskProgressUpdateViewModel
    {
        [Required]
        public int Id { get; set; }

        [Range(0, 100, ErrorMessage = "Progress must be between 0 and 100.")]
        public int Progress { get; set; }

        // Only meaningful when the task is currently Pending — the one
        // manual transition an employee can trigger. Any other value
        // (e.g. a tampered "completed") is ignored server-side; the
        // "completed" status is always derived from Progress instead.
        public string? RequestedStatus { get; set; }

        
    }
}