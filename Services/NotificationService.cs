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
                Message = message,
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
                .Where(n => n.RecipientUserId == userId)
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
                _db.Notifications.Remove(notif);
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
    }
}