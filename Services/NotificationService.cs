using DTIOneLink.Data;
using DTIOneLink.Models;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services
{
    public class NotificationService
    {
        private readonly AppDbContext _db;

        public NotificationService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Notification> CreateAsync(
            int recipientUserId,
            NotificationType type,
            string message,
            int? relatedTaskId = null,
            int? relatedRecordId = null,
            string? link = null)
        {
            var notif = new Notification
            {
                RecipientUserId = recipientUserId,
                Type = type,
                Message = message.Length > 500 ? message.Substring(0, 497) + "..." : message,
                RelatedTaskId = relatedTaskId,
                RelatedRecordId = relatedRecordId,
                Link = link
            };

            _db.Notifications.Add(notif);
            await _db.SaveChangesAsync();
            return notif;
        }

        // Latest 30 only — enforced here, not left to the controller to remember.
        public async Task<List<Notification>> GetForUserAsync(int userId, int take = 30)
        {
            return await _db.Notifications
                .Where(n => n.RecipientUserId == userId && !n.IsDismissed)
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToListAsync();
        }

        public async Task MarkReadAsync(int notificationId, int userId)
        {
            var notif = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId);
            if (notif != null)
            {
                notif.IsRead = true;
                await _db.SaveChangesAsync();
            }
        }

        public async Task MarkAllReadAsync(int userId)
        {
            var unread = await _db.Notifications
                .Where(n => n.RecipientUserId == userId && !n.IsRead)
                .ToListAsync();
            unread.ForEach(n => n.IsRead = true);
            await _db.SaveChangesAsync();
        }

        public async Task DismissAsync(int notificationId, int userId)
        {
            var notif = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId);
            if (notif != null)
            {
                notif.IsDismissed = true;
                notif.IsRead = true;
                await _db.SaveChangesAsync();
            }
        }

        public async Task<bool> ExistsAsync(int recipientUserId, int taskId, NotificationType type)
        {
            return await _db.Notifications.AnyAsync(n =>
                n.RecipientUserId == recipientUserId &&
                n.RelatedTaskId == taskId &&
                n.Type == type);
        }

        // ── Records retention notices ───────────────────────────────
        // Counts dismissed notifications too, so dismissing the reminder
        // doesn't make it come back on the next check.
        public async Task<bool> RecordNotificationExistsAsync(int recipientUserId, int recordId, NotificationType type)
        {
            return await _db.Notifications.AnyAsync(n =>
                n.RecipientUserId == recipientUserId &&
                n.RelatedRecordId == recordId &&
                n.Type == type);
        }

        // Sent once per record to the person who logged it (the owner) when
        // its retention period ends within the reminder window — or has
        // already ended, e.g. a record logged with a short retention.
        public async Task NotifyRecordDisposalDueAsync(
            int ownerUserId, int recordId, string code, string title, DateTime retentionDueDate, DateTime today)
        {
            if (await RecordNotificationExistsAsync(ownerUserId, recordId, NotificationType.RecordDisposalDue)) return;

            var shortTitle = title.Length > 150 ? title.Substring(0, 147) + "..." : title;
            var dueText = retentionDueDate.ToString("MMMM d, yyyy");
            var message = retentionDueDate.Date > today.Date
                ? $"Record \"{code} – {shortTitle}\" reaches the end of its retention period on {dueText}. Please review it for disposal."
                : $"Record \"{code} – {shortTitle}\" reached the end of its retention period on {dueText}. Please review it for disposal.";

            await CreateAsync(
                recipientUserId: ownerUserId,
                type: NotificationType.RecordDisposalDue,
                message: message,
                relatedRecordId: recordId,
                link: "/Records");
        }

        // ── Task assignment/change notices ──────────────────────────
        // All employee-facing links point at /Employee/Details/{taskId} —
        // TaskManagement doesn't read a taskId, Details does.

        public async Task NotifyTaskAssignedAsync(int assigneeUserId, int taskId, string taskTitle)
        {
            await CreateAsync(
                recipientUserId: assigneeUserId,
                type: NotificationType.Task,
                message: $"You've been assigned: \"{taskTitle}\"",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }

        public async Task NotifyTaskReassignedAsync(int newAssigneeUserId, int taskId, string taskTitle)
        {
            await CreateAsync(
                recipientUserId: newAssigneeUserId,
                type: NotificationType.Task,
                message: $"Task reassigned to you: \"{taskTitle}\"",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }

        public async Task NotifyTaskDueDateChangedAsync(int assigneeUserId, int taskId, string taskTitle, DateTime newDueDate)
        {
            await CreateAsync(
                recipientUserId: assigneeUserId,
                type: NotificationType.Task,
                message: $"Due date changed for \"{taskTitle}\": now {newDueDate:MMM d, yyyy}",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }

        public async Task NotifyTaskPriorityChangedAsync(int assigneeUserId, int taskId, string taskTitle, string newPriority)
        {
            await CreateAsync(
                recipientUserId: assigneeUserId,
                type: NotificationType.Task,
                message: $"Priority changed for \"{taskTitle}\": now {newPriority}",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }

        // OPD decisions on a submitted proof. The recipient is whoever the
        // TaskAssignment belongs to — an Employee for ordinary/Department
        // Directive tasks, or the Responsible Admin for a Direct Admin Task —
        // so this works unchanged for both without a separate "notify admin"
        // method.
        public async Task NotifyTaskApprovedAsync(int assigneeUserId, int taskId, string taskTitle)
        {
            await CreateAsync(
                recipientUserId: assigneeUserId,
                type: NotificationType.Task,
                message: $"Your submission for \"{taskTitle}\" was approved and marked completed.",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }

        public async Task NotifyTaskReturnedAsync(int assigneeUserId, int taskId, string taskTitle, string remarks)
        {
            await CreateAsync(
                recipientUserId: assigneeUserId,
                type: NotificationType.Task,
                message: $"Your submission for \"{taskTitle}\" was returned for correction: {remarks}",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }
        // ── Due-soon / overdue reminders ─────────────────────────────
        public async Task NotifyTaskDueSoonAsync(int assigneeUserId, int taskId, string taskTitle, DateTime dueDate)
        {
            if (await ExistsAsync(assigneeUserId, taskId, NotificationType.DueSoon)) return;

            await CreateAsync(
                recipientUserId: assigneeUserId,
                type: NotificationType.DueSoon,
                message: $"Due soon: \"{taskTitle}\" is due {dueDate:MMM d, yyyy}",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }

        // isAdminCopy recipients (Admin/Supervisor/SuperAdmin) still go to the
        // admin task editor, not the employee Details page — they're not the assignee.
        public async Task NotifyTaskOverdueAsync(int recipientUserId, int taskId, string taskTitle, DateTime dueDate, bool isAdminCopy = false)
        {
            if (await ExistsAsync(recipientUserId, taskId, NotificationType.Overdue)) return;

            var message = isAdminCopy
                ? $"Overdue: \"{taskTitle}\" was due {dueDate:MMM d, yyyy} and is still incomplete"
                : $"Overdue: \"{taskTitle}\" was due {dueDate:MMM d, yyyy}";

            await CreateAsync(
                recipientUserId: recipientUserId,
                type: NotificationType.Overdue,
                message: message,
                relatedTaskId: taskId,
                link: isAdminCopy ? $"/Tasks/Edit/{taskId}" : $"/Employee/Details/{taskId}"
            );
        }
                // The removed employee can no longer open the task (Details returns
        // NotFound for non-assignees), so this links to their task list instead.
        public async Task NotifyTaskRemovedAsync(int removedUserId, int taskId, string taskTitle)
        {
            await CreateAsync(
                recipientUserId: removedUserId,
                type: NotificationType.Task,
                message: $"You've been removed from: \"{taskTitle}\"",
                relatedTaskId: taskId,
                link: "/Employee/TaskManagement"
            );
        }

        public async Task NotifyTaskCommentedAsync(int assigneeUserId, int taskId, string taskTitle, string authorName)
        {
            await CreateAsync(
                recipientUserId: assigneeUserId,
                type: NotificationType.Task,
                message: $"{authorName} commented on \"{taskTitle}\"",
                relatedTaskId: taskId,
                link: $"/Employee/Details/{taskId}"
            );
        }

        // Called when a due date changes, so the new date can trigger fresh
        // due-soon/overdue notices (ExistsAsync would otherwise block them).
        public async Task ResetDeadlineRemindersAsync(int taskId)
        {
            var stale = await _db.Notifications
                .Where(n => n.RelatedTaskId == taskId &&
                            (n.Type == NotificationType.DueSoon || n.Type == NotificationType.Overdue))
                .ToListAsync();

            if (stale.Count == 0) return;

            _db.Notifications.RemoveRange(stale);
            await _db.SaveChangesAsync();
        }
                // ── SuperAdmin / OPD notifications ───────────────────────────
        // An "OPD-issued" task is a Main Task, or a subtask under one. The
        // recipient is the creator of that Main Task, as long as they're
        // still an active SuperAdmin. Only if that can't be resolved does it
        // fall back to every active SuperAdmin (so a Direct Admin proof never
        // ends up with nobody able to review it). Anything else returns empty.
        public static List<int> ResolveOpdRecipients(TaskItem task, IReadOnlyCollection<int> activeSuperAdminIds)
        {
            var isOpdIssued = task.TaskLevel == TaskLevels.Main || task.ParentTaskId.HasValue;
            if (!isOpdIssued) return new List<int>();

            var root = task.TaskLevel == TaskLevels.Main ? task : task.ParentTask;
            var creatorId = root?.CreatedByUserId;

            if (creatorId.HasValue && activeSuperAdminIds.Contains(creatorId.Value))
            {
                return new List<int> { creatorId.Value };
            }

            return activeSuperAdminIds.ToList();
        }

        private async Task<TaskItem?> LoadTaskWithParentAsync(int taskId)
        {
            return await _db.TaskItems
                .AsNoTracking()
                .Include(t => t.ParentTask)
                .FirstOrDefaultAsync(t => t.Id == taskId);
        }

        private async Task<List<int>> GetOpdRecipientIdsAsync(TaskItem task)
        {
            var superAdminIds = await _db.Users
                .Where(u => u.IsActive && u.Role == "SuperAdmin")
                .Select(u => u.Id)
                .ToListAsync();

            return ResolveOpdRecipients(task, superAdminIds);
        }

        // Direct Admin Task only: the assigned Admin submitted proof and OPD
        // reviews it. Links to the existing Review page, which SuperAdmin is
        // authorized to open for Direct Admin submissions only.
        public async Task NotifyOpdProofSubmittedAsync(int taskId, string taskTitle, string adminName, int submissionId)
        {
            var task = await LoadTaskWithParentAsync(taskId);
            if (task == null || task.TaskType != TaskTypes.DirectAdmin) return;

            foreach (var recipientId in await GetOpdRecipientIdsAsync(task))
            {
                await CreateAsync(
                    recipientUserId: recipientId,
                    type: NotificationType.Task,
                    message: $"{adminName} submitted proof for Direct Admin Task \"{taskTitle}\" — awaiting your review",
                    relatedTaskId: taskId,
                    link: $"/Tasks/Review/{submissionId}"
                );
            }
        }

        // Fires once per recipient per directive (ExistsAsync guard). The
        // status is read from the database here rather than trusted from the
        // caller, so a call for a not-yet-completed directive does nothing.
        public async Task NotifyOpdDirectiveCompletedAsync(int mainTaskId)
        {
            var task = await LoadTaskWithParentAsync(mainTaskId);
            if (task == null
                || task.TaskLevel != TaskLevels.Main
                || task.TaskType == TaskTypes.DirectAdmin
                || task.Status != TaskWorkflow.Completed)
            {
                return;
            }

            foreach (var recipientId in await GetOpdRecipientIdsAsync(task))
            {
                if (await ExistsAsync(recipientId, task.Id, NotificationType.Completed)) continue;

                await CreateAsync(
                    recipientUserId: recipientId,
                    type: NotificationType.Completed,
                    message: $"Department directive completed: \"{task.TaskName}\"",
                    relatedTaskId: task.Id,
                    link: $"/Tasks/MainTaskDetails/{task.Id}"
                );
            }
        }

        // Admin/Supervisor comment (escalation) on an OPD-issued task. Returns
        // empty recipients for a non-OPD task, so nothing is sent for those.
        public async Task NotifyOpdTaskCommentedAsync(int taskId, string taskTitle, string authorName)
        {
            var task = await LoadTaskWithParentAsync(taskId);
            if (task == null) return;

            foreach (var recipientId in await GetOpdRecipientIdsAsync(task))
            {
                await CreateAsync(
                    recipientUserId: recipientId,
                    type: NotificationType.Task,
                    message: $"{authorName} commented on OPD task \"{taskTitle}\"",
                    relatedTaskId: taskId,
                    link: $"/Employee/Details/{taskId}"
                );
            }
        }

        // ── Admin (department-scoped) notifications ──────────────────
        // Recipients are ALWAYS resolved here, on the server, from the task's
        // effective department — never from anything the client sends.

        // Same priority order used across the controllers: the task's own
        // OwningDepartment, then the parent Main Task's, then (legacy tasks
        // only) an assignee's department. Requires task.ParentTask loaded.
        public static string? ResolveEffectiveDepartment(TaskItem task, string? legacyAssigneeDepartment)
        {
            if (!string.IsNullOrWhiteSpace(task.OwningDepartment)) return task.OwningDepartment;
            if (!string.IsNullOrWhiteSpace(task.ParentTask?.OwningDepartment)) return task.ParentTask!.OwningDepartment;
            return string.IsNullOrWhiteSpace(legacyAssigneeDepartment) ? null : legacyAssigneeDepartment;
        }

        // Active Admin/Supervisor users in the task's effective department.
        public async Task<List<int>> GetDepartmentAdminIdsAsync(int taskId)
        {
            var task = await _db.TaskItems
                .AsNoTracking()
                .Include(t => t.ParentTask)
                .Include(t => t.Assignments).ThenInclude(a => a.User)
                .FirstOrDefaultAsync(t => t.Id == taskId);

            if (task == null) return new List<int>();

            var legacyDepartment = task.Assignments
                .Select(a => a.User?.Department)
                .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));

            var department = ResolveEffectiveDepartment(task, legacyDepartment);
            if (department == null) return new List<int>();

            return await _db.Users
                .Where(u => u.IsActive
                         && (u.Role == "Admin" || u.Role == "Supervisor")
                         && u.Department == department)
                .Select(u => u.Id)
                .ToListAsync();
        }

        private async Task NotifyDepartmentAdminsAsync(int taskId, string message, string link, int? excludeUserId = null)
        {
            var adminIds = await GetDepartmentAdminIdsAsync(taskId);

            foreach (var adminId in adminIds.Where(id => id != excludeUserId))
            {
                await CreateAsync(
                    recipientUserId: adminId,
                    type: NotificationType.Task,
                    message: message,
                    relatedTaskId: taskId,
                    link: link
                );
            }
        }

        // OPD sent a Department Directive: goes to the Responsible Admin only.
        public async Task NotifyAdminDirectiveReceivedAsync(int adminUserId, int taskId, string taskTitle)
        {
            await CreateAsync(
                recipientUserId: adminUserId,
                type: NotificationType.Task,
                message: $"New department directive from OPD: \"{taskTitle}\"",
                relatedTaskId: taskId,
                link: $"/Tasks/MainTaskDetails/{taskId}"
            );
        }

        // Links to the Review page for this specific submission.
        public async Task NotifyAdminsProofSubmittedAsync(int taskId, string taskTitle, string employeeName, int submissionId)
        {
            await NotifyDepartmentAdminsAsync(
                taskId,
                $"{employeeName} submitted proof for \"{taskTitle}\" — awaiting your review",
                $"/Tasks/Review/{submissionId}");
        }

        // Comments live on the shared task detail page, so that's the link.
        public async Task NotifyAdminsTaskCommentedAsync(int taskId, string taskTitle, string authorName)
        {
            await NotifyDepartmentAdminsAsync(
                taskId,
                $"{authorName} commented on \"{taskTitle}\"",
                $"/Employee/Details/{taskId}");
        }

        public async Task NotifyAdminsOpdDueDateChangedAsync(int taskId, string taskTitle, DateTime newDueDate)
        {
            await NotifyDepartmentAdminsAsync(
                taskId,
                $"OPD changed the due date for \"{taskTitle}\": now {newDueDate:MMM d, yyyy}",
                $"/Tasks/Edit/{taskId}");
        }

        public async Task NotifyAdminsOpdPriorityChangedAsync(int taskId, string taskTitle, string newPriority)
        {
            await NotifyDepartmentAdminsAsync(
                taskId,
                $"OPD changed the priority for \"{taskTitle}\": now {newPriority}",
                $"/Tasks/Edit/{taskId}");
        }
    }
}