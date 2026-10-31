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
        // TargetDepartment — validated in TasksController. Used for a
        // Department Directive's single recipient; a Direct Admin Task uses
        // DirectAdminUserIds instead (see below).
        [Display(Name = "Responsible Admin")]
        public int? ResponsibleAdminUserId { get; set; }

        // SuperAdmin + TaskTypes.DirectAdmin only. One or more active Admins
        // (any department — picking Admins across departments is the point),
        // each of whom gets their own TaskAssignment and works the task
        // themselves, same as a multi-assignee employee task. Not
        // [Required]: empty is rejected explicitly in the controller so the
        // error reads the same way AssigneeIds's does.
        [Display(Name = "Admins")]
        public List<int> DirectAdminUserIds { get; set; } = new();

        // SuperAdmin only, optional. Each non-blank entry becomes its own
        // employee-level TaskItem parented to the newly created Main Task,
        // sharing its department. Left unassigned — the target
        // department's Admin assigns employees to each one afterward via
        // Edit; SuperAdmin no longer picks assignees or a parent task
        // directly.
        [Display(Name = "Subtasks")]
        public List<string>? SubtaskNames { get; set; } = new();

        // Parallel to SubtaskNames by position (same row in the form, each
        // row submits one of each) — each subtask's literal percentage
        // share of this directive's overall progress (TaskItem.Weight).
        // Shorter than SubtaskNames, or missing entirely, just means the
        // trailing subtasks get the default weight; see OpdTaskService.
        // CreateAsync's zip. Actual range AND sum-to-100 validation happens
        // in TasksController.Create — this list is never trusted as-is.
        [Display(Name = "Subtask Weights")]
        public List<int>? SubtaskWeights { get; set; } = new();

        [Required(ErrorMessage = "Due date is required.")]
        [Display(Name = "Due Date")]
        [DataType(DataType.DateTime)]
        public DateTime DueDate { get; set; } = DTIOneLink.Services.TimeZoneHelper.PhilippineToday;

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

        // Decision 6: set by the creator here, at creation, only — never
        // editable afterward (not present on TaskEditViewModel).
        [Display(Name = "Requires review before completion")]
        public bool RequiresReview { get; set; } = true;
    }
}