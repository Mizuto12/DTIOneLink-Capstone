using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // Deliberately narrow, mirroring TaskProgressUpdateViewModel's approach:
    // only the fields Admin/Supervisor are allowed to change here. Progress
    // and Status are intentionally absent at the task level — those are now
    // per-assignee (TaskAssignment.Progress/Status), owned by the
    // employee-facing Update/SubmitProof flow and the Admin Review decision,
    // never by this form.
    //
    // AssigneeId (singular) has become AssigneeIds (list) — same
    // "at least one assignee" rule as TaskCreateViewModel, enforced in the
    // controller rather than via [Required] on the list itself.
    public class TaskEditViewModel
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "Task name is required.")]
        [Display(Name = "Task Name")]
        [StringLength(200)]
        public string TaskName { get; set; } = string.Empty;

        [Display(Name = "Assignees")]
        public List<int> AssigneeIds { get; set; } = new();

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