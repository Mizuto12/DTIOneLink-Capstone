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

        // The people currently assigned (filled by the server for display;
        // never trusted from the form).
        [Display(Name = "Assignees")]
        public List<int> AssigneeIds { get; set; } = new();

        // What the form actually sends: people to add, and current
        // assignees to hand over to someone else. Nobody is ever removed
        // outright — a reassignment moves the person's assignment (progress,
        // status, history) to the new person instead.
        public List<int> AddAssigneeIds { get; set; } = new();

        // Current assignee's UserId -> the person taking over their part.
        // Posted as ReassignTo[<currentUserId>] = <newUserId>.
        public Dictionary<int, int?> ReassignTo { get; set; } = new();

        [Required(ErrorMessage = "Due date is required.")]
        [Display(Name = "Due Date")]
        [DataType(DataType.DateTime)]
        public DateTime DueDate { get; set; }

        [Required(ErrorMessage = "Priority is required.")]
        public string Priority { get; set; } = "medium";

        [Required(ErrorMessage = "Description is required.")]
        [Display(Name = "Task Description")]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        // Only meaningful (and only ever rendered/written) when this task is
        // actually a subtask of a Main Task — see the view's IsSubtask flag
        // and the controller's isAssignmentOnly gate, same as Priority above.
        // A literal percentage of the parent's progress (not a relative
        // weight) — the controller also enforces that this subtask's
        // siblings' percentages, plus this one, never sum past 100.
        [Display(Name = "% of Main Task Progress")]
        public int Weight { get; set; } = DTIOneLink.Services.TaskWorkflow.DefaultSubtaskWeight;
    }
}