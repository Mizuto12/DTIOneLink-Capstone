using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTIOneLink.Models
{
    // One checklist item belonging to a single assignee's piece of work —
    // tied to TaskAssignment, not TaskItem, so each assignee on a
    // multi-assignee task tracks their own checklist independently, the
    // same way TaskAssignment.Progress already works. Once an assignment
    // has any of these, its Progress is derived from them
    // (TaskAssignmentService.RecalculateProgressFromChecklist) instead of
    // the manual progress slider.
    public class TaskChecklistItem
    {
        public int Id { get; set; }

        [ForeignKey(nameof(TaskAssignment))]
        public int TaskAssignmentId { get; set; }
        public TaskAssignment? TaskAssignment { get; set; }

        [Required(ErrorMessage = "A checklist item needs a label.")]
        [StringLength(200)]
        public string Label { get; set; } = string.Empty;

        // Relative share of the assignment's progress this item counts for
        // (e.g. a bigger step can count double a small one). Defaults to 1
        // so an all-equal checklist just divides evenly.
        public int Weight { get; set; } = 1;

        public bool IsDone { get; set; }
        public DateTime? DoneAt { get; set; }
    }
}
