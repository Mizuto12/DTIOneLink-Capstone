using Microsoft.Data.SqlClient;

namespace DTIOneLink.Services
{
    // Short fingerprints of the data pages show, used for live updates
    // (LiveChangeBroadcaster pushes them, LiveController answers them).
    // A fingerprint changes whenever a row is added, removed or edited, by
    // any part of the app (EF or raw SQL) or by hand in the database.
    public class LiveVersionService
    {
        // Every table a page shows, folded into one number per table.
        // BINARY_CHECKSUM(*) covers each row's values, so an edit shows up
        // even when no row is added or removed. Takes a few milliseconds.
        private const string DataSql = @"
            SELECT CONCAT_WS('-',
                (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.TaskItems),
                (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.TaskAssignments),
                (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.TaskSubmissions),
                (SELECT COUNT_BIG(*) FROM dbo.TaskActivities),
                (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.TaskComments),
                (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.Records),
                (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM dbo.RecordMasterlists),
                (SELECT CHECKSUM_AGG(BINARY_CHECKSUM(Id, FullName, Email, Role, Department, IsActive)) FROM dbo.Users));";

        private const string NotificationsSql = @"
            SELECT RecipientUserId,
                   CONCAT_WS('-', COUNT_BIG(*), MAX(Id), SUM(CASE WHEN IsRead = 0 THEN 1 ELSE 0 END))
            FROM dbo.Notifications
            WHERE IsDismissed = 0 AND (@UserId IS NULL OR RecipientUserId = @UserId)
            GROUP BY RecipientUserId;";

        private readonly DatabaseHelper _db;

        public LiveVersionService(DatabaseHelper db)
        {
            _db = db;
        }

        // The data fingerprint, plus each user's notification fingerprint
        // (only userId's when given; users with no notifications are absent).
        public async Task<(string Data, Dictionary<int, string> Notifications)> GetAsync(int? userId, CancellationToken ct)
        {
            using var conn = _db.GetConnection();
            await conn.OpenAsync(ct);
            using var cmd = new SqlCommand(DataSql + NotificationsSql, conn);
            cmd.Parameters.Add(new SqlParameter("@UserId", System.Data.SqlDbType.Int) { Value = (object?)userId ?? DBNull.Value });

            using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var data = reader.GetString(0);

            var notifications = new Dictionary<int, string>();
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                notifications[reader.GetInt32(0)] = reader.GetString(1);
            }
            return (data, notifications);
        }
    }
}
