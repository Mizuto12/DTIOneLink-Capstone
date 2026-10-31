using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    // Task Progress in the non-Completed cases stockpiles: each assignee's
    // Progress is their contribution to the one shared piece of work, so
    // they add up (capped at 100). E.g. Bong does 10%, Elijah is added to
    // take over and does 20% -> task is 30%, not the 15% average. Whole
    // Office tasks are the exception — everyone does their own copy, so
    // there it stays the average.
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

        // Work that is awaiting review or already approved belongs to the
        // person who submitted it, so only these can be handed over.
        public static bool CanReassign(TaskAssignment assignment)
        {
            var status = TaskWorkflow.Normalize(assignment.Status);
            return status == TaskWorkflow.Pending
                || status == TaskWorkflow.InProgress
                || status == TaskWorkflow.ReturnedForCorrection;
        }

        // Hands one assignee's part of the task to someone else. The same
        // row is kept and only its owner changes, so the progress, status
        // and submission history carry over and the new person continues
        // from there. The previous person loses access (every access check
        // goes through Assignments.UserId) until it is reassigned back to
        // them. Caller validates newUserId, calls RecalculateOverallStatus
        // and saves.
        public void Reassign(TaskItem task, TaskAssignment assignment, User newUser, int? changedByUserId)
        {
            assignment.ReassignedFromUserId = assignment.UserId;
            assignment.UserId = newUser.Id;
            assignment.User = newUser;
            assignment.AssignedByUserId = changedByUserId;
            assignment.AssignedAt = DateTime.UtcNow;

            if (assignment.IsPrimaryAssignee)
            {
                task.AssigneeId = newUser.Id;
            }
        }

        // Shared progress-write path for both the assignee's own slider
        // (EmployeeController.UpdateProgress) and an authorized Admin/
        // SuperAdmin's correction of someone else's assignment
        // (TasksController.UpdateAssignmentProgress — panelist item T10).
        // Same lock rules either way: frozen once Completed or ForReview
        // (an Admin correction belongs in the Review decision for a
        // submission that's actively awaiting review, not here), and once a
        // checklist governs the assignment. The only automatic status
        // transition is Pending -> InProgress; this never reaches Completed
        // on its own — that only ever happens via TasksController.Review's
        // Approve decision. Does NOT call SaveChangesAsync — the caller
        // owns the save (and its own concurrency/audit-log handling).
        public async Task<string?> ApplyProgressChangeAsync(TaskItem task, TaskAssignment assignment, int progress, string? requestedStatus)
        {
            var currentStatus = (assignment.Status ?? "pending").Trim().ToLowerInvariant();

            if (currentStatus == TaskWorkflow.Completed)
            {
                return "This task is already completed and can no longer be updated.";
            }

            // Locked while awaiting Admin review — progress/status are frozen
            // until a decision comes back (Completed or Returned for
            // Correction). An Admin wanting to correct it mid-review uses the
            // Review decision's own Progress field instead.
            if (currentStatus == TaskWorkflow.ForReview)
            {
                return "This task is awaiting review and can't be updated right now.";
            }

            // Once a checklist governs this assignment's progress, manual
            // edits are locked out — check items off instead. Reverts
            // automatically if every item is later removed.
            if (assignment.ChecklistItems.Count > 0)
            {
                return "This task's progress is tracked by its checklist — check items off instead.";
            }

            var requested = (requestedStatus ?? currentStatus).Trim().ToLowerInvariant();
            var nextStatus = currentStatus;

            // The only manual transition allowed from here: Pending -> In Progress.
            if (currentStatus == TaskWorkflow.Pending && requested == TaskWorkflow.InProgress)
            {
                nextStatus = TaskWorkflow.InProgress;
            }

            // Moving progress off zero implies work has started, even if the
            // checkbox wasn't also ticked.
            if (progress > 0 && nextStatus == TaskWorkflow.Pending)
            {
                nextStatus = TaskWorkflow.InProgress;
            }

            // Defense in depth: reject a tampered RequestedStatus that doesn't
            // match a legal transition. Reaching Completed happens only via
            // TasksController.Review, never from here — not even for an
            // Admin setting progress to 100 (T10's own restriction).
            if (!TaskWorkflow.CanTransition(currentStatus, nextStatus) || nextStatus == TaskWorkflow.Completed)
            {
                return "That status change isn't allowed.";
            }

            assignment.Progress = progress;
            assignment.Status = nextStatus;
            assignment.RowVersion = Guid.NewGuid().ToByteArray();

            RecalculateOverallStatus(task);
            await PropagateToParentMainTaskAsync(task);

            return null;
        }

        // Writes TaskAssignment.Progress from its own checklist items'
        // IsDone/Weight — the moment an assignment has any checklist items,
        // they (not the manual progress slider) decide its Progress. A no-op
        // when the assignment has zero checklist items, so manual progress
        // stays in full control until the first item is added — and keeps
        // working again if every item is later removed. Does NOT touch
        // Status or call RecalculateOverallStatus itself; callers do that
        // afterward, same as every other assignment mutation in this class.
        public static void RecalculateProgressFromChecklist(TaskAssignment assignment)
        {
            if (assignment.ChecklistItems.Count == 0)
            {
                return;
            }

            var totalWeight = assignment.ChecklistItems.Sum(c => c.Weight);
            var doneWeight = assignment.ChecklistItems.Where(c => c.IsDone).Sum(c => c.Weight);
            assignment.Progress = totalWeight <= 0 ? 0 : (int)Math.Round(100.0 * doneWeight / totalWeight);
        }

        // Recomputes TaskItem.Status/Progress/AssigneeId from task.Assignments
        // (must be loaded). Call this after ANY assignment mutation —
        // Employee/UpdateProgress, Employee/SubmitProof, Tasks/Review
        // decision, or Tasks/Create|Edit — right before SaveChangesAsync.
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
                task.Progress = CombinedProgress(task);
            }
            else
            {
                task.Status = statuses.Any(s => s != TaskWorkflow.Pending)
                    ? TaskWorkflow.InProgress
                    : TaskWorkflow.Pending;
                task.Progress = CombinedProgress(task);
            }

            var primary = task.Assignments.FirstOrDefault(a => a.IsPrimaryAssignee) ?? task.Assignments.First();
            task.AssigneeId = primary.UserId;
        }

        private static int CombinedProgress(TaskItem task) =>
            task.TaskType == TaskTypes.WholeOffice
                ? (int)Math.Round(task.Assignments.Average(a => a.Progress))
                : Math.Min(100, task.Assignments.Sum(a => a.Progress));

        // Recomputes a Main Task's own Status/Progress purely from its child
        // subtasks' Status/Progress — the same rollup pattern as
        // RecalculateOverallStatus above, one level higher (subtask -> main
        // task instead of assignment -> task). Every subtask under a main
        // task is treated as required; there's no "optional subtask" concept
        // in this model. Call with mainTask.Subtasks already loaded. Only
        // ever meaningful for TaskLevel == Main — a standalone task
        // (TaskLevel == Subtask, ParentTaskId == null) never reaches this
        // and keeps using the assignment-based rollup exclusively.
        public void RecalculateMainTaskFromSubtasks(TaskItem mainTask)
        {
            if (mainTask.TaskLevel != TaskLevels.Main)
            {
                return; // safety guard — never meant to run on anything else
            }

            var subtasks = mainTask.Subtasks;
            if (subtasks == null || subtasks.Count == 0)
            {
                return; // no subtasks yet — leave Status/Progress untouched
            }

            var statuses = subtasks.Select(s => TaskWorkflow.Normalize(s.Status)).ToList();

            if (statuses.All(s => s == TaskWorkflow.Completed))
            {
                mainTask.Status = TaskWorkflow.Completed;
                mainTask.Progress = 100; // trivially also what WeightedProgress would give here
            }
            else if (statuses.All(s => s == TaskWorkflow.ForReview || s == TaskWorkflow.Completed))
            {
                // "All required subtasks completed or validated" — every
                // subtask has at least been submitted for review (or fully
                // validated), so the directive as a whole is ready for OPD
                // to review, even if not every subtask is individually
                // signed off yet.
                mainTask.Status = TaskWorkflow.ForReview;
                mainTask.Progress = WeightedProgress(subtasks);
            }
            else
            {
                mainTask.Status = statuses.Any(s => s != TaskWorkflow.Pending)
                    ? TaskWorkflow.InProgress
                    : TaskWorkflow.Pending;
                mainTask.Progress = WeightedProgress(subtasks);
            }
        }

        // Percentage-contribution model (2026-10-31 redesign): each
        // subtask's Weight is a literal percentage of the parent's overall
        // progress, not a relative share normalized against siblings —
        // Sum(Progress% * Weight / 100), not divided by Sum(Weight).
        // TasksController enforces that one Main Task's subtask weights
        // never sum past 100 (TaskWorkflow.MaxTotalSubtaskWeight), so this
        // can only exceed 100 through corrupted/historical data — Math.Min
        // guards that defensively rather than trusting the invariant blindly.
        // Each subtask's Progress is already its own fully-resolved
        // authoritative value by the time it reaches here —
        // RecalculateOverallStatus has already combined that subtask's own
        // assignees (including the Whole Office averaging rule) into this
        // single number, so a multi-assignee subtask is never double-counted
        // or arbitrarily picked from. Math.Max(0, ...) on each weight is
        // defense-in-depth against a negative historical value ever
        // contributing a negative share — [Range(1, 100)] on TaskItem.Weight
        // should make this unreachable in practice.
        private static int WeightedProgress(List<TaskItem> subtasks)
        {
            var contribution = subtasks.Sum(s => s.Progress / 100.0 * Math.Max(0, s.Weight));
            return (int)Math.Min(100, Math.Round(contribution));
        }

        // Called after any subtask's own RecalculateOverallStatus, so a
        // change to one subtask's status/progress propagates up to its
        // parent Main Task in the same save. No-op for a standalone task
        // (ParentTaskId == null) — this is what keeps existing standalone
        // tasks behaving exactly as before.
        public async Task PropagateToParentMainTaskAsync(TaskItem subtask)
        {
            if (!subtask.ParentTaskId.HasValue)
            {
                return;
            }

            await RecalculateMainTaskAsync(subtask.ParentTaskId.Value);
        }

        // Recomputes a Main Task from its subtasks as currently stored — also
        // used after a subtask is deleted, when there's no subtask left to
        // pass in. Caller saves.
        public async Task RecalculateMainTaskAsync(int mainTaskId)
        {
            var mainTask = await _context.TaskItems
                .Include(t => t.Subtasks)
                .FirstOrDefaultAsync(t => t.Id == mainTaskId && t.TaskLevel == TaskLevels.Main);

            if (mainTask == null)
            {
                return; // shouldn't happen — the parent was verified to exist and be Main at subtask-creation time
            }

            RecalculateMainTaskFromSubtasks(mainTask);
        }

        // Permanently deletes the given tasks (all in one transaction) with
        // their assignments, proof submissions, comments, activity history,
        // and notifications; recalculates any OPD directive a deleted
        // subtask belonged to; then removes the proof files from disk.
        // Shared by EmployeeController.Delete/DeleteAllCompleted (an
        // employee tidying up their own completed work) and
        // TasksController.Delete (an Admin/SuperAdmin removing a task or
        // directive they manage) — same cascading cleanup either way, only
        // the eligibility rules differ per caller. Callers must have already
        // checked those rules and loaded Assignments, Submissions,
        // Activities, and Comments (tracked) on every task.
        public async Task PermanentlyDeleteTasksAsync(IReadOnlyCollection<TaskItem> tasks, int deletedByUserId, ILogger logger)
        {
            var taskIds = tasks.Select(t => t.Id).ToList();
            var parentTaskIds = tasks
                .Where(t => t.ParentTaskId.HasValue)
                .Select(t => t.ParentTaskId!.Value)
                .Distinct()
                .ToList();
            var proofFiles = tasks
                .SelectMany(t => t.Submissions.Select(s => new { TaskId = t.Id, File = s.ProofStoredFileName }))
                .Where(f => !string.IsNullOrWhiteSpace(f.File))
                .ToList();
            var deletedNames = tasks.Select(t => new { t.Id, t.TaskName }).ToList();

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                // Notifications only lose their task link (SET NULL) on delete,
                // which would leave messages pointing at a task that no longer
                // exists. Load-then-RemoveRange rather than ExecuteDeleteAsync,
                // which the EF Core InMemory provider (used by the unit test
                // suite) can't translate — this stays portable across both.
                var relatedNotifications = await _context.Notifications
                    .Where(n => n.RelatedTaskId.HasValue && taskIds.Contains(n.RelatedTaskId.Value))
                    .ToListAsync();
                _context.Notifications.RemoveRange(relatedNotifications);

                // Children first, explicitly: activities reference submissions
                // and submissions reference assignments (both restricted), so
                // EF deletes them in dependency order before the tasks themselves.
                foreach (var task in tasks)
                {
                    _context.TaskActivities.RemoveRange(task.Activities);
                    _context.TaskComments.RemoveRange(task.Comments);
                    _context.TaskSubmissions.RemoveRange(task.Submissions);
                    _context.TaskAssignments.RemoveRange(task.Assignments);
                    _context.TaskItems.Remove(task);
                }
                await _context.SaveChangesAsync();

                // A deleted subtask no longer counts toward its OPD directive,
                // so recompute each affected directive from what remains.
                foreach (var parentId in parentTaskIds)
                {
                    await RecalculateMainTaskAsync(parentId);
                }
                if (parentTaskIds.Count > 0)
                {
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }

            // Files go only after the database commit, so a failed delete
            // never leaves submissions pointing at missing files. Best
            // effort: a file that can't be removed is logged, not shown to
            // the user.
            var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ProofUploads");
            foreach (var proof in proofFiles)
            {
                try
                {
                    var fullPath = Path.Combine(storageRoot, Path.GetFileName(proof.File));
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not delete proof file {File} for deleted task {TaskId}.", proof.File, proof.TaskId);
                }
            }

            // The tasks' own activity history is gone, so the server log is
            // the remaining record of who deleted what.
            foreach (var deleted in deletedNames)
            {
                logger.LogInformation("Task {TaskId} \"{TaskName}\" was permanently deleted by user {UserId}.",
                    deleted.Id, deleted.TaskName, deletedByUserId);
            }
        }
    }
}