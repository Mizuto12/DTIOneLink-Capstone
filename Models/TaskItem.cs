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

        // Date AND time now (previously date-only) — stored as Philippine
        // local time, same convention as before, just with a real
        // time-of-day instead of always midnight. See
        // TaskWorkflow.EffectiveDueMoment for how a pre-existing midnight
        // value (every task created before this) is still treated as "due
        // by the end of that day", not "due at its very start".
        [Required(ErrorMessage = "Due date is required.")]
        [Display(Name = "Due Date")]
        [DataType(DataType.DateTime)]
        public DateTime DueDate { get; set; } = TimeZoneHelper.PhilippineToday;

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

        // Decision 6: set by the task's creator at creation time only —
        // never editable afterward. false lets EmployeeController.SubmitProof
        // complete the assignment directly, skipping the ForReview/Admin-
        // decision step entirely (TaskWorkflow.AllowedTransitions still
        // requires InProgress specifically; a resubmission after Returned
        // for Correction always goes through review, regardless of this flag).
        public bool RequiresReview { get; set; } = true;

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

        // Set (and overwritten) whenever a submission moves this task's
        // rollup status to ForReview — EmployeeController.SubmitProof sets
        // it to SubmittedAt + TaskWorkflow.ReviewWindow. Stored/compared in
        // UTC (like SubmittedAt), unlike DueDate's Philippine-local
        // convention: it's a fixed-duration window from an exact instant,
        // not a calendar-date concept. Only meaningful while Status is
        // ForReview — TaskWorkflow.DelayRisk only reads it in that branch,
        // so a stale leftover value from a previous review cycle is never
        // acted on once the status has moved on.
        public DateTime? ReviewDueDate { get; set; }

        // Only meaningful for a subtask (ParentTaskId != null) under a Main
        // Task — its estimated relative workload, used to weight this
        // subtask's contribution to the parent's calculated progress
        // (TaskAssignmentService.RecalculateMainTaskFromSubtasks). A
        // standalone task or a Main Task itself never reads this. Same
        // authorization boundary as TaskName/Priority/Description on a
        // subtask: only the OPD (SuperAdmin) sets it, at creation or edit
        // time — see TasksController.Edit's isAssignmentOnly gate.
        [Range(TaskWorkflow.MinSubtaskWeight, TaskWorkflow.MaxSubtaskWeight,
            ErrorMessage = "Weight must be between 1 and 20.")]
        public int Weight { get; set; } = TaskWorkflow.DefaultSubtaskWeight;

        public List<TaskAssignment> Assignments { get; set; } = new();
        public List<TaskSubmission> Submissions { get; set; } = new();
        public List<TaskActivity> Activities { get; set; } = new();
        public List<TaskComment> Comments { get; set; } = new();
    }
}