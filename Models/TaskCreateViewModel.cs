using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // Replaces binding straight to TaskItem in TasksController.Create, since
    // TaskItem.AssigneeId is a single int and we now need a list. AssigneeIds
    // is intentionally not [Required] (List<int> required doesn't reject an
    // empty-but-non-null list) — the "at least one assignee" rule is
    // enforced explicitly in the controller. AssigneeIds is only used on the
    // Admin/Supervisor path (an ordinary employee-level task); SuperAdmin's
    // Create always produces a Main Task and never sets it.
    public class TaskCreateViewModel
    {
        [Required(ErrorMessage = "Task name is required.")]
        [Display(Name = "Task Name")]
        [StringLength(200)]
        public string TaskName { get; set; } = string.Empty;

        [Display(Name = "Assignees")]
        public List<int> AssigneeIds { get; set; } = new();

        // SuperAdmin only. Either TaskTypes.DirectAdmin (assigned straight
        // to the Responsible Admin, who works it themselves) or
        // TaskTypes.DepartmentDirective (sent to the department, no
        // individual assignee yet). Required when SuperAdmin submits —
        // enforced in TasksController.
        [Display(Name = "Task Type")]
        public string? TaskType { get; set; }

        // SuperAdmin only. Required when SuperAdmin is the one submitting —
        // enforced in TasksController, since which fields are required
        // depends on role, not on a form field anymore.
        [Display(Name = "Owning Department")]
        public string? OwningDepartment { get; set; }

        // SuperAdmin only. Must be an active Admin belonging to
        // TargetDepartment — validated in TasksController.
        [Display(Name = "Responsible Admin")]
        public int? ResponsibleAdminUserId { get; set; }

        // SuperAdmin only, optional. Each non-blank entry becomes its own
        // employee-level TaskItem parented to the newly created Main Task,
        // sharing its department. Left unassigned — the target
        // department's Admin assigns employees to each one afterward via
        // Edit; SuperAdmin no longer picks assignees or a parent task
        // directly.
        [Display(Name = "Subtasks")]
        public List<string>? SubtaskNames { get; set; } = new();

        [Required(ErrorMessage = "Due date is required.")]
        [Display(Name = "Due Date")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; } = DateTime.UtcNow.Date;

        [Required(ErrorMessage = "Priority is required.")]
        public string Priority { get; set; } = "medium";

        // Optional. Null/empty = does not repeat; otherwise one of
        // TaskRecurrence.All — validated in TasksController.
        [Display(Name = "Repeat")]
        public string? Recurrence { get; set; }

        [Required(ErrorMessage = "Task description is required.")]
        [Display(Name = "Task Description")]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;
    }
}