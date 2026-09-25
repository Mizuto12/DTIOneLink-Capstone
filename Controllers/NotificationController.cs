using DTIOneLink.Filters;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    [RequireLogin]
    public class NotificationsController : Controller
    {
        private readonly NotificationService _notifications;
        private readonly RecordRetentionReminder _retentionReminder;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            NotificationService notifications,
            RecordRetentionReminder retentionReminder,
            ILogger<NotificationsController> logger)
        {
            _notifications = notifications;
            _retentionReminder = retentionReminder;
            _logger = logger;
        }

        private int CurrentUserId => HttpContext.Session.GetInt32("UserId") ?? 0;

        // Read-only — no token needed. Filters strictly by session user;
        // there is no path for one user to request another's notifications.
        [HttpGet]
        public async Task<IActionResult> List()
        {
            // Create any record-disposal reminders due for this user before
            // listing, so they appear on the next page view rather than
            // waiting for the hourly background run. Only this user's own
            // records are checked. A failure here must never break the bell.
            try
            {
                await _retentionReminder.SendDueRemindersAsync(CurrentUserId, HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Record retention check failed for user {UserId}.", CurrentUserId);
            }

            var items = await _notifications.GetForUserAsync(CurrentUserId, take: 30);
            return Json(items.Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString().ToLower(),
                text = n.Message,
                time = DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
                unread = !n.IsRead,
                link = n.Link
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            await _notifications.MarkReadAsync(id, CurrentUserId);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            await _notifications.MarkAllReadAsync(CurrentUserId);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dismiss(int id)
        {
            await _notifications.DismissAsync(id, CurrentUserId);
            return Ok();
        }
    }
}