using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services;
using System.Collections.Generic;
using System.Data;

namespace DTIOneLink.Controllers
{
    public class RecordsController : Controller
    {
        private readonly string _connectionString;

        // Standard choices mirror the dropdowns on Views/Records/Index.cshtml.
        // Each dropdown also has "Other / Specify", so any non-empty typed
        // value is accepted too; a typed value that matches a standard choice
        // is stored in that choice's exact spelling so filters stay reliable.
        private static readonly string[] Media = { "Hard Copy", "Electronic", "Hard Copy/Electronic" };
        private static readonly string[] FilingSystems =
        {
            "Alphabetical/Numerical", "Chronological", "Per Transaction", "Per Activity/Project",
            "Per Batch", "Per Bureau/Office/FG", "Per Location"
        };
        private static readonly string[] AccessLevels = { "Confidential", "Public Record", "Exclusive", "DTI Only" };

        // What the browser sends for "Other / Specify" if the typed box was
        // somehow skipped — never a real value.
        private static readonly string[] OtherPlaceholders = { "__other__", "Other / Specify", "Other/Specify" };

        // Filter value meaning "anything not in the standard list" (typed
        // values and pre-existing records such as Physical/Digital).
        private const string OtherFilter = "other";

        private const int MaxChoiceLength = 50;
        private const int MaxSearchLength = 100;

        // Upper bounds for the typed text fields. The real column sizes may be
        // smaller; SQL Server's truncation error is caught in Save as well.
        private const int MaxCodeLength = 100;
        private const int MaxTitleLength = 500;
        private const int MaxLocationLength = 255;
        private const int MaxPeriodCoveredLength = 100;
        private const int MaxRetentionPeriodLength = 100;

        private const string GenericSaveError = "Unable to save the record. Please contact the administrator.";
        private const string GenericLoadError = "Unable to load records. Please contact the administrator.";

        // ── Optional system columns ──────────────────────────────────
        // Migration 20260925000000_AddRecordSystemFields adds these. Until it
        // is applied, Save/GetAll must work with the original columns only —
        // writing to missing columns is what made Save fail with SQL error
        // 207 ("Invalid column name"). Once present they're used
        // automatically; a "missing" result is re-checked every few minutes
        // so applying the migration doesn't need an app restart.
        private static readonly string[] SystemColumns =
            { "CreatedByUserId", "OwningDepartment", "RecordStatus", "RecordDate", "RetentionDueDate" };
        private static readonly TimeSpan SchemaRecheckInterval = TimeSpan.FromMinutes(5);
        private static readonly object SchemaLock = new();
        private static bool _hasSystemColumns;
        // Records.MasterlistId, from migration 20260926000000_AddRecordMasterlists.
        // Without it the working table shows every record and Save Masterlist
        // is refused.
        private const string MasterlistColumn = "MasterlistId";
        private static bool _hasMasterlistColumn;
        private static DateTime _schemaCheckedAtUtc = DateTime.MinValue;

        // Stand-in for "Records r" when the system columns don't exist yet:
        // exposes the same column names as NULLs, so the visibility clause
        // and filters below run unchanged and treat every row as a record
        // logged before departments were tracked.
        private const string LegacyRecordsSource = @"(
    SELECT RecordId, Code, Title, Medium, Location, PeriodCovered,
           FilingSystem, AccessControl, RetentionPeriod,
           CAST(NULL AS int)           AS CreatedByUserId,
           CAST(NULL AS nvarchar(50))  AS OwningDepartment,
           CAST(N'Active' AS nvarchar(20)) AS RecordStatus,
           CAST(NULL AS date)          AS RecordDate,
           CAST(NULL AS date)          AS RetentionDueDate
    FROM Records) r";

        private readonly ILogger<RecordsController> _logger;

        public RecordsController(IConfiguration configuration, ILogger<RecordsController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
            _logger = logger;
        }

        private bool HasSystemColumns(SqlConnection openConnection)
        {
            lock (SchemaLock)
            {
                if ((_hasSystemColumns && _hasMasterlistColumn) || DateTime.UtcNow - _schemaCheckedAtUtc < SchemaRecheckInterval)
                {
                    return _hasSystemColumns;
                }
            }

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(
                "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Records')", openConnection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    found.Add(reader.GetString(0));
                }
            }

            var hasAll = SystemColumns.All(found.Contains);
            if (!hasAll)
            {
                _logger.LogWarning(
                    "Records system columns are missing ({Missing}); saving and listing with the original columns only. " +
                    "Apply migration 20260925000000_AddRecordSystemFields to enable them.",
                    string.Join(", ", SystemColumns.Where(c => !found.Contains(c))));
            }

