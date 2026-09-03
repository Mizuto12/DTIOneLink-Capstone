using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        [ForeignKey(nameof(Assignee))]
        public int AssigneeId { get; set; }

        public User? Assignee { get; set; }

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
        public List<TaskSubmission> Submissions { get; set; } = new();
        public List<TaskActivity> Activities { get; set; } = new();
    }
}