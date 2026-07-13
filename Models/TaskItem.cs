using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    public class TaskItem
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Task name is required.")]
        [Display(Name = "Task Name")]
        public string TaskName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Assignee is required.")]
        [Display(Name = "Assignee")]
        public string Assignee { get; set; } = string.Empty;

        [Required(ErrorMessage = "Due date is required.")]
        [Display(Name = "Due Date")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; } = DateTime.UtcNow.Date;

        [Required(ErrorMessage = "Priority is required.")]
        public string Priority { get; set; } = "medium";

        [Required(ErrorMessage = "Description is required.")]
        [Display(Name = "Task Description")]
        public string Description { get; set; } = string.Empty;

        public int Progress { get; set; } = 0;

        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
