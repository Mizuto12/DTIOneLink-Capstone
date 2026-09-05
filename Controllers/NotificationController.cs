using DTIOneLink.Filters;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    [RequireLogin]
    public class NotificationsController : Controller
    {
        private readonly NotificationService _notifications;

        public NotificationsController(NotificationService notifications)
        {
            _notifications = notifications;
        }

        private int CurrentUserId => HttpContext.Session.GetInt32("UserId") ?? 0;

        // Read-only — no token needed. Filters strictly by session user;
        // there is no path for one user to request another's notifications.
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var items = await _notifications.GetForUserAsync(CurrentUserId, take: 30);
            return Json(items.Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString().ToLower(),
                text = n.Message,
                time = n.CreatedAt,
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