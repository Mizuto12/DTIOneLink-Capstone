using DTIOneLink.Services;
using Microsoft.AspNetCore.Mvc;

namespace DTIOneLink.Controllers
{
    // Live updates for open pages (wwwroot/js/live-refresh.js).
    //
    // Changes are pushed through SignalR (LiveHub / LiveChangeBroadcaster).
    // This endpoint gives a page the fingerprints it was built from, so it
    // can tell a real change from its own save, and serves as the fallback
    // when a SignalR connection can't be made.
    public class LiveController : Controller
    {
        private readonly LiveVersionService _versions;

        public LiveController(LiveVersionService versions)
        {
            _versions = versions;
        }

        // GET: /Live/Version
        [HttpGet]
        public async Task<IActionResult> Version()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return StatusCode(StatusCodes.Status401Unauthorized);
            }

            var (data, notifications) = await _versions.GetAsync(userId.Value, HttpContext.RequestAborted);
            notifications.TryGetValue(userId.Value, out var mine);

            Response.Headers.CacheControl = "no-store";
            return Json(new { data, notifications = mine ?? "" });
        }
    }
}
