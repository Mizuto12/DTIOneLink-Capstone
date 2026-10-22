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

        // Days per page. Index.cshtml groups logs into one card per calendar
        // day, so pagination has to happen at the day level too — paging by
        // row count instead let a single day's sessions straddle a page
        // boundary, splitting it into two cards with the same date on
        // different pages. Paging here by whole days makes that impossible.
        private const int DaysPerPage = 7;

        // Upper bound on rows pulled into memory for grouping (below), not a
        // display limit — the oldest rows beyond it are simply never seen.
        // Generous for how this app is actually used; revisit if a single
        // account's history genuinely grows past it.
        private const int MaxRowsConsidered = 1000;

        // from/to are calendar dates in the office's time zone (Philippine),
        // same as the date picker shows — converted to UTC bounds against
        // TimeInUtc since that's what's stored. Both optional; either can be
        // used alone (e.g. "from" only = "since this date").
        [HttpGet]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, int page = 1)
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
                .Take(MaxRowsConsidered)
                .ToListAsync();

            // Group by Philippine calendar date — the same key Index.cshtml
            // groups by for display — so a "page" below means whole days,
            // never a day cut in half.
            var dayGroups = logs
                .GroupBy(t => TimeZoneHelper.ToPhilippineTime(t.TimeInUtc).Date)
                .OrderByDescending(g => g.Key)
                .ToList();

            var totalPages = Math.Max(1, (int)Math.Ceiling(dayGroups.Count / (double)DaysPerPage));
            page = Math.Clamp(page, 1, totalPages);

            var pageLogs = dayGroups
                .Skip((page - 1) * DaysPerPage)
                .Take(DaysPerPage)
                .SelectMany(g => g)
                .ToList();

            ViewBag.FromDate = from?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = to?.ToString("yyyy-MM-dd");
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            return View(pageLogs);
        }
    }
}
