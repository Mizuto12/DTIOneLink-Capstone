using DTIOneLink.Data;
using DTIOneLink.Filters;
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

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;

            var logs = await _db.TimeLogs
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.TimeInUtc)
                .Take(200)
                .ToListAsync();

            return View(logs);
        }
    }
}
