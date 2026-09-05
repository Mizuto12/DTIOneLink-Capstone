using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using DTIOneLink.Models;


namespace DTIOneLink.Services
{
    // Single source of truth for how a TaskItem's employee assignments are
    // created, changed, and rolled up into the task-level Status/Progress/
    // AssigneeId that legacy single-assignee code paths still read.
    //
    // Aggregation rule (overall TaskItem.Status), matching "the overall task
    // becomes For Review only when every assignee has completed their
    // assigned work":
    //   - every assignment Completed          -> task Completed, Progress 100
    //   - every assignment ForReview/Completed -> task ForReview (nobody left
    //                                              Pending/InProgress/Returned)
    //   - otherwise                            -> task InProgress if anyone
    //                                              has started, else Pending
    // Task Progress in the non-Completed cases is the average of the
    // assignees' individual Progress, purely for dashboard display.
    public class TaskAssignmentService
    {
        private readonly AppDbContext _context;

        public TaskAssignmentService(AppDbContext context)
        {
            _context = context;
        }

        public record SyncResult(List<int> Added, List<int> Removed, List<int> BlockedRemovals);

        // Used by Tasks/Create. task.Id must already be saved (assignments
        // need a real TaskId). The first user in the list becomes the
        // primary assignee, mirroring the old single-AssigneeId behavior for
        // any legacy reader of task.AssigneeId/task.Assignee.
        public void AssignEmployees(TaskItem task, IReadOnlyList<int> employeeUserIds, int? assignedByUserId)
        {
            var distinctIds = employeeUserIds.Distinct().ToList();

            for (var i = 0; i < distinctIds.Count; i++)
            {
                var assignment = new TaskAssignment
                {
                    TaskId = task.Id,
                    UserId = distinctIds[i],
                    AssignedByUserId = assignedByUserId,
                    AssignedAt = DateTime.UtcNow,
                    Progress = 0,
                    Status = TaskWorkflow.Pending,
                    IsPrimaryAssignee = i == 0
                };
                _context.TaskAssignments.Add(assignment);
                task.Assignments.Add(assignment);
            }

            task.AssigneeId = distinctIds[0];
        }

        // Used by Tasks/Edit. task.Assignments must already be loaded
        // (Include). Reconciles the desired assignee list against what's
        // currently there. An assignee who has already submitted proof can't
        // be silently dropped — that would orphan their submission history —
        // so that removal is blocked and reported back via BlockedRemovals
        // for the controller to surface as a validation error. Nothing is
        // removed/added in the database until the caller calls
        // SaveChangesAsync; if BlockedRemovals is non-empty the caller is
        // expected to bail out before saving.
        public async Task<SyncResult> SyncAssignmentsAsync(TaskItem task, IReadOnlyList<int> desiredUserIds, int? changedByUserId)
        {
            var desired = desiredUserIds.Distinct().ToList();
            var currentByUser = task.Assignments.ToDictionary(a => a.UserId);

            var toAdd = desired.Where(id => !currentByUser.ContainsKey(id)).ToList();
            var wantToRemove = currentByUser.Keys.Where(id => !desired.Contains(id)).ToList();

            var blocked = new List<int>();
            var toRemove = new List<int>();
            foreach (var userId in wantToRemove)
            {
                var hasSubmissions = await _context.TaskSubmissions
                    .AnyAsync(s => s.TaskAssignmentId == currentByUser[userId].Id);
                if (hasSubmissions)
                {
                    blocked.Add(userId);
                }
                else
                {
                    toRemove.Add(userId);
                }
            }

            foreach (var userId in toRemove)
            {
                _context.TaskAssignments.Remove(currentByUser[userId]);
                task.Assignments.Remove(currentByUser[userId]);
            }

            foreach (var userId in toAdd)
            {
                var assignment = new TaskAssignment
                {
                    TaskId = task.Id,
                    UserId = userId,
                    AssignedByUserId = changedByUserId,
                    AssignedAt = DateTime.UtcNow,
                    Progress = 0,
                    Status = TaskWorkflow.Pending,
                    IsPrimaryAssignee = false
                };
                _context.TaskAssignments.Add(assignment);
                task.Assignments.Add(assignment);
            }

            // Keep exactly one primary assignee; if it was removed, promote
            // whoever remains first so legacy AssigneeId reads stay valid.
            if (!task.Assignments.Any(a => a.IsPrimaryAssignee) && task.Assignments.Any())
            {
                task.Assignments.First().IsPrimaryAssignee = true;
            }

            var primary = task.Assignments.FirstOrDefault(a => a.IsPrimaryAssignee) ?? task.Assignments.FirstOrDefault();
            if (primary != null)
            {
                task.AssigneeId = primary.UserId;
            }

            return new SyncResult(toAdd, toRemove, blocked);
        }

        // Recomputes TaskItem.Status/Progress/AssigneeId from task.Assignments
        // (must be loaded). Call this after ANY assignment mutation —
        // Employee/Update, Employee/SubmitProof, Tasks/Review decision, or
        // Tasks/Create|Edit — right before SaveChangesAsync.
        public void RecalculateOverallStatus(TaskItem task)
        {
            if (!task.Assignments.Any())
            {
                return; // shouldn't happen — every task requires >= 1 assignee
            }

            var statuses = task.Assignments.Select(a => a.Status).ToList();

            if (statuses.All(s => s == TaskWorkflow.Completed))
            {
                task.Status = TaskWorkflow.Completed;
                task.Progress = 100;
            }
            else if (statuses.All(s => s == TaskWorkflow.ForReview || s == TaskWorkflow.Completed))
            {
                task.Status = TaskWorkflow.ForReview;
                task.Progress = (int)Math.Round(task.Assignments.Average(a => a.Progress));
            }
            else
            {
                task.Status = statuses.Any(s => s != TaskWorkflow.Pending)
                    ? TaskWorkflow.InProgress
                    : TaskWorkflow.Pending;
                task.Progress = (int)Math.Round(task.Assignments.Average(a => a.Progress));
            }

            var primary = task.Assignments.FirstOrDefault(a => a.IsPrimaryAssignee) ?? task.Assignments.First();
            task.AssigneeId = primary.UserId;
        }
    }
}