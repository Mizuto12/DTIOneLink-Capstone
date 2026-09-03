using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // Deliberately narrow, mirroring TaskProgressUpdateViewModel's approach:
    // only the fields Admin/Supervisor are allowed to change here. Progress,
    // Status, and CreatedAt are intentionally absent — those are owned by
    // the employee-facing Update/SubmitProof flow, not this form.
    public class TaskEditViewModel
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "Task name is required.")]
        [Display(Name = "Task Name")]
        [StringLength(200)]
        public string TaskName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Assignee is required.")]
        [Display(Name = "Assignee")]
        public int AssigneeId { get; set; }

        [Required(ErrorMessage = "Due date is required.")]
        [Display(Name = "Due Date")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        [Required(ErrorMessage = "Priority is required.")]
        public string Priority { get; set; } = "medium";

        [Required(ErrorMessage = "Description is required.")]
        [Display(Name = "Task Description")]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;
    }
}