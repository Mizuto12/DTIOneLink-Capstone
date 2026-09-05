using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // Replaces binding straight to TaskItem in TasksController.Create, since
    // TaskItem.AssigneeId is a single int and we now need a list. AssigneeIds
    // is intentionally not [Required] (List<int> required doesn't reject an
    // empty-but-non-null list) — the "at least one assignee" rule is
    // enforced explicitly in the controller.
    public class TaskCreateViewModel
    {
        [Required(ErrorMessage = "Task name is required.")]
        [Display(Name = "Task Name")]
        [StringLength(200)]
        public string TaskName { get; set; } = string.Empty;

        [Display(Name = "Assignees")]
        public List<int> AssigneeIds { get; set; } = new();

        [Required(ErrorMessage = "Due date is required.")]
        [Display(Name = "Due Date")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; } = DateTime.UtcNow.Date;

        [Required(ErrorMessage = "Priority is required.")]
        public string Priority { get; set; } = "medium";

        [Required(ErrorMessage = "Description is required.")]
        [Display(Name = "Task Description")]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;
    }
}