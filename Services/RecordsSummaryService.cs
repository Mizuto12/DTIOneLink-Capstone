using Microsoft.Data.SqlClient;
using System.Data;

namespace DTIOneLink.Services
{
    // Records totals for the dashboards' "Records Overview" card. Counts only:
    // no codes, titles or names ever leave here, so each record stays visible
    // only to the person who logged it (the Records page's rule) while
    // Admins still get a department view and the SuperAdmin an office view.
    //
    // Uses the same "today" and the same 6-month window as the Records page's
    // yellow/red rows and the disposal reminder (RecordRetentionReminder).
    public class RecordsSummaryService
    {
        public sealed record RecordsSummary(int Total, int LoggedThisMonth, int RetentionEndingSoon, int RetentionEnded);

        private readonly DatabaseHelper _db;
        private readonly ILogger<RecordsSummaryService> _logger;

        public RecordsSummaryService(DatabaseHelper db, ILogger<RecordsSummaryService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // Records logged by anyone in this department.
        public Task<RecordsSummary?> ForDepartmentAsync(string department) => LoadAsync(department.Trim());

        // Every record in the office (SuperAdmin).
        public Task<RecordsSummary?> OfficeWideAsync() => LoadAsync(department: null);

        // Null when the Records table can't be read — the dashboard still
        // loads and the card says the totals are unavailable.
        private async Task<RecordsSummary?> LoadAsync(string? department)
        {
            const string sql = @"
SELECT COUNT(*),
       SUM(CASE WHEN r.RecordDate >= @MonthStart THEN 1 ELSE 0 END),
       SUM(CASE WHEN r.RecordStatus = N'Active' AND r.RetentionDueDate > @Today
                 AND r.RetentionDueDate <= @SoonLimit THEN 1 ELSE 0 END),
       SUM(CASE WHEN r.RecordStatus = N'Active' AND r.RetentionDueDate <= @Today THEN 1 ELSE 0 END)
FROM dbo.Records r
WHERE @Department IS NULL OR r.OwningDepartment = @Department";

            var today = TimeZoneHelper.PhilippineToday;
            try
            {
                await using var conn = _db.GetConnection();
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add("@Department", SqlDbType.NVarChar, 50).Value = (object?)department ?? DBNull.Value;
                cmd.Parameters.Add("@Today", SqlDbType.Date).Value = today;
                cmd.Parameters.Add("@MonthStart", SqlDbType.Date).Value = new DateTime(today.Year, today.Month, 1);
                cmd.Parameters.Add("@SoonLimit", SqlDbType.Date).Value = today.AddMonths(RecordRetentionReminder.ReminderMonths);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return new RecordsSummary(0, 0, 0, 0);

                // SUM over zero rows is NULL.
                int Count(int i) => reader.IsDBNull(i) ? 0 : reader.GetInt32(i);
                return new RecordsSummary(Count(0), Count(1), Count(2), Count(3));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Could not load the records summary for {Scope}.", department ?? "the whole office");
                return null;
            }
        }
    }
}
