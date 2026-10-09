using DTIOneLink.Data;
using DTIOneLink.Filters;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Controllers
{
    // Shows a signed-in user their own Time In / Time Out history. No path
    // for one user to view another's — always filtered by the session user.
    [RequireLogin]
    public class TimeLogsController : Controller
    {
        private readonly AppDbContext _db;

        public TimeLogsController(AppDbContext db)
        {
            _db = db;
        }

        // from/to are calendar dates in the office's time zone (Philippine),
        // same as the date picker shows — converted to UTC bounds against
        // TimeInUtc since that's what's stored. Both optional; either can be
        // used alone (e.g. "from" only = "since this date").
        [HttpGet]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;

            var query = _db.TimeLogs.Where(t => t.UserId == userId);

            if (from != null)
            {
                var fromUtc = TimeZoneHelper.PhilippineDateStartUtc(from.Value.Date);
                query = query.Where(t => t.TimeInUtc >= fromUtc);
            }
            if (to != null)
            {
                var toUtc = TimeZoneHelper.PhilippineDateStartUtc(to.Value.Date.AddDays(1));
                query = query.Where(t => t.TimeInUtc < toUtc);
            }

            var logs = await query
                .OrderByDescending(t => t.TimeInUtc)
                .Take(200)
                .ToListAsync();

            ViewBag.FromDate = from?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = to?.ToString("yyyy-MM-dd");
            return View(logs);
        }
    }
}