            var hasMasterlist = hasAll && found.Contains(MasterlistColumn);
            if (hasAll && !hasMasterlist)
            {
                _logger.LogWarning(
                    "Records.MasterlistId is missing; Save Masterlist is disabled. " +
                    "Apply migration 20260926000000_AddRecordMasterlists to enable it.");
            }

            lock (SchemaLock)
            {
                _hasSystemColumns = hasAll;
                _hasMasterlistColumn = hasMasterlist;
                _schemaCheckedAtUtc = DateTime.UtcNow;
            }
            return hasAll;
        }

        private bool HasMasterlists(SqlConnection openConnection)
        {
            HasSystemColumns(openConnection); // refreshes both flags when due
            lock (SchemaLock)
            {
                return _hasMasterlistColumn;
            }
        }

        // Validates one required free-text field. Returns an error message,
        // or null when the value is acceptable.
        private static string? CheckText(string value, string label, int maxLength)
        {
            if (value.Length == 0) return $"Please fill in {label}.";
            if (value.Length > maxLength) return $"{label} is too long (maximum {maxLength} characters).";
            if (value.Any(char.IsControl)) return $"{label} contains characters that aren't allowed.";
            return null;
        }

        // Who is asking, resolved from the server-side session only — never
        // from anything the browser sends.
        private sealed record RecordsUser(int UserId, string? Department, bool CanManage);

        // Roles allowed to open, list, and add records. The single rule used by
        // Index, GetAll, and Save. Which rows a user can SEE is decided
        // separately by VisibilityClause: only the records they logged.
        private static readonly string[] RecordsRoles = { "SuperAdmin", "Admin", "Employee" };

        private enum RecordsAccess { Allowed, NotLoggedIn, Forbidden }

        private RecordsAccess CheckAccess(out RecordsUser? user)
        {
            user = null;

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RecordsAccess.NotLoggedIn;
            }

            var role = HttpContext.Session.GetString("UserRole");
            if (!RecordsRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                return RecordsAccess.Forbidden;
            }

            var department = HttpContext.Session.GetString("UserDepartment");
            user = new RecordsUser(
                userId.Value,
                string.IsNullOrWhiteSpace(department) ? null : department.Trim(),
                RolePermissions.Has(role, Permissions.ManageRecords));
            return RecordsAccess.Allowed;
        }

        // JSON endpoints (GetAll, Save) answer with a status + { message }
        // instead of an HTML redirect, so records.js can show the reason.
        private IActionResult AccessDeniedJson(RecordsAccess access) =>
            access == RecordsAccess.NotLoggedIn
                ? StatusCode(StatusCodes.Status401Unauthorized,
                    new { message = "Your session has expired. Please log in again." })
                : StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Your account is not allowed to use Records Management." });

        // Row-level visibility, applied in SQL so hidden rows never leave the
        // database: every record is visible ONLY to the person who logged it
        // (CreatedByUserId). This applies whatever the Access Control value
        // says and whatever the viewer's role is — other Admins and the
        // SuperAdmin don't see it either. Records with no stored owner
        // (logged before the owner column existed) are visible to nobody
        // until an owner is assigned in the database.
        private const string VisibilityClause = @"
