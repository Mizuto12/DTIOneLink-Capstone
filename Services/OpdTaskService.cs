using DTIOneLink.Data;
using DTIOneLink.Models;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services
{
    // Creates an OPD (SuperAdmin) main task — a Direct Admin Task or a
    // Department Directive — with its assignment, subtasks, notifications,
    // and activity log. Shared by TasksController.Create and the repeating-
    // task job, so a repeated copy is given out exactly like a new task.
    // Callers validate the input first (type, department, responsible Admin).
    public class OpdTaskService
    {
        private readonly AppDbContext _context;
        private readonly TaskAssignmentService _taskAssignments;
        private readonly NotificationService _notifications;

        public OpdTaskService(AppDbContext context, TaskAssignmentService taskAssignments, NotificationService notifications)
        {
            _context = context;
            _taskAssignments = taskAssignments;
            _notifications = notifications;
        }

        public record NewOpdTask(
            string TaskName,
            string Description,
            DateTime DueDate,
            string Priority,
            string TaskType,
            string OwningDepartment,
            int? ResponsibleAdminUserId, // null for a Whole Office task
            IReadOnlyList<string> SubtaskNames,
            string? Recurrence,
            bool RequiresReview = true,
            // Parallel to SubtaskNames by position. Missing/short/null falls
            // back to TaskWorkflow.DefaultSubtaskWeight per entry — the
            // caller (TasksController.Create) is the one that actually
            // rejects an out-of-range value; this is paired up, not
            // re-validated.
            IReadOnlyList<int>? SubtaskWeights = null,
            // TaskTypes.DirectAdmin only. The full set of Admins who each
            // get their own TaskAssignment. Null/empty falls back to just
            // ResponsibleAdminUserId, so the recurring-task job and any
            // other caller that only ever had one admin keep working
            // unchanged.
            IReadOnlyList<int>? DirectAdminAssigneeIds = null);

        // activityNote: what the activity log says about how it was created
        // (e.g. "OPD created this Department Directive." or "Repeated
        // automatically from TASK-0012.").
        public async Task<(TaskItem MainTask, int SubtaskCount)> CreateAsync(
            NewOpdTask spec, int? createdByUserId, string activityNote)
        {
            var mainTask = new TaskItem
            {
                TaskName = spec.TaskName,
                DueDate = spec.DueDate,
                Priority = spec.Priority,
                Description = spec.Description,
                CreatedByUserId = createdByUserId,
                TaskLevel = TaskLevels.Main,
                TaskType = spec.TaskType,
                OwningDepartment = spec.OwningDepartment,
                ResponsibleAdminUserId = spec.ResponsibleAdminUserId,
                ParentTaskId = null,
                Recurrence = spec.Recurrence,
                RequiresReview = spec.RequiresReview
            };

            _context.TaskItems.Add(mainTask);
            await _context.SaveChangesAsync(); // need mainTask.Id before attaching assignments/subtasks

            if (mainTask.TaskType == TaskTypes.WholeOffice)
            {
                // Everyone currently active in the office except the OPD
                // (Super Admin) gets their own assignment, so each person
                // works and submits it through their normal task board.
                var staff = await _context.Users
                    .Where(u => u.IsActive && (u.Role == "Admin" || u.Role == "Employee"))
                    .OrderBy(u => u.FullName)
                    .Select(u => u.Id)
                    .ToListAsync();

                if (staff.Count > 0)
                {
                    _taskAssignments.AssignEmployees(mainTask, staff, createdByUserId);
                    await _context.SaveChangesAsync();
                }

                // Batched into one recipient lookup + one save instead of up
                // to 2 round trips PER staff member — this is the one case
                // in the app where "per recipient" can mean the entire
                // office's headcount at once. Message/link/subject match
                // NotifyTaskAssignedAsync exactly, just issued as a batch.
                await _notifications.CreateManyAsync(staff
                    .Select(userId => new NotificationService.NotificationSpec(
                        userId, NotificationType.Task, $"You've been assigned: \"{mainTask.TaskName}\"",
                        RelatedTaskId: mainTask.Id, Link: $"/Employee/Details/{mainTask.Id}",
                        EmailSubject: "You have a new task in DTI OneLink"))
                    .ToList());

                if (createdByUserId.HasValue)
                {
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, "created", activityNote);
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, TaskActivityType.Assigned,
                        $"Given to the whole office ({staff.Count} people).");
                    await _context.SaveChangesAsync();
                }

                return (mainTask, 0);
            }

            if (mainTask.TaskType == TaskTypes.DirectAdmin)
            {
                // One or more Admins, each via the existing assignment
                // system — gives every one of them a normal TaskAssignment
                // row to work through Employee/UpdateProgress and
                // Employee/SubmitProof, same as any employee task. Falls
                // back to just the (singular) Responsible Admin when the
                // caller never passed a list, e.g. the recurring-task job
                // recreating an older, single-admin copy. No subtasks are
                // ever attached to a Direct Admin Task.
                var assigneeIds = (spec.DirectAdminAssigneeIds != null && spec.DirectAdminAssigneeIds.Count > 0)
                    ? spec.DirectAdminAssigneeIds.Distinct().ToList()
                    : new List<int> { spec.ResponsibleAdminUserId!.Value };
                _taskAssignments.AssignEmployees(mainTask, assigneeIds, createdByUserId);
                await _context.SaveChangesAsync();

                var admins = await _context.Users
                    .Where(u => assigneeIds.Contains(u.Id))
                    .OrderBy(u => u.FullName)
                    .ToListAsync();
                var adminNames = admins.Count > 0
                    ? string.Join(", ", admins.Select(a => a.FullName))
                    : "the responsible Admin";

                await _notifications.CreateManyAsync(assigneeIds
                    .Select(userId => new NotificationService.NotificationSpec(
                        userId, NotificationType.Task, $"You've been assigned: \"{mainTask.TaskName}\"",
                        RelatedTaskId: mainTask.Id, Link: $"/Employee/Details/{mainTask.Id}",
                        EmailSubject: "You have a new task in DTI OneLink"))
                    .ToList());

                if (createdByUserId.HasValue)
                {
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, "created", activityNote);
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, TaskActivityType.Assigned,
                        $"Directly assigned to {adminNames}.");
                    await _context.SaveChangesAsync();
                }

                return (mainTask, 0);
            }

            // Department Directive: no TaskAssignment rows created here —
            // the receiving Admin assigns employees afterward. Each non-blank
            // subtask name becomes its own employee-level task parented to
            // this Main Task, left unassigned on purpose. Weight is paired up
            // by position BEFORE blank names are filtered out, so a blank
            // row in the middle of the form can't shift every later
            // subtask's weight onto the wrong one.
            var subtaskWeights = spec.SubtaskWeights ?? new List<int>();
            var subtaskEntries = spec.SubtaskNames
                .Select((n, i) => (
                    Name: n?.Trim(),
                    // Clamped, not trusted outright — TasksController.Create
                    // is the actual reject-on-invalid gate; this is only a
                    // defense-in-depth backstop, same pattern AddChecklistItem
                    // already uses for its own Weight.
                    Weight: Math.Clamp(
                        i < subtaskWeights.Count ? subtaskWeights[i] : TaskWorkflow.DefaultSubtaskWeight,
                        TaskWorkflow.MinSubtaskWeight, TaskWorkflow.MaxSubtaskWeight)))
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .ToList();

            foreach (var entry in subtaskEntries)
            {
                _context.TaskItems.Add(new TaskItem
                {
                    TaskName = entry.Name!,
                    DueDate = mainTask.DueDate,
                    Priority = mainTask.Priority,
                    Description = $"Subtask of: {mainTask.TaskName}",
                    CreatedByUserId = createdByUserId,
                    TaskLevel = TaskLevels.Subtask,
                    // Inherited from the parent Main Task, not left null —
                    // OwningDepartment must be populated on every task now,
                    // this is the security boundary Review checks against.
                    OwningDepartment = mainTask.OwningDepartment,
                    ResponsibleAdminUserId = null,
                    ParentTaskId = mainTask.Id,
                    RequiresReview = mainTask.RequiresReview,
                    Weight = entry.Weight
                });
            }

            await _context.SaveChangesAsync();

            if (createdByUserId.HasValue)
            {
                TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, "created", activityNote);
                await _context.SaveChangesAsync();
            }

            await _notifications.NotifyAdminDirectiveReceivedAsync(spec.ResponsibleAdminUserId!.Value, mainTask.Id, mainTask.TaskName);

            return (mainTask, subtaskEntries.Count);
        }

        // Adds one more subtask to an already-created Department Directive —
        // the directive doesn't have to be re-created just to break out
        // another piece of work. Left unassigned, same as a subtask created
        // at CreateAsync time; the receiving Admin assigns it afterward.
        public async Task<TaskItem> AddSubtaskAsync(TaskItem mainTask, string subtaskName, int? createdByUserId,
            bool requiresReview = true, int weight = TaskWorkflow.DefaultSubtaskWeight)
        {
            var subtask = new TaskItem
            {
                TaskName = subtaskName,
                DueDate = mainTask.DueDate,
                Priority = mainTask.Priority,
                Description = $"Subtask of: {mainTask.TaskName}",
                CreatedByUserId = createdByUserId,
                TaskLevel = TaskLevels.Subtask,
                OwningDepartment = mainTask.OwningDepartment,
                ResponsibleAdminUserId = null,
                ParentTaskId = mainTask.Id,
                RequiresReview = requiresReview,
                // Clamped, not trusted outright — the controller is the
                // actual reject-on-invalid gate (same backstop pattern as
                // CreateAsync's own subtask weights above).
                Weight = Math.Clamp(weight, TaskWorkflow.MinSubtaskWeight, TaskWorkflow.MaxSubtaskWeight)
            };

            _context.TaskItems.Add(subtask);
            await _context.SaveChangesAsync();

            // A fresh subtask starts Pending — if the directive had already
            // reached Completed/ForReview (every subtask that existed before
            // this one was done or submitted), it needs to drop back to
            // reflect the new unstarted work now under it. Without this, the
            // Main Task kept whatever status it had before this subtask
            // existed, so a directive could still show "Completed" while an
            // unstarted subtask sat underneath it.
            await _taskAssignments.RecalculateMainTaskAsync(mainTask.Id);
            await _context.SaveChangesAsync();

            if (createdByUserId.HasValue)
            {
                TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, "updated", $"Added a new subtask: {subtaskName}.");
                await _context.SaveChangesAsync();
            }

            return subtask;
        }
    }
}
