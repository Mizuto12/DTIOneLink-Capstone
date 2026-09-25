using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DTIOneLink.Services;

namespace DTIOneLink.Models
{
    public class TaskItem
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Task name is required.")]
        [Display(Name = "Task Name")]
        public string TaskName { get; set; } = string.Empty;

        [Display(Name = "Assignee")]
        [ForeignKey(nameof(Assignee))]
        public int? AssigneeId { get; set; }

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

        // Null = a main task (e.g. an OPD-issued instruction). Non-null =
        // this row is a subtask of the TaskItem it points to. Same table,
        // same TaskItem shape either way — a subtask is just a TaskItem
        // whose ParentTaskId is set, not a different entity.
        [Display(Name = "Parent Task")]
        [ForeignKey(nameof(ParentTask))]
        public int? ParentTaskId { get; set; }

        public TaskItem? ParentTask { get; set; }

        public List<TaskItem> Subtasks { get; set; } = new();

        // "main" = a top-level, department-wide OPD instruction created by
        // SuperAdmin (no individual assignees; TargetDepartment set instead).
        // "subtask" = an ordinary, assignee-bearing employee-level task —
        // everything created before this feature.
        [Required]
        public string TaskLevel { get; set; } = TaskLevels.Subtask;

        // Only meaningful when TaskLevel == Main. Distinguishes a directive
        // assigned straight to the Responsible Admin (TaskTypes.DirectAdmin —
        // a TaskAssignment row is created for that Admin at creation time,
        // and they alone work the task via the normal Employee workflow)
        // from a department-wide instruction with no individual assignee yet
        // (TaskTypes.DepartmentDirective — the owning department's Admin
        // assigns employees or creates subtasks afterward). Null for every
        // ordinary Subtask-level task.
        [Display(Name = "Task Type")]
        public string? TaskType { get; set; }

        // Only meaningful when TaskLevel == Main (BDD/FAU/CPD). Null for
        // every subtask. (Renamed from TargetDepartment.)
        [Display(Name = "Owning Department")]
        public string? OwningDepartment { get; set; }

        // Only meaningful when TaskLevel == Main. The Admin-role user (a
        // Division Chief or Supervisor — both are just Role = "Admin"; no
        // separate Supervisor role is introduced here) accountable for this
        // instruction. Must belong to OwningDepartment — enforced in
        // TasksController, not by a DB constraint, since it's a cross-field
        // business rule rather than something SQL can express.
        [Display(Name = "Responsible Admin")]
        [ForeignKey(nameof(ResponsibleAdmin))]
        public int? ResponsibleAdminUserId { get; set; }

        public User? ResponsibleAdmin { get; set; }

        // Aggregate rollup across all Assignments — see
        // TaskAssignmentService.RecalculateOverallStatus for the rule.
        // Do not write to these directly for a task that has assignments.
        public int Progress { get; set; } = 0;

        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
         [Display(Name = "Created By")]
        [ForeignKey(nameof(CreatedBy))]
        public int? CreatedByUserId { get; set; }

        public User? CreatedBy { get; set; }
        // OPD main tasks only: how often this task repeats (see
        // TaskRecurrence — daily/weekly/monthly/quarterly). Kept on the
        // LATEST copy only: when the repeating-task job sends out the next
        // copy, the setting moves to that copy. Null = does not repeat.
        [MaxLength(20)]
        public string? Recurrence { get; set; }

        public List<TaskAssignment> Assignments { get; set; } = new();
        public List<TaskSubmission> Submissions { get; set; } = new();
        public List<TaskActivity> Activities { get; set; } = new();
        public List<TaskComment> Comments { get; set; } = new();
    }
}