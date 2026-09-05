using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DTIOneLink.Services;

namespace DTIOneLink.Models
{
    // One row per (Task, User) pairing. This is the source of truth for an
    // individual assignee's own progress/status on a task. TaskItem.Status
    // and TaskItem.Progress remain on TaskItem as an aggregate rollup
    // computed from these rows (see TaskAssignmentService.RecalculateOverallStatus)
    // — nothing should write task.Status/task.Progress directly anymore for a
    // task that has assignments; go through the service instead.
    public class TaskAssignment
    {
        public int Id { get; set; }

        [ForeignKey(nameof(Task))]
        public int TaskId { get; set; }
        public TaskItem? Task { get; set; }

        [ForeignKey(nameof(User))]
        public int UserId { get; set; }
        public User? User { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        // Who assigned this employee to the task (Admin/Supervisor). Nullable
        // because a data-migration-created row (from the old single AssigneeId)
        // has no "who assigned it" to backfill.
        [ForeignKey(nameof(AssignedBy))]
        public int? AssignedByUserId { get; set; }
        public User? AssignedBy { get; set; }

        public int Progress { get; set; } = 0;

        // Same string vocabulary as TaskWorkflow (pending / in-progress /
        // for-review / returned-for-correction / completed), but tracked
        // per-assignee instead of once per task.
        public string Status { get; set; } = TaskWorkflow.Pending;

        // Display-only convenience flag (e.g. a "Primary" badge in the
        // Admin UI, and the fallback for legacy single-assignee readers).
        // Never used to gate authorization, notifications, or the
        // ForReview/Completed aggregation rule — every assignee counts
        // equally for those.
        public bool IsPrimaryAssignee { get; set; }
    }
}