(
    r.CreatedByUserId IS NOT NULL
    AND r.CreatedByUserId = @UserId
)";

        // Builds "@Prefix0, @Prefix1, ..." for a NOT IN over a standard list.
        private static string AddListParameters(SqlCommand cmd, string prefix, string[] values)
        {
            var names = new List<string>();
            for (var i = 0; i < values.Length; i++)
            {
                var name = $"@{prefix}{i}";
                cmd.Parameters.Add(name, SqlDbType.NVarChar, MaxChoiceLength).Value = values[i];
                names.Add(name);
            }
            return string.Join(", ", names);
        }

        // Validates one dropdown's final value (standard or typed). Returns
        // the value to store, or null if it isn't a real value.
        private static string? NormalizeChoice(string? value, string[] standard)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)
                || trimmed.Length > MaxChoiceLength
                || trimmed.Any(char.IsControl)
                || OtherPlaceholders.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return MatchAllowed(trimmed, standard) ?? trimmed;
        }

        private static void AddVisibilityParameters(SqlCommand cmd, RecordsUser user)
        {
            cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
        }

        // Records dates are calendar dates in the office's time zone.
        private static DateTime PhilippineToday() => TimeZoneHelper.ToPhilippineTime(DateTime.UtcNow).Date;

        // Treat user search text literally inside LIKE.
        private static string EscapeLike(string value) =>
            value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

        private static string? MatchAllowed(string? value, string[] allowed) =>
            allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));

        // Reminder colour for a row, as DTI asked: yellow ("due-soon") when the
        // retention period ends within the same window as the disposal
        // notification, red ("ended") once it has ended. Null otherwise —
        // including records with no computable due date. Reminder only; the
        // system never disposes of anything.
        private static string? RetentionState(RecordRow row)
        {
            if (row.RetentionDueDate == null || !string.Equals(row.RecordStatus, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var today = PhilippineToday();
            var due = row.RetentionDueDate.Value.Date;
            if (due <= today)
            {
                return "ended";
            }
            return due <= today.AddMonths(RecordRetentionReminder.ReminderMonths) ? "due-soon" : null;
        }

        // Employees only ever receive the eight columns shown on the page plus
        // the reminder colour; system-managed fields are added for records
        // managers only.
        private static object ToClientRow(RecordRow row, RecordsUser user)
        {
            if (!user.CanManage)
            {
                return new
                {
                    row.RecordId, row.Code, row.Title, row.Medium, row.Location,
                    row.PeriodCovered, row.FilingSystem, row.AccessControl, row.RetentionPeriod,
                    RetentionState = RetentionState(row)
                };
            }

            return new
            {
                row.RecordId, row.Code, row.Title, row.Medium, row.Location,
                row.PeriodCovered, row.FilingSystem, row.AccessControl, row.RetentionPeriod,
                row.RecordStatus,
                row.OwningDepartment,
                RecordDate = row.RecordDate?.ToString("yyyy-MM-dd"),
                RetentionDueDate = row.RetentionDueDate?.ToString("yyyy-MM-dd"),
                RetentionState = RetentionState(row)
            };
        }

        private sealed class RecordRow
        {
            public int RecordId { get; init; }
            public string Code { get; init; } = "";
            public string Title { get; init; } = "";
            public string Medium { get; init; } = "";
            public string Location { get; init; } = "";
            public string PeriodCovered { get; init; } = "";
            public string FilingSystem { get; init; } = "";
            public string AccessControl { get; init; } = "";
            public string RetentionPeriod { get; init; } = "";
            public string RecordStatus { get; init; } = "";
            public string? OwningDepartment { get; init; }
            public DateTime? RecordDate { get; init; }
            public DateTime? RetentionDueDate { get; init; }
        }

        [HttpGet]
        public IActionResult Index()
        {
            var access = CheckAccess(out var user);
            if (access == RecordsAccess.NotLoggedIn)
            {
                return RedirectToAction("Login", "Account");
            }
            if (access != RecordsAccess.Allowed || user == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            // Only decides whether the retention filter is rendered; GetAll
            // enforces the same rule on its own.
            ViewBag.CanManageRecords = user.CanManage;
            return View();
        }

        // GET: /Records/GetAll?q=&medium=&access=&retention=
        // Loads the list, filtered on the server. Every filter is optional.
        [HttpGet]
        public IActionResult GetAll(string? q, string? medium, string? access, string? retention)
        {
            var accessResult = CheckAccess(out var user);
            if (accessResult != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(accessResult);
            }

            try
            {
                return Json(LoadRecords(user, q, medium, access, retention));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load records for user {UserId}.", user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericLoadError });
            }
        }

        private List<object> LoadRecords(RecordsUser user, string? q, string? medium, string? access, string? retention)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            var source = HasSystemColumns(conn) ? "Records r" : LegacyRecordsSource;

            var sql = new System.Text.StringBuilder(@"
SELECT r.RecordId, r.Code, r.Title, r.Medium, r.Location, r.PeriodCovered,
       r.FilingSystem, r.AccessControl, r.RetentionPeriod,
       r.RecordStatus, r.OwningDepartment, r.RecordDate, r.RetentionDueDate
FROM ").Append(source).Append(@"
WHERE ").Append(VisibilityClause);

            using var cmd = new SqlCommand { Connection = conn };
            AddVisibilityParameters(cmd, user);

            // The working table only holds records not yet saved into a
            // masterlist, plus the records of the masterlist the user has
            // reopened to add to (if any). Saved ones stay in the database
            // (for retention reminders and Reports) and live in their file.
            if (HasMasterlists(conn))
            {
                sql.Append(" AND (r.MasterlistId IS NULL OR r.MasterlistId = @OpenMasterlistId)");
                cmd.Parameters.Add("@OpenMasterlistId", SqlDbType.Int).Value =
                    (object?)HttpContext.Session.GetInt32(OpenMasterlistSessionKey) ?? DBNull.Value;
            }

            var search = q?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                if (search.Length > MaxSearchLength)
                {
                    search = search[..MaxSearchLength];
                }

                sql.Append(@"
  AND (r.Code LIKE @Search OR r.Title LIKE @Search
       OR r.Location LIKE @Search OR r.PeriodCovered LIKE @Search)");
                cmd.Parameters.Add("@Search", SqlDbType.NVarChar, MaxSearchLength * 3 + 2).Value =
                    "%" + EscapeLike(search) + "%";
            }

            // Unknown filter values are ignored rather than trusted. "other"
            // matches every value outside the standard list.
            var mediumFilter = MatchAllowed(medium, Media);
            if (mediumFilter != null)
            {
                sql.Append(" AND r.Medium = @Medium");
                cmd.Parameters.Add("@Medium", SqlDbType.NVarChar, MaxChoiceLength).Value = mediumFilter;
            }
            else if (string.Equals(medium?.Trim(), OtherFilter, StringComparison.OrdinalIgnoreCase))
            {
                sql.Append($" AND r.Medium NOT IN ({AddListParameters(cmd, "MediumStd", Media)})");
            }

            var accessFilter = MatchAllowed(access, AccessLevels);
            if (accessFilter != null)
            {
                sql.Append(" AND r.AccessControl = @Access");
                cmd.Parameters.Add("@Access", SqlDbType.NVarChar, MaxChoiceLength).Value = accessFilter;
            }
            else if (string.Equals(access?.Trim(), OtherFilter, StringComparison.OrdinalIgnoreCase))
            {
                sql.Append($" AND r.AccessControl NOT IN ({AddListParameters(cmd, "AccessStd", AccessLevels)})");
            }

            // Retention monitoring is a records-manager function; the filter
            // is silently ignored for anyone else.
            if (user.CanManage && !string.IsNullOrWhiteSpace(retention))
            {
                var today = PhilippineToday();
                switch (retention.Trim().ToLowerInvariant())
                {
                    case "overdue":
                        sql.Append(" AND r.RetentionDueDate <= @Today");
                        cmd.Parameters.Add("@Today", SqlDbType.Date).Value = today;
                        break;
                    case "due-soon":
                        sql.Append(" AND r.RetentionDueDate > @Today AND r.RetentionDueDate <= @SoonLimit");
                        cmd.Parameters.Add("@Today", SqlDbType.Date).Value = today;
                        cmd.Parameters.Add("@SoonLimit", SqlDbType.Date).Value = today.AddMonths(RecordRetentionReminder.ReminderMonths);
                        break;
                    case "not-computed":
                        sql.Append(" AND r.RetentionDueDate IS NULL");
                        break;
                }
            }

            // Oldest first — the order entries were logged, matching DTI's
            // masterlist (GAS-01, GAS-02, ...). RecordId is the identity, so
            // it follows entry order.
            sql.Append(" ORDER BY r.RecordId ASC");
            cmd.CommandText = sql.ToString();

            var results = new List<object>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var row = new RecordRow
                    {
                        RecordId = reader.GetInt32(0),
                        Code = reader.GetString(1),
                        Title = reader.GetString(2),
                        Medium = reader.GetString(3),
                        Location = reader.GetString(4),
                        PeriodCovered = reader.GetString(5),
                        FilingSystem = reader.GetString(6),
                        AccessControl = reader.GetString(7),
                        RetentionPeriod = reader.GetString(8),
                        RecordStatus = reader.GetString(9),
                        OwningDepartment = reader.IsDBNull(10) ? null : reader.GetString(10),
                        RecordDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                        RetentionDueDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12)
                    };
                    results.Add(ToClientRow(row, user));
                }
            }

            return results;
        }

        // POST: /Records/Save — called by the form's fetch().
        // The browser sends only the eight visible fields; every
        // system-managed field is set here from the session. Every failure
        // returns JSON { message } so the page can show a useful reason.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Save([FromBody] RecordEntry? entry)
        {
            // Same rule as Index and GetAll.
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }

            // Null when the body isn't valid JSON or a field is missing
            // (RecordEntry's properties are all required).
            if (entry == null)
            {
                return BadRequest(new { message = "Please fill in every field." });
            }

            var code = entry.Code?.Trim() ?? "";
            var title = entry.Title?.Trim() ?? "";
            var location = entry.Location?.Trim() ?? "";
            var periodCovered = entry.PeriodCovered?.Trim() ?? "";
            var retentionPeriod = entry.RetentionPeriod?.Trim() ?? "";

            var textError = CheckText(code, "Code", MaxCodeLength)
                ?? CheckText(title, "Title of Record", MaxTitleLength)
                ?? CheckText(location, "Location", MaxLocationLength)
                ?? CheckText(periodCovered, "Period Covered", MaxPeriodCoveredLength)
                ?? CheckText(retentionPeriod, "Retention Period", MaxRetentionPeriodLength);
            if (textError != null)
            {
                return BadRequest(new { message = textError });
            }

            var medium = NormalizeChoice(entry.Medium, Media);
            if (medium == null)
            {
                return BadRequest(new { message = "Please choose or type a Medium." });
            }

            var filingSystem = NormalizeChoice(entry.FilingSystem, FilingSystems);
            if (filingSystem == null)
            {
                return BadRequest(new { message = "Please choose or type a Filing System." });
            }

            var accessControl = NormalizeChoice(entry.AccessControl, AccessLevels);
            if (accessControl == null)
            {
                return BadRequest(new { message = "Please choose or type an Access Control." });
            }

            var recordDate = PhilippineToday();
            var retentionDueDate = RetentionPeriodParser.TryComputeDueDate(recordDate, periodCovered, retentionPeriod);
            const string recordStatus = "Active";

            try
            {
                int newId;
                bool savedSystemFields;
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    savedSystemFields = HasSystemColumns(conn);

                    // Records are visible only to their owner, so a record
                    // saved without CreatedByUserId would be invisible to
                    // everyone. Refuse rather than store an orphan.
                    if (!savedSystemFields)
                    {
                        _logger.LogError(
                            "Refused to save record {Code} for user {UserId}: Records owner column is missing. " +
                            "Apply migration 20260925000000_AddRecordSystemFields.", code, user.UserId);
                        return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
                    }

                    // Original columns only, unless the system columns exist.
                    var sql = savedSystemFields
                        ? @"INSERT INTO Records
                              (Code, Title, Medium, Location, PeriodCovered, FilingSystem, AccessControl, RetentionPeriod, CreatedAt,
                               CreatedByUserId, OwningDepartment, RecordStatus, RecordDate, RetentionDueDate)
                            OUTPUT INSERTED.RecordId
                            VALUES
                              (@Code, @Title, @Medium, @Location, @PeriodCovered, @FilingSystem, @AccessControl, @RetentionPeriod, SYSUTCDATETIME(),
                               @CreatedByUserId, @OwningDepartment, @RecordStatus, @RecordDate, @RetentionDueDate)"
                        : @"INSERT INTO Records
                              (Code, Title, Medium, Location, PeriodCovered, FilingSystem, AccessControl, RetentionPeriod, CreatedAt)
                            OUTPUT INSERTED.RecordId
                            VALUES
                              (@Code, @Title, @Medium, @Location, @PeriodCovered, @FilingSystem, @AccessControl, @RetentionPeriod, SYSUTCDATETIME())";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Code", code);
                    cmd.Parameters.AddWithValue("@Title", title);
                    cmd.Parameters.AddWithValue("@Medium", medium);
                    cmd.Parameters.AddWithValue("@Location", location);
                    cmd.Parameters.AddWithValue("@PeriodCovered", periodCovered);
                    cmd.Parameters.AddWithValue("@FilingSystem", filingSystem);
                    cmd.Parameters.AddWithValue("@AccessControl", accessControl);
                    cmd.Parameters.AddWithValue("@RetentionPeriod", retentionPeriod);
                    if (savedSystemFields)
                    {
                        cmd.Parameters.Add("@CreatedByUserId", SqlDbType.Int).Value = user.UserId;
                        cmd.Parameters.Add("@OwningDepartment", SqlDbType.NVarChar, 50).Value = (object?)user.Department ?? DBNull.Value;
                        cmd.Parameters.Add("@RecordStatus", SqlDbType.NVarChar, 20).Value = recordStatus;
                        cmd.Parameters.Add("@RecordDate", SqlDbType.Date).Value = recordDate;
                        cmd.Parameters.Add("@RetentionDueDate", SqlDbType.Date).Value = (object?)retentionDueDate ?? DBNull.Value;
                    }

                    newId = (int)cmd.ExecuteScalar();
                }

                var saved = new RecordRow
                {
                    RecordId = newId,
                    Code = code,
                    Title = title,
                    Medium = medium,
                    Location = location,
                    PeriodCovered = periodCovered,
                    FilingSystem = filingSystem,
                    AccessControl = accessControl,
                    RetentionPeriod = retentionPeriod,
                    RecordStatus = recordStatus,
                    OwningDepartment = savedSystemFields ? user.Department : null,
                    RecordDate = savedSystemFields ? recordDate : null,
                    RetentionDueDate = savedSystemFields ? retentionDueDate : null
                };

                return Json(ToClientRow(saved, user));
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // Unique constraint violation on Code
                return Conflict(new { message = $"The Code \"{code}\" already exists. Please use a different Code." });
            }
            catch (SqlException ex) when (ex.Number == 8152 || ex.Number == 2628)
            {
                // String or binary data would be truncated
                return BadRequest(new { message = "One of the entries is too long. Please shorten it and try again." });
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                // CHECK constraint rejected a value. The Records table's
                // CK_Records_Medium / CK_Records_Filing / CK_Records_Access
                // still only allow the old dropdown choices, so the new choices
                // and typed "Other / Specify" values are refused by the
                // database until those constraints are updated.
                _logger.LogError(ex,
                    "Database constraint rejected record {Code} for user {UserId} " +
                    "(Medium={Medium}, FilingSystem={FilingSystem}, AccessControl={AccessControl}).",
                    code, user.UserId, medium, filingSystem, accessControl);
                return BadRequest(new
                {
                    message = "The database does not accept one of the selected Medium, Filing System, or Access Control values yet. Please contact the administrator."
                });
            }
            catch (Exception ex)
            {
                // Full details go to the server log only (SqlException messages
                // carry no connection string); the user gets a generic message.
                _logger.LogError(ex, "Failed to save record with Code {Code} for user {UserId}.", code, user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
            }
        }

        // ── Masterlists ──────────────────────────────────────────────
        // "Save Masterlist" puts every record the user has added since their
        // last save into one DTI Masterlist of Records Excel file, keeps the
        // file, and takes those records off the working table. Same owner-only
        // rule as the records themselves.

        private const int MaxSignatoryLength = 100;
        private const int MasterlistHistoryLimit = 100;
        private const string GenericMasterlistError = "Unable to save the masterlist. Please contact the administrator.";

        public sealed class MasterlistRequest
        {
            public string? PreparedByName { get; set; }
            public string? PreparedByPosition { get; set; }
            public string? ReviewedByName { get; set; }
            public string? ReviewedByPosition { get; set; }
            public string? NotedByName { get; set; }
            public string? NotedByPosition { get; set; }
        }

        // Signature lines may be left blank (to be written on the printout).
        private static string? CheckSignatory(string value, string label) =>
            value.Length > MaxSignatoryLength ? $"{label} is too long (maximum {MaxSignatoryLength} characters)."
            : value.Any(char.IsControl) ? $"{label} contains characters that aren't allowed."
            : null;

        // The saved masterlist the user reopened with "Add Records", kept in
        // the server-side session. While set, its records are back on the
        // working table and Save Masterlist updates it instead of creating a
        // new one. Losing the session just closes it — nothing is changed.
        private const string OpenMasterlistSessionKey = "OpenMasterlistId";

        // The id back only if that masterlist exists and belongs to the user.
        private static int? OwnedMasterlistId(SqlConnection conn, SqlTransaction? transaction, RecordsUser user, int? masterlistId)
        {
            if (masterlistId == null)
            {
                return null;
            }
            using var cmd = new SqlCommand(@"
SELECT COUNT(*) FROM dbo.RecordMasterlists WHERE MasterlistId = @Id AND CreatedByUserId = @UserId", conn, transaction);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = masterlistId.Value;
            cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
            return (int)cmd.ExecuteScalar() > 0 ? masterlistId : null;
        }

        // POST: /Records/ReopenMasterlist?id= — "Add Records" on a saved masterlist.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReopenMasterlist(int id)
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                if (!HasMasterlists(conn) || OwnedMasterlistId(conn, null, user, id) == null)
                {
                    return NotFound(new { message = "That masterlist could not be found." });
                }
                HttpContext.Session.SetInt32(OpenMasterlistSessionKey, id);
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reopen masterlist {MasterlistId} for user {UserId}.", id, user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericMasterlistError });
            }
        }

        // POST: /Records/CloseMasterlist — "Stop Adding". The saved file is
        // left as it was; records added meanwhile stay on the table as new.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CloseMasterlist()
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }
            HttpContext.Session.Remove(OpenMasterlistSessionKey);
            return Json(new { ok = true });
        }

        // POST: /Records/SaveMasterlist — returns { masterlistId, fileName, recordCount };
        // the page then downloads the file from DownloadMasterlist.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveMasterlist([FromBody] MasterlistRequest? request)
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }

            request ??= new MasterlistRequest();
            var preparedBy = new RecordMasterlistExcel.Signatory(request.PreparedByName?.Trim() ?? "", request.PreparedByPosition?.Trim() ?? "");
            var reviewedBy = new RecordMasterlistExcel.Signatory(request.ReviewedByName?.Trim() ?? "", request.ReviewedByPosition?.Trim() ?? "");
            var notedBy = new RecordMasterlistExcel.Signatory(request.NotedByName?.Trim() ?? "", request.NotedByPosition?.Trim() ?? "");

            var error = CheckSignatory(preparedBy.Name, "Prepared by name")
                ?? CheckSignatory(preparedBy.Position, "Prepared by position")
                ?? CheckSignatory(reviewedBy.Name, "Reviewed by name")
                ?? CheckSignatory(reviewedBy.Position, "Reviewed by position")
                ?? CheckSignatory(notedBy.Name, "Noted by name")
                ?? CheckSignatory(notedBy.Position, "Noted by position");
            if (error != null)
            {
                return BadRequest(new { message = error });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                if (!HasMasterlists(conn))
                {
                    _logger.LogError(
                        "Refused to save a masterlist for user {UserId}: apply migration 20260926000000_AddRecordMasterlists.",
                        user.UserId);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericMasterlistError });
                }

                using var transaction = conn.BeginTransaction();

                // A reopened masterlist (see ReopenMasterlist) is updated in
                // place: its records plus the new ones go into a fresh file.
                // Otherwise a new masterlist is created.
                var openId = OwnedMasterlistId(conn, transaction, user, HttpContext.Session.GetInt32(OpenMasterlistSessionKey));

                // UPDLOCK/HOLDLOCK: a record this user saves while the file is
                // being built waits, then stays on the table for next time,
                // instead of being marked saved without being in the file.
                var rows = new List<(int Id, bool IsNew, RecordMasterlistExcel.Row Row)>();
                using (var select = new SqlCommand(@"
SELECT RecordId, Code, Title, Medium, Location, PeriodCovered, FilingSystem, AccessControl, RetentionPeriod, MasterlistId
FROM dbo.Records WITH (UPDLOCK, HOLDLOCK)
WHERE CreatedByUserId = @UserId AND (MasterlistId IS NULL OR MasterlistId = @OpenMasterlistId)
ORDER BY RecordId", conn, transaction))
                {
                    select.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                    select.Parameters.Add("@OpenMasterlistId", SqlDbType.Int).Value = (object?)openId ?? DBNull.Value;
                    using var reader = select.ExecuteReader();
                    while (reader.Read())
                    {
                        rows.Add((reader.GetInt32(0), reader.IsDBNull(9), new RecordMasterlistExcel.Row(
                            reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8))));
                    }
                }

                if (!rows.Any(r => r.IsNew))
                {
                    return BadRequest(new
                    {
                        message = openId.HasValue
                            ? "You haven't added any new records to this masterlist yet. Add a record first, or click Stop Adding."
                            : "There are no new records to save. Add a record first."
                    });
                }

                var nowPh = TimeZoneHelper.ToPhilippineTime(DateTime.UtcNow);
                var fileName = $"Masterlist of Records {nowPh:yyyy-MM-dd HHmm}.xlsx";
                var content = RecordMasterlistExcel.Build(new RecordMasterlistExcel.Sheet(
                    user.Department, nowPh.Date, preparedBy, reviewedBy, notedBy, rows.Select(r => r.Row).ToList()));

                int masterlistId;
                // Updating keeps the same entry in Saved Masterlists; CreatedAt
                // becomes the "last saved" time shown there.
                var saveSql = openId.HasValue
                    ? @"
UPDATE dbo.RecordMasterlists
SET RecordCount = @RecordCount, FileName = @FileName, FileContent = @FileContent, CreatedAt = SYSUTCDATETIME(),
    PreparedByName = @PreparedByName, PreparedByPosition = @PreparedByPosition,
    ReviewedByName = @ReviewedByName, ReviewedByPosition = @ReviewedByPosition,
    NotedByName = @NotedByName, NotedByPosition = @NotedByPosition
OUTPUT INSERTED.MasterlistId
WHERE MasterlistId = @OpenMasterlistId AND CreatedByUserId = @UserId"
                    : @"
INSERT INTO dbo.RecordMasterlists
  (CreatedByUserId, OwningDepartment, RecordCount, FileName, FileContent,
   PreparedByName, PreparedByPosition, ReviewedByName, ReviewedByPosition, NotedByName, NotedByPosition)
OUTPUT INSERTED.MasterlistId
VALUES
  (@UserId, @Department, @RecordCount, @FileName, @FileContent,
   @PreparedByName, @PreparedByPosition, @ReviewedByName, @ReviewedByPosition, @NotedByName, @NotedByPosition)";
                using (var insert = new SqlCommand(saveSql, conn, transaction))
                {
                    insert.Parameters.Add("@OpenMasterlistId", SqlDbType.Int).Value = (object?)openId ?? DBNull.Value;
                    insert.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                    insert.Parameters.Add("@Department", SqlDbType.NVarChar, 50).Value = (object?)user.Department ?? DBNull.Value;
                    insert.Parameters.Add("@RecordCount", SqlDbType.Int).Value = rows.Count;
                    insert.Parameters.Add("@FileName", SqlDbType.NVarChar, 200).Value = fileName;
                    insert.Parameters.Add("@FileContent", SqlDbType.VarBinary, -1).Value = content;
                    insert.Parameters.Add("@PreparedByName", SqlDbType.NVarChar, MaxSignatoryLength).Value = preparedBy.Name;
                    insert.Parameters.Add("@PreparedByPosition", SqlDbType.NVarChar, MaxSignatoryLength).Value = preparedBy.Position;
                    insert.Parameters.Add("@ReviewedByName", SqlDbType.NVarChar, MaxSignatoryLength).Value = reviewedBy.Name;
                    insert.Parameters.Add("@ReviewedByPosition", SqlDbType.NVarChar, MaxSignatoryLength).Value = reviewedBy.Position;
                    insert.Parameters.Add("@NotedByName", SqlDbType.NVarChar, MaxSignatoryLength).Value = notedBy.Name;
                    insert.Parameters.Add("@NotedByPosition", SqlDbType.NVarChar, MaxSignatoryLength).Value = notedBy.Position;
                    masterlistId = (int)insert.ExecuteScalar();
                }

                using (var update = new SqlCommand(@"
UPDATE dbo.Records SET MasterlistId = @MasterlistId
WHERE CreatedByUserId = @UserId AND MasterlistId IS NULL AND RecordId <= @MaxRecordId", conn, transaction))
                {
                    update.Parameters.Add("@MasterlistId", SqlDbType.Int).Value = masterlistId;
                    update.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                    update.Parameters.Add("@MaxRecordId", SqlDbType.Int).Value = rows[^1].Id;
                    update.ExecuteNonQuery();
                }

                transaction.Commit();
                HttpContext.Session.Remove(OpenMasterlistSessionKey);
                return Json(new { masterlistId, fileName, recordCount = rows.Count, updated = openId.HasValue });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save a masterlist for user {UserId}.", user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericMasterlistError });
            }
        }

        // GET: /Records/Masterlists — the user's saved masterlists (newest
        // first) plus the signature names from the last one, to pre-fill the
        // Save Masterlist form.
        [HttpGet]
        public IActionResult Masterlists()
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }

            var fullName = HttpContext.Session.GetString("FullName") ?? "";
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                if (!HasMasterlists(conn))
                {
                    return Json(new { available = false, items = Array.Empty<object>(), signatories = new MasterlistRequest { PreparedByName = fullName } });
                }

                using var cmd = new SqlCommand($@"
SELECT TOP ({MasterlistHistoryLimit}) MasterlistId, FileName, CreatedAt, RecordCount,
       PreparedByName, PreparedByPosition, ReviewedByName, ReviewedByPosition, NotedByName, NotedByPosition
FROM dbo.RecordMasterlists
WHERE CreatedByUserId = @UserId
ORDER BY CreatedAt DESC, MasterlistId DESC", conn);
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;

                // Only trusted if it is one of this user's masterlists (below).
                var openId = HttpContext.Session.GetInt32(OpenMasterlistSessionKey);
                object? openMasterlist = null;
                var items = new List<object>();
                MasterlistRequest? last = null;
                MasterlistRequest? openSignatories = null;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetInt32(0);
                        var signatories = new MasterlistRequest
                        {
                            PreparedByName = reader.GetString(4),
                            PreparedByPosition = reader.GetString(5),
                            ReviewedByName = reader.GetString(6),
                            ReviewedByPosition = reader.GetString(7),
                            NotedByName = reader.GetString(8),
                            NotedByPosition = reader.GetString(9)
                        };
                        last ??= signatories;
                        var isOpen = id == openId;
                        if (isOpen)
                        {
                            openSignatories = signatories;
                            openMasterlist = new { id, fileName = reader.GetString(1) };
                        }
                        var savedAt = TimeZoneHelper.ToPhilippineTime(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc));
                        items.Add(new
                        {
                            id,
                            fileName = reader.GetString(1),
                            savedAt = savedAt.ToString("MMMM d, yyyy h:mm tt"),
                            recordCount = reader.GetInt32(3),
                            isOpen
                        });
                    }
                }

                return Json(new
                {
                    available = true,
                    items,
                    openMasterlist,
                    // Updating a reopened masterlist starts from its own names.
                    signatories = openSignatories ?? last ?? new MasterlistRequest { PreparedByName = fullName }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load masterlists for user {UserId}.", user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericLoadError });
            }
        }

        // GET: /Records/DownloadMasterlist?id= — only the user who saved it.
        [HttpGet]
        public IActionResult DownloadMasterlist(int id)
        {
            var access = CheckAccess(out var user);
            if (access == RecordsAccess.NotLoggedIn)
            {
                return RedirectToAction("Login", "Account");
            }
            if (access != RecordsAccess.Allowed || user == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                if (!HasMasterlists(conn))
                {
                    return NotFound();
                }

                using var cmd = new SqlCommand(@"
SELECT FileName, FileContent FROM dbo.RecordMasterlists
WHERE MasterlistId = @Id AND CreatedByUserId = @UserId", conn);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    return NotFound();
                }

                return File((byte[])reader[1], RecordMasterlistExcel.ContentType, reader.GetString(0));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download masterlist {MasterlistId} for user {UserId}.", id, user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
