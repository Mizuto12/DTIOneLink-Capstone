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

        // Legacy single-assignee pointer. Kept for backward compatibility with
        // any code that still reads task.AssigneeId/task.Assignee directly
        // (e.g. dashboards or reports outside this change set). For a task
        // with multiple assignees, this is a DENORMALIZED mirror of whichever
        // TaskAssignment has IsPrimaryAssignee == true, maintained exclusively
        // by TaskAssignmentService — never set it directly once Assignments
        // is in play.
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

        // Aggregate rollup across all Assignments — see
        // TaskAssignmentService.RecalculateOverallStatus for the rule.
        // Do not write to these directly for a task that has assignments.
        public int Progress { get; set; } = 0;

        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<TaskAssignment> Assignments { get; set; } = new();
        public List<TaskSubmission> Submissions { get; set; } = new();
        public List<TaskActivity> Activities { get; set; } = new();
        public List<TaskComment> Comments { get; set; } = new();
    }
}