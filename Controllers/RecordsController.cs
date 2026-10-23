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
        // Records.ApprovedByUserId/ApprovedAt, from migration
        // 20261019000000_AddRecordApproval. Without them Approve is refused,
        // same tolerant pattern as the other optional columns above.
        private static readonly string[] ApprovalColumns = { "ApprovedByUserId", "ApprovedAt" };
        private static bool _hasApprovalColumns;
        private static DateTime _approvalSchemaCheckedAtUtc = DateTime.MinValue;
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
           CAST(N'Retention' AS nvarchar(20)) AS RecordStatus,
           CAST(NULL AS date)          AS RecordDate,
           CAST(NULL AS date)          AS RetentionDueDate
    FROM Records) r";

        private readonly ILogger<RecordsController> _logger;
        private readonly NotificationService _notifications;

        public RecordsController(IConfiguration configuration, ILogger<RecordsController> logger, NotificationService notifications)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
            _logger = logger;
            _notifications = notifications;
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

        private bool HasApprovalColumns(SqlConnection openConnection)
        {
            lock (SchemaLock)
            {
                if (_hasApprovalColumns || DateTime.UtcNow - _approvalSchemaCheckedAtUtc < SchemaRecheckInterval)
                {
                    return _hasApprovalColumns;
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

            var hasAll = ApprovalColumns.All(found.Contains);
            if (!hasAll)
            {
                _logger.LogWarning(
                    "Records approval columns are missing ({Missing}); Approve is disabled. " +
                    "Apply migration 20261019000000_AddRecordApproval to enable it.",
                    string.Join(", ", ApprovalColumns.Where(c => !found.Contains(c))));
            }

            lock (SchemaLock)
            {
                _hasApprovalColumns = hasAll;
                _approvalSchemaCheckedAtUtc = DateTime.UtcNow;
            }
            return hasAll;
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
        // from anything the browser sends. CanManage (Admin, the records
        // officer) handles disposal validation; CanApprove (SuperAdmin, the
        // PD) handles sign-off — deliberately separate permissions/roles.
        private sealed record RecordsUser(int UserId, string? Department, bool CanManage, bool CanApprove);

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
                RolePermissions.Has(role, Permissions.ManageRecords),
                RolePermissions.Has(role, Permissions.ApproveRecords));
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
        // database. An Employee only ever sees the records they logged
        // (CreatedByUserId). Admin (the records officer, ManageRecords) and
        // SuperAdmin (the PD, ApproveRecords) see every record office-wide —
        // deliberately, so disposal validation and approval can actually see
        // the records they're approving/validating, Confidential included.
        // Records with no stored owner (logged before the owner column
        // existed) are visible to nobody but managers/approvers until an
        // owner is assigned in the database.
        private static string VisibilityClause(RecordsUser user) =>
            (user.CanManage || user.CanApprove)
                ? "1 = 1"
                : @"
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
            if (row.RetentionDueDate == null || !string.Equals(row.RecordStatus, "Retention", StringComparison.OrdinalIgnoreCase))
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

        // Who may Edit/Delete a given row: its own creator, or a records
        // manager (Admin). Allowed even once a record is saved into a
        // Masterlist — a saved Masterlist's own Excel file is a frozen
        // snapshot from when it was generated, so correcting the live
        // record afterward doesn't rewrite history, it just means a
        // re-download would differ from what was already handed out.
        // SuperAdmin (approval-only) does not get this on someone else's
        // record, only on one they logged themselves, same as anyone else.
        private static bool CanEditRow(RecordRow row, RecordsUser user) =>
            user.CanManage || row.CreatedByUserId == user.UserId;

        // Disposal is the record owner's call too (not just a records
        // manager's — see SetDisposalStatus), so an Employee needs to see
        // their own record's status and disposal actions the same as a
        // manager/approver would for it. Confidentiality isn't a concern
        // here: VisibilityClause already limits a non-manager/approver to
        // only the records they logged themselves.
        private static bool CanDisposeRow(RecordRow row, RecordsUser user) =>
            user.CanManage || row.CreatedByUserId == user.UserId;

        private static object ToClientRow(RecordRow row, RecordsUser user)
        {
            return new
            {
                row.RecordId, row.Code, row.Title, row.Medium, row.Location,
                row.PeriodCovered, row.FilingSystem, row.AccessControl, row.RetentionPeriod,
                row.RecordStatus,
                row.OwningDepartment,
                RecordDate = row.RecordDate?.ToString("yyyy-MM-dd"),
                RetentionDueDate = row.RetentionDueDate?.ToString("yyyy-MM-dd"),
                RetentionState = RetentionState(row),
                ApprovedByName = row.ApprovedByName,
                ApprovedAt = row.ApprovedAt?.ToString("yyyy-MM-dd"),
                CanEdit = CanEditRow(row, user),
                CanDispose = CanDisposeRow(row, user)
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
            public string? ApprovedByName { get; init; }
            public DateTime? ApprovedAt { get; init; }
            public int? CreatedByUserId { get; init; }
            public int? MasterlistId { get; init; }
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

            // Only decides which columns/buttons are rendered; GetAll and the
            // action endpoints enforce the same rules on their own.
            ViewBag.CanManageRecords = user.CanManage;
            ViewBag.CanApproveRecords = user.CanApprove;
            return View();
        }

        // GET: /Records/Overview — a compact summary for the Dashboard.
        //
        // Admin/Employee: how many active (RecordStatus = 'Retention')
        // records this user has logged, how many are due for disposal
        // review soon or already overdue (same thresholds as the per-row
        // reminder colours, see RetentionState), and how many masterlists
        // they've saved. Scoped the same as everywhere else in Records:
        // only the records this user logged (VisibilityClause).
        //
        // SuperAdmin (CanApprove) sees every record office-wide already
        // (VisibilityClause is "1 = 1" for them), but the overview itself
        // only counts records a records officer has actually raised for
        // the SuperAdmin's attention — RecordStatus IN ('Disposal',
        // 'Disposed'), i.e. MarkForDisposal/MarkDisposed has been called —
        // rather than every still-active Retention record office-wide.
        // "Saved Masterlists" follows the same rule: masterlists that
        // contain at least one such record, not every masterlist anyone
        // has ever saved.
        [HttpGet]
        public IActionResult Overview()
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
                var source = HasSystemColumns(conn) ? "Records r" : LegacyRecordsSource;
                var today = PhilippineToday();
                var statusFilter = user.CanApprove
                    ? "r.RecordStatus IN ('Disposal', 'Disposed')"
                    : "r.RecordStatus = 'Retention'";

                int total, dueSoon, overdue;
                using (var cmd = new SqlCommand($@"
SELECT
    COUNT(*),
    SUM(CASE WHEN r.RetentionDueDate IS NOT NULL AND r.RetentionDueDate <= @Today THEN 1 ELSE 0 END),
    SUM(CASE WHEN r.RetentionDueDate IS NOT NULL AND r.RetentionDueDate > @Today AND r.RetentionDueDate <= @SoonLimit THEN 1 ELSE 0 END)
FROM {source}
WHERE {VisibilityClause(user)} AND {statusFilter}", conn))
                {
                    AddVisibilityParameters(cmd, user);
                    cmd.Parameters.Add("@Today", SqlDbType.Date).Value = today;
                    cmd.Parameters.Add("@SoonLimit", SqlDbType.Date).Value = today.AddMonths(RecordRetentionReminder.ReminderMonths);
                    using var reader = cmd.ExecuteReader();
                    reader.Read();
                    total = reader.GetInt32(0);
                    overdue = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    dueSoon = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                }

                var savedMasterlists = 0;
                if (HasMasterlists(conn))
                {
                    if (user.CanApprove)
                    {
                        using var cmd = new SqlCommand($@"
SELECT COUNT(DISTINCT r.MasterlistId)
FROM {source}
WHERE {VisibilityClause(user)} AND r.MasterlistId IS NOT NULL AND {statusFilter}", conn);
                        AddVisibilityParameters(cmd, user);
                        savedMasterlists = (int)cmd.ExecuteScalar();
                    }
                    else
                    {
                        using var cmd = new SqlCommand(
                            "SELECT COUNT(*) FROM dbo.RecordMasterlists WHERE CreatedByUserId = @UserId", conn);
                        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                        savedMasterlists = (int)cmd.ExecuteScalar();
                    }
                }

                return Json(new { total, dueSoon, overdue, savedMasterlists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load records overview for user {UserId}.", user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericLoadError });
            }
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
            var hasApproval = HasApprovalColumns(conn);
            var approvalSelect = hasApproval ? ", au.FullName AS ApprovedByName, r.ApprovedAt" : ", CAST(NULL AS nvarchar(200)) AS ApprovedByName, CAST(NULL AS datetime2) AS ApprovedAt";
            var approvalJoin = hasApproval ? " LEFT JOIN Users au ON au.Id = r.ApprovedByUserId" : "";

            var hasMasterlistColumn = HasMasterlists(conn);
            var masterlistSelect = hasMasterlistColumn ? ", r.MasterlistId" : ", CAST(NULL AS int) AS MasterlistId";

            var sql = new System.Text.StringBuilder(@"
SELECT r.RecordId, r.Code, r.Title, r.Medium, r.Location, r.PeriodCovered,
       r.FilingSystem, r.AccessControl, r.RetentionPeriod,
       r.RecordStatus, r.OwningDepartment, r.RecordDate, r.RetentionDueDate")
                .Append(approvalSelect).Append(", r.CreatedByUserId").Append(masterlistSelect).Append(@"
FROM ").Append(source).Append(approvalJoin).Append(@"
WHERE ").Append(VisibilityClause(user));

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
                        RetentionDueDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                        ApprovedByName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        ApprovedAt = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                        CreatedByUserId = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                        MasterlistId = reader.IsDBNull(16) ? null : reader.GetInt32(16)
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

            var result = TryInsertRecord(entry, user);
            return result.Status switch
            {
                RecordInsertStatus.Ok => Json(ToClientRow(result.Saved!, user)),
                RecordInsertStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
                RecordInsertStatus.ServerError => StatusCode(StatusCodes.Status500InternalServerError, new { message = result.ErrorMessage }),
                _ => BadRequest(new { message = result.ErrorMessage })
            };
        }

        private enum RecordInsertStatus { Ok, BadRequest, Conflict, ServerError }

        private sealed record RecordInsertResult(RecordInsertStatus Status, RecordRow? Saved, string? ErrorMessage);

        private sealed record ValidatedFields(
            string Code, string Title, string Location, string PeriodCovered, string RetentionPeriod,
            string Medium, string FilingSystem, string AccessControl);

        // Shared by TryInsertRecord and TryUpdateRecord — the same field
        // rules apply whether a record is being logged for the first time or
        // corrected afterward.
        private static (ValidatedFields? Fields, string? Error) ValidateEntry(RecordEntry entry)
        {
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
                return (null, textError);
            }

            var medium = NormalizeChoice(entry.Medium, Media);
            if (medium == null)
            {
                return (null, "Please choose or type a Medium.");
            }

            var filingSystem = NormalizeChoice(entry.FilingSystem, FilingSystems);
            if (filingSystem == null)
            {
                return (null, "Please choose or type a Filing System.");
            }

            var accessControl = NormalizeChoice(entry.AccessControl, AccessLevels);
            if (accessControl == null)
            {
                return (null, "Please choose or type an Access Control.");
            }

            return (new ValidatedFields(code, title, location, periodCovered, retentionPeriod, medium, filingSystem, accessControl), null);
        }

        // The actual validation-and-insert logic behind Save, extracted so
        // ImportMasterlist can run the exact same rules per row instead of a
        // separate, looser copy. Every status/message here is unchanged from
        // the single-entry Save path that existed before this was extracted.
        private RecordInsertResult TryInsertRecord(RecordEntry entry, RecordsUser user)
        {
            var (validated, validationError) = ValidateEntry(entry);
            if (validated == null)
            {
                return new RecordInsertResult(RecordInsertStatus.BadRequest, null, validationError);
            }
            var (code, title, location, periodCovered, retentionPeriod, medium, filingSystem, accessControl) =
                (validated.Code, validated.Title, validated.Location, validated.PeriodCovered, validated.RetentionPeriod,
                 validated.Medium, validated.FilingSystem, validated.AccessControl);

            var recordDate = PhilippineToday();
            var retentionDueDate = RetentionPeriodParser.TryComputeDueDate(recordDate, periodCovered, retentionPeriod);
            const string recordStatus = "Retention";

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
                        return new RecordInsertResult(RecordInsertStatus.ServerError, null, GenericSaveError);
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

                return new RecordInsertResult(RecordInsertStatus.Ok, saved, null);
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // Unique constraint violation on Code
                return new RecordInsertResult(RecordInsertStatus.Conflict, null, $"The Code \"{code}\" already exists. Please use a different Code.");
            }
            catch (SqlException ex) when (ex.Number == 8152 || ex.Number == 2628)
            {
                // String or binary data would be truncated
                return new RecordInsertResult(RecordInsertStatus.BadRequest, null, "One of the entries is too long. Please shorten it and try again.");
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
                return new RecordInsertResult(RecordInsertStatus.BadRequest, null,
                    "The database does not accept one of the selected Medium, Filing System, or Access Control values yet. Please contact the administrator.");
            }
            catch (Exception ex)
            {
                // Full details go to the server log only (SqlException messages
                // carry no connection string); the user gets a generic message.
                _logger.LogError(ex, "Failed to save record with Code {Code} for user {UserId}.", code, user.UserId);
                return new RecordInsertResult(RecordInsertStatus.ServerError, null, GenericSaveError);
            }
        }

        // Ownership/state needed to authorize Update/Delete, read fresh from
        // the database rather than trusted from the client. RecordDate comes
        // back too so Update can recompute RetentionDueDate from the record's
        // real logging date, not "today" — editing an old record must not
        // silently shift its due date to be computed as if logged today.
        private (int? CreatedByUserId, DateTime? RecordDate)? LoadRecordState(SqlConnection conn, int recordId)
        {
            var hasSystemColumns = HasSystemColumns(conn);
            var createdByCol = hasSystemColumns ? "CreatedByUserId" : "NULL";
            var recordDateCol = hasSystemColumns ? "RecordDate" : "NULL";
            using var cmd = new SqlCommand($"SELECT {createdByCol}, {recordDateCol} FROM Records WHERE RecordId = @Id", conn);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = recordId;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            return (
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetDateTime(1)
            );
        }

        // POST: /Records/Update — corrects a record already logged. Allowed
        // for its own creator or a records manager (Admin), and only while
        // it's still on the working table (not yet saved into a Masterlist —
        // see CanEditRow). Clears any PD approval on a successful edit: an
        // approval recorded against the old content shouldn't silently carry
        // over to the new content.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Update([FromBody] RecordEntry? entry)
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }
            if (entry == null)
            {
                return BadRequest(new { message = "Please fill in every field." });
            }

            var (validated, validationError) = ValidateEntry(entry);
            if (validated == null)
            {
                return BadRequest(new { message = validationError });
            }
            var (code, title, location, periodCovered, retentionPeriod, medium, filingSystem, accessControl) =
                (validated.Code, validated.Title, validated.Location, validated.PeriodCovered, validated.RetentionPeriod,
                 validated.Medium, validated.FilingSystem, validated.AccessControl);

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                var state = LoadRecordState(conn, entry.RecordId);
                if (state == null)
                {
                    return NotFound(new { message = "Record not found." });
                }
                var (createdByUserId, recordDate) = state.Value;

                if (!user.CanManage && createdByUserId != user.UserId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only edit a record you logged yourself." });
                }

                var hasSystemColumns = HasSystemColumns(conn);
                var hasApproval = HasApprovalColumns(conn);
                var retentionDueDate = hasSystemColumns
                    ? RetentionPeriodParser.TryComputeDueDate(recordDate ?? PhilippineToday(), periodCovered, retentionPeriod)
                    : (DateTime?)null;

                var setClauses = new List<string>
                {
                    "Code=@Code", "Title=@Title", "Medium=@Medium", "Location=@Location",
                    "PeriodCovered=@PeriodCovered", "FilingSystem=@FilingSystem",
                    "AccessControl=@AccessControl", "RetentionPeriod=@RetentionPeriod"
                };
                if (hasSystemColumns)
                {
                    setClauses.Add("RetentionDueDate=@RetentionDueDate");
                }
                if (hasApproval)
                {
                    setClauses.Add("ApprovedByUserId=NULL");
                    setClauses.Add("ApprovedAt=NULL");
                }

                using var cmd = new SqlCommand($"UPDATE Records SET {string.Join(", ", setClauses)} WHERE RecordId=@RecordId", conn);
                cmd.Parameters.AddWithValue("@Code", code);
                cmd.Parameters.AddWithValue("@Title", title);
                cmd.Parameters.AddWithValue("@Medium", medium);
                cmd.Parameters.AddWithValue("@Location", location);
                cmd.Parameters.AddWithValue("@PeriodCovered", periodCovered);
                cmd.Parameters.AddWithValue("@FilingSystem", filingSystem);
                cmd.Parameters.AddWithValue("@AccessControl", accessControl);
                cmd.Parameters.AddWithValue("@RetentionPeriod", retentionPeriod);
                cmd.Parameters.Add("@RecordId", SqlDbType.Int).Value = entry.RecordId;
                if (hasSystemColumns)
                {
                    cmd.Parameters.Add("@RetentionDueDate", SqlDbType.Date).Value = (object?)retentionDueDate ?? DBNull.Value;
                }

                var rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return NotFound(new { message = "Record not found." });
                }

                return Json(new { ok = true });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return Conflict(new { message = $"The Code \"{code}\" already exists. Please use a different Code." });
            }
            catch (SqlException ex) when (ex.Number == 8152 || ex.Number == 2628)
            {
                return BadRequest(new { message = "One of the entries is too long. Please shorten it and try again." });
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                _logger.LogError(ex, "Database constraint rejected an update to record {RecordId}.", entry.RecordId);
                return BadRequest(new { message = "The database does not accept one of the selected Medium, Filing System, or Access Control values yet. Please contact the administrator." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update record {RecordId}.", entry.RecordId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
            }
        }

        // POST: /Records/Delete/{id} — same ownership rule as Update:
        // creator or records manager. Allowed even once the record is saved
        // into a Masterlist (see CanEditRow).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
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

                var state = LoadRecordState(conn, id);
                if (state == null)
                {
                    return NotFound(new { message = "Record not found." });
                }
                var (createdByUserId, _) = state.Value;

                if (!user.CanManage && createdByUserId != user.UserId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only delete a record you logged yourself." });
                }

                using var cmd = new SqlCommand("DELETE FROM Records WHERE RecordId = @Id", conn);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                var rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return NotFound(new { message = "Record not found." });
                }

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete record {RecordId}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
            }
        }

        // POST: /Records/Approve/{id} — the PD's (SuperAdmin's) sign-off.
        // Does not gate anything else: the record stays fully usable either
        // way, this just records that the PD looked at it, same as the real
        // office process — never a hard block that could strand a record if
        // the PD hasn't clicked it yet.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(int id)
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }
            if (!user.CanApprove)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only the Provincial Director can approve records." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                if (!HasApprovalColumns(conn))
                {
                    _logger.LogError("Refused to approve record {RecordId}: Records approval columns are missing.", id);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
                }

                using var cmd = new SqlCommand(@"
UPDATE Records SET ApprovedByUserId = @ApproverId, ApprovedAt = SYSUTCDATETIME()
WHERE RecordId = @RecordId", conn);
                cmd.Parameters.Add("@ApproverId", SqlDbType.Int).Value = user.UserId;
                cmd.Parameters.Add("@RecordId", SqlDbType.Int).Value = id;
                var rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return NotFound(new { message = "Record not found." });
                }
                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to approve record {RecordId} for user {UserId}.", id, user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
            }
        }

        // POST: /Records/MarkForDisposal/{id} — a manual early flag into
        // Disposal status. Normally unnecessary: RecordRetentionReminder's
        // background run already promotes a Retention record to Disposal
        // once its due date is within the reminder window, matching the
        // "near or needs disposal" meaning the office gave this status. This
        // stays available for records with no computable due date (event-based
        // retention text), which the background run can never promote on its own.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> MarkForDisposal(int id) => SetDisposalStatus(id, "Disposal");

        // POST: /Records/MarkDisposed/{id} — the records officer's (Admin's)
        // confirmation that a record has actually been physically disposed of.
        // This step stays manual: the system has no way to know disposal truly
        // happened, only that it is due.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> MarkDisposed(int id) => SetDisposalStatus(id, "Disposed");

        // POST: /Records/MarkArchived/{id} — the other real outcome once a
        // record's retention period is due, alongside Disposal: under RA
        // 9470/NAP's own vocabulary, a record of permanent value is
        // preserved at the National Archives instead of disposed of. The
        // office's own retention schedule already states, per record
        // series, whether it is temporary (eventually disposed) or
        // permanent (archived) — this is that second outcome, not a
        // synonym for Disposed. No disposal notification fires for it:
        // archiving isn't a disposal action, it's the opposite of one.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> MarkArchived(int id) => SetDisposalStatus(id, "Archived");

        private async Task<IActionResult> SetDisposalStatus(int id, string newStatus)
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
                if (!HasSystemColumns(conn))
                {
                    _logger.LogError("Refused to set record {RecordId} status to {Status}: Records system columns are missing.", id, newStatus);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
                }

                // Disposal is the "product owner's" call (Say, OPD, chat on
                // 2026-10-23: the account person who logged the record keeps
                // it and decides when it's due for disposal — not Imran, not
                // the PD, and not records managers generically). Allowed for
                // the record's own creator or a records manager (Admin), same
                // ownership rule as CanEditRow.
                var state = LoadRecordState(conn, id);
                if (state == null)
                {
                    return NotFound(new { message = "Record not found." });
                }
                var (ownerOfRecord, _) = state.Value;
                if (!user.CanManage && ownerOfRecord != user.UserId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only the record's owner or a records manager can update disposal status." });
                }

                int? ownerUserId = null;
                string? code = null, title = null;
                using (var cmd = new SqlCommand(@"
UPDATE Records SET RecordStatus = @Status
OUTPUT inserted.CreatedByUserId, inserted.Code, inserted.Title
WHERE RecordId = @RecordId", conn))
                {
                    cmd.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = newStatus;
                    cmd.Parameters.Add("@RecordId", SqlDbType.Int).Value = id;
                    using var reader = cmd.ExecuteReader();
                    if (!reader.Read())
                    {
                        return NotFound(new { message = "Record not found." });
                    }
                    ownerUserId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
                    code = reader.GetString(1);
                    title = reader.GetString(2);
                }

                // No owner on a legacy record (logged before CreatedByUserId
                // existed) — nobody to notify, same as the reminder job.
                if (ownerUserId.HasValue)
                {
                    if (newStatus == "Disposal")
                    {
                        await _notifications.NotifyRecordMarkedForDisposalAsync(ownerUserId.Value, id, code!, title!);
                    }
                    else if (newStatus == "Disposed")
                    {
                        await _notifications.NotifyRecordDisposedAsync(ownerUserId.Value, id, code!, title!);
                    }
                }

                return Json(new { ok = true, status = newStatus });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set record {RecordId} status to {Status} for user {UserId}.", id, newStatus, user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericSaveError });
            }
        }

        // ── Masterlist import ───────────────────────────────────────────
        // POST: /Records/ImportMasterlist — reads an uploaded .xlsx built in
        // the exact layout RecordMasterlistExcel produces (title block, a
        // header row with CODE/TITLE OF RECORD/.../RETENTION PERIOD, data
        // rows, then a Prepared/Reviewed/Noted-by block) and inserts each
        // data row through the same TryInsertRecord path Save uses — so a
        // row that would be rejected by hand is rejected the same way here.
        private const long MaxImportFileBytes = 10 * 1024 * 1024; // 10 MB, same figure already used for proof uploads
        private const int MaxHeaderScanRows = 20;
        private static readonly string[] ExpectedHeaders =
        {
            "CODE", "TITLE OF RECORD", "MEDIUM", "LOCATION", "PERIOD COVERED",
            "FILING SYSTEM", "ACCESS CONTROL", "RETENTION PERIOD"
        };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ImportMasterlist(IFormFile? file)
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Please choose an Excel (.xlsx) masterlist file to import." });
            }
            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only .xlsx files are supported." });
            }
            if (file.Length > MaxImportFileBytes)
            {
                return BadRequest(new { message = "That file is too large (maximum 10 MB)." });
            }

            ClosedXML.Excel.IXLWorksheet worksheet;
            try
            {
                using var stream = file.OpenReadStream();
                using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
                worksheet = workbook.Worksheets.First();

                var headerRow = 0;
                for (var r = 1; r <= MaxHeaderScanRows; r++)
                {
                    if (string.Equals(worksheet.Cell(r, 1).GetString().Trim(), "CODE", StringComparison.OrdinalIgnoreCase))
                    {
                        headerRow = r;
                        break;
                    }
                }
                if (headerRow == 0)
                {
                    return BadRequest(new { message = "Could not find the masterlist header row (expecting CODE, TITLE OF RECORD, ...). Please use a masterlist exported from this system." });
                }

                for (var c = 0; c < ExpectedHeaders.Length; c++)
                {
                    var actual = worksheet.Cell(headerRow, c + 1).GetString().Trim();
                    if (!string.Equals(actual, ExpectedHeaders[c], StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest(new { message = $"Unexpected column \"{actual}\" where \"{ExpectedHeaders[c]}\" was expected. Please use a masterlist exported from this system." });
                    }
                }

                var imported = new List<object>();
                var skipped = new List<object>();
                var row = headerRow + 1;
                while (true)
                {
                    var codeCell = worksheet.Cell(row, 1).GetString().Trim();
                    if (string.IsNullOrEmpty(codeCell) || string.Equals(codeCell, "Prepared by:", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    var entry = new RecordEntry
                    {
                        Code = codeCell,
                        Title = worksheet.Cell(row, 2).GetString().Trim(),
                        Medium = worksheet.Cell(row, 3).GetString().Trim(),
                        Location = worksheet.Cell(row, 4).GetString().Trim(),
                        PeriodCovered = worksheet.Cell(row, 5).GetString().Trim(),
                        FilingSystem = worksheet.Cell(row, 6).GetString().Trim(),
                        AccessControl = worksheet.Cell(row, 7).GetString().Trim(),
                        RetentionPeriod = worksheet.Cell(row, 8).GetString().Trim()
                    };

                    var result = TryInsertRecord(entry, user);
                    if (result.Status == RecordInsertStatus.Ok)
                    {
                        imported.Add(ToClientRow(result.Saved!, user));
                    }
                    else
                    {
                        skipped.Add(new { row, reason = result.ErrorMessage });
                    }

                    row++;
                }

                return Json(new { imported, skipped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read uploaded masterlist file {FileName} for user {UserId}.", file.FileName, user.UserId);
                return BadRequest(new { message = "Could not read that file. Please confirm it's a masterlist exported from this system (.xlsx)." });
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

                using var cmd = new SqlCommand(@"
SELECT TOP (@Limit) MasterlistId, FileName, CreatedAt, RecordCount,
       PreparedByName, PreparedByPosition, ReviewedByName, ReviewedByPosition, NotedByName, NotedByPosition
FROM dbo.RecordMasterlists
WHERE CreatedByUserId = @UserId
ORDER BY CreatedAt DESC, MasterlistId DESC", conn);
                cmd.Parameters.Add("@Limit", SqlDbType.Int).Value = MasterlistHistoryLimit;
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

        // GET: /Records/PrintMasterlist?id= — a PDF of the saved masterlist,
        // built server-side (Services/RecordMasterlistPdf.cs) from the exact
        // same layout as the Excel file, opened inline so the browser's own
        // PDF viewer prints it pixel-for-pixel instead of an HTML approximation.
        [HttpGet]
        public IActionResult PrintMasterlist(int id)
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

                string division;
                DateTime createdAtUtc;
                RecordMasterlistExcel.Signatory prepared, reviewed, noted;
                using (var cmd = new SqlCommand(@"
SELECT OwningDepartment, CreatedAt,
       PreparedByName, PreparedByPosition, ReviewedByName, ReviewedByPosition, NotedByName, NotedByPosition
FROM dbo.RecordMasterlists
WHERE MasterlistId = @Id AND CreatedByUserId = @UserId", conn))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                    using var reader = cmd.ExecuteReader();
                    if (!reader.Read())
                    {
                        return NotFound();
                    }
                    division = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    createdAtUtc = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
                    prepared = new RecordMasterlistExcel.Signatory(reader.GetString(2), reader.GetString(3));
                    reviewed = new RecordMasterlistExcel.Signatory(reader.GetString(4), reader.GetString(5));
                    noted = new RecordMasterlistExcel.Signatory(reader.GetString(6), reader.GetString(7));
                }

                var rows = new List<RecordMasterlistExcel.Row>();
                using (var cmd = new SqlCommand(@"
SELECT Code, Title, Medium, Location, PeriodCovered, FilingSystem, AccessControl, RetentionPeriod
FROM dbo.Records
WHERE MasterlistId = @Id AND CreatedByUserId = @UserId
ORDER BY RecordId", conn))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        rows.Add(new RecordMasterlistExcel.Row(
                            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
                    }
                }

                var pdf = RecordMasterlistPdf.Build(new RecordMasterlistExcel.Sheet(
                    division, TimeZoneHelper.ToPhilippineTime(createdAtUtc).Date, prepared, reviewed, noted, rows));

                Response.Headers.Append("Content-Disposition", "inline; filename=masterlist.pdf");
                return File(pdf, RecordMasterlistPdf.ContentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to print masterlist {MasterlistId} for user {UserId}.", id, user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        public sealed class RenameMasterlistRequest
        {
            public string? FileName { get; set; }
        }

        // POST: /Records/RenameMasterlist?id= — renames a saved masterlist entry.
        // The file itself is a database blob (see SaveMasterlist); only the
        // FileName shown in Saved Masterlists and used on download changes.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RenameMasterlist(int id, [FromBody] RenameMasterlistRequest? request)
        {
            var access = CheckAccess(out var user);
            if (access != RecordsAccess.Allowed || user == null)
            {
                return AccessDeniedJson(access);
            }

            var newName = request?.FileName?.Trim() ?? "";
            if (newName.Length == 0)
            {
                return BadRequest(new { message = "Please enter a file name." });
            }
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return BadRequest(new { message = "That file name has characters that aren't allowed." });
            }
            if (!newName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                newName += ".xlsx";
            }
            if (newName.Length > 150)
            {
                return BadRequest(new { message = "That file name is too long." });
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
UPDATE dbo.RecordMasterlists SET FileName = @FileName
WHERE MasterlistId = @Id AND CreatedByUserId = @UserId", conn);
                cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 150).Value = newName;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
                var rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                {
                    return NotFound(new { message = "That masterlist could not be found." });
                }

                return Json(new { fileName = newName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename masterlist {MasterlistId} for user {UserId}.", id, user.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = GenericMasterlistError });
            }
        }
    }
}
