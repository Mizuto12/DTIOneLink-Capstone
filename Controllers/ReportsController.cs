using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Controllers
{
    public class ReportsController : Controller
    {
        // Newest audit-log entries returned (keeps the page quick).
        private const int AuditLogLimit = 200;

        private readonly AppDbContext _context;
        private readonly DatabaseHelper _db;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(AppDbContext context, DatabaseHelper db, ILogger<ReportsController> logger)
        {
            _context = context;
            _db = db;
            _logger = logger;
        }

        // GET: /Reports or /Reports/Index
        // Shared by both Admin and Employee sidebars — the view itself should pick
        // AdminLayout or EmployeeLayout based on the logged-in user's session role,
        // the same way Views/Records/Index.cshtml does.
        [HttpGet]
        public IActionResult Index()
        {
            if (!IsAllowedRole(HttpContext.Session.GetString("UserRole")))
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        // GET: /Reports/Data — every report item this user may see, in the
        // shape reports.js renders: { id, title, owner, tag, badge, icon,
        // tone, time, category }. Scope follows the rest of the system:
        //   Employee — tasks they are assigned to, and their activity.
        //   Admin    — tasks of their division (plus their own assignments).
        //   Records  — only the records the user created (owner-only rule).
        [HttpGet]
        public async Task<IActionResult> Data()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("UserRole");
            if (userId == null)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new { message = "Please sign in again." });
            }
            if (!IsAllowedRole(role))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "You don't have access to reports." });
            }

            var department = HttpContext.Session.GetString("UserDepartment");
            var isAdmin = role == "Admin";
            var isOfficeWide = RolePermissions.Has(role, Permissions.ViewOfficeWideSummaries);
            var now = DateTime.UtcNow;
            var items = new List<ReportItem>();

            // ── Tasks: Active / Completed ─────────────────────────────
            var tasks = await ScopedTasks(userId.Value, isAdmin, isOfficeWide, department)
                .AsNoTracking()
                .Select(t => new
                {
                    t.Id,
                    t.TaskName,
                    t.Status,
                    t.Progress,
                    t.CreatedAt,
                    t.DueDate,
                    t.TaskLevel,
                    t.OwningDepartment,
                    AssigneeNames = t.Assignments.Where(a => a.User != null).Select(a => a.User!.FullName).ToList(),
                    LastActivity = t.Activities.Max(a => (DateTime?)a.OccurredAt)
                })
                .ToListAsync();

            foreach (var t in tasks)
            {
                var completed = TaskWorkflow.Normalize(t.Status) == TaskWorkflow.Completed;
                var owner = t.AssigneeNames.Count switch
                {
                    0 => t.TaskLevel == TaskLevels.Main ? (t.OwningDepartment ?? "OPD") : "Unassigned",
                    1 => t.AssigneeNames[0],
                    _ => $"{t.AssigneeNames[0]} +{t.AssigneeNames.Count - 1} more"
                };

                string tag, icon, tone;
                string? badge = null;
                if (completed)
                {
                    tag = "Completed";
                    icon = "task_alt";
                    tone = "secondary";
                }
                else if (TaskWorkflow.IsOverdue(t.Status, t.DueDate))
                {
                    badge = "Overdue";
                    tag = badge;
                    icon = "event_busy";
                    tone = "primary";
                }
                else
                {
                    // Delay-risk indicator, same rule as Workflow Monitoring.
                    var risk = TaskWorkflow.DelayRisk(t.Status, t.Progress, t.CreatedAt, t.DueDate);
                    if (risk is { AtRisk: true })
                    {
                        badge = "At risk";
                    }
                    tag = $"{TaskWorkflow.DisplayLabel(t.Status, t.DueDate)} · {t.Progress}% · due {t.DueDate:MMM d}";
                    icon = risk is { AtRisk: true } ? "warning" : "schedule";
                    tone = risk is { AtRisk: true } ? "primary" : "tint";
                }

                items.Add(new ReportItem(
                    Id: $"TASK-{t.Id:D4}",
                    Title: t.TaskName,
                    Owner: owner,
                    Tag: tag,
                    Badge: badge,
                    Icon: icon,
                    Tone: tone,
                    Time: TimeAgo(t.LastActivity ?? t.CreatedAt, now),
                    Category: completed ? "Completed Tasks" : "Active Tasks",
                    SortAt: t.LastActivity ?? t.CreatedAt));
            }

            // ── Audit logs: task history on the tasks above ──────────
            var taskIds = tasks.Select(t => t.Id).ToList();
            var activities = await _context.TaskActivities
                .AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskId))
                .OrderByDescending(a => a.OccurredAt)
                .Take(AuditLogLimit)
                .Select(a => new
                {
                    a.Id,
                    a.ActivityType,
                    a.Details,
                    a.OccurredAt,
                    a.TaskId,
                    TaskName = a.Task != null ? a.Task.TaskName : "",
                    PerformedBy = a.PerformedBy != null ? a.PerformedBy.FullName : "Unknown"
                })
                .ToListAsync();

            foreach (var a in activities)
            {
                items.Add(new ReportItem(
                    Id: $"LOG-{a.Id}",
                    Title: string.IsNullOrWhiteSpace(a.Details)
                        ? $"{ActivityLabel(a.ActivityType)}: {a.TaskName}"
                        : $"{a.TaskName} — {a.Details}",
                    Owner: a.PerformedBy,
                    Tag: $"{ActivityLabel(a.ActivityType)} · TASK-{a.TaskId:D4}",
                    Badge: null,
                    Icon: "history",
                    Tone: "primary-container",
                    Time: TimeAgo(a.OccurredAt, now),
                    Category: "Audit Logs",
                    SortAt: a.OccurredAt));
            }

            // ── Records: Retained / Archived / Disposed ──────────────
            items.AddRange(await OwnRecordsAsync(userId.Value, now));

            var ordered = items.OrderByDescending(i => i.SortAt).Select(i => new
            {
                id = i.Id,
                title = i.Title,
                owner = i.Owner,
                tag = i.Tag,
                badge = i.Badge,
                icon = i.Icon,
                tone = i.Tone,
                time = i.Time,
                category = i.Category
            });

            return Json(ordered);
        }

        private static bool IsAllowedRole(string? role) =>
            role == "Admin" || role == "Employee" ||
            RolePermissions.Has(role, Permissions.ViewOfficeWideSummaries);

        // Same visibility rules as EmployeeController.AccessibleTasksQuery.
        private IQueryable<TaskItem> ScopedTasks(int userId, bool isAdmin, bool isOfficeWide, string? department)
        {
            var query = _context.TaskItems.AsQueryable();
            if (isOfficeWide)
            {
                return query;
            }
            if (!isAdmin)
            {
                return query.Where(t => t.Assignments.Any(a => a.UserId == userId));
            }

            return query.Where(t =>
                t.Assignments.Any(a => a.UserId == userId) ||
                (t.OwningDepartment != null && t.OwningDepartment == department) ||
                (t.OwningDepartment == null && t.ParentTask != null && t.ParentTask.OwningDepartment == department) ||
                (t.OwningDepartment == null && (t.ParentTask == null || t.ParentTask.OwningDepartment == null) &&
                    t.Assignments.Any(a => a.User != null && a.User.Department == department)));
        }

        // Records the user owns (same owner-only rule as the Records page).
        // "Retained" = still active; "Archived"/"Disposed" follow RecordStatus
        // once those statuses are used.
        private async Task<List<ReportItem>> OwnRecordsAsync(int userId, DateTime now)
        {
            var items = new List<ReportItem>();
            const string sql = @"
                SELECT r.RecordId, r.Code, r.Title, r.RecordStatus, r.RetentionPeriod,
                       r.RetentionDueDate, r.CreatedAt, r.UpdatedAt, u.FullName
                FROM dbo.Records r
                LEFT JOIN dbo.Users u ON u.Id = r.CreatedByUserId
                WHERE r.CreatedByUserId = @UserId
                ORDER BY r.RecordId";

            try
            {
                using var conn = _db.GetConnection();
                await conn.OpenAsync();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                using var reader = await cmd.ExecuteReaderAsync();

                // Same "today" as the Records page's retention colours.
                var today = TimeZoneHelper.ToPhilippineTime(now).Date;
                var reminderFrom = today.AddMonths(RecordRetentionReminder.ReminderMonths);

                while (await reader.ReadAsync())
                {
                    var status = reader.IsDBNull(3) ? "Active" : reader.GetString(3);
                    var retention = reader.IsDBNull(4) ? "" : reader.GetString(4);
                    DateTime? dueDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5);
                    var createdAt = reader.GetDateTime(6);
                    DateTime? updatedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7);
                    var changedAt = updatedAt ?? createdAt;

                    var category = status.Trim().ToLowerInvariant() switch
                    {
                        "archived" => "Archived",
                        "disposed" => "Disposed",
                        _ => "Retained"
                    };

                    string? badge = null;
                    string tag;
                    if (category == "Retained" && dueDate.HasValue && dueDate.Value <= today)
                    {
                        badge = "Retention ended";
                        tag = badge;
                    }
                    else if (category == "Retained" && dueDate.HasValue && dueDate.Value <= reminderFrom)
                    {
                        badge = "Disposal due soon";
                        tag = badge;
                    }
                    else
                    {
                        tag = dueDate.HasValue
                            ? $"Retain {retention} · until {dueDate.Value:MMM d, yyyy}"
                            : $"Retain {retention}";
                    }

                    items.Add(new ReportItem(
                        Id: reader.IsDBNull(1) ? $"REC-{reader.GetInt32(0)}" : reader.GetString(1),
                        Title: reader.IsDBNull(2) ? "(untitled record)" : reader.GetString(2),
                        Owner: reader.IsDBNull(8) ? "Unknown" : reader.GetString(8),
                        Tag: tag,
                        Badge: badge,
                        Icon: category switch { "Archived" => "inventory_2", "Disposed" => "delete", _ => "folder" },
                        Tone: badge != null ? "primary" : "secondary",
                        Time: TimeAgo(changedAt, now),
                        Category: category,
                        SortAt: changedAt));
                }
            }
            catch (SqlException ex)
            {
                // Tasks still load if the Records table can't be read.
                _logger.LogError(ex, "Reports: could not read records for user {UserId}", userId);
            }

            return items;
        }

        private static string ActivityLabel(string activityType) => activityType switch
        {
            TaskActivityType.Created => "Created",
            TaskActivityType.Edited => "Edited",
            TaskActivityType.Reassigned => "Reassigned",
            TaskActivityType.ProgressUpdated => "Progress updated",
            TaskActivityType.StatusChanged => "Status changed",
            TaskActivityType.ProofSubmitted => "Proof submitted",
            TaskActivityType.Validated => "Reviewed",
            TaskActivityType.Commented => "Commented",
            TaskActivityType.Assigned => "Assigned",
            TaskActivityType.Removed => "Removed",
            _ => "Updated"
        };

        // "just now", "5 minutes ago", "3 days ago", or a date after a month.
        // Times are stored in UTC.
        private static string TimeAgo(DateTime whenUtc, DateTime nowUtc)
        {
            var span = nowUtc - whenUtc;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return Plural((int)span.TotalMinutes, "minute");
            if (span.TotalDays < 1) return Plural((int)span.TotalHours, "hour");
            if (span.TotalDays < 30) return Plural((int)span.TotalDays, "day");
            return TimeZoneHelper.ToPhilippineTime(whenUtc).ToString("MMM d, yyyy");
        }

        private static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";

        private sealed record ReportItem(
            string Id, string Title, string Owner, string Tag, string? Badge,
            string Icon, string Tone, string Time, string Category, DateTime SortAt);
    }
}
