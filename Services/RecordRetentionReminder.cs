using DTIOneLink.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace DTIOneLink.Services
{
    // Creates "review for disposal" notifications for records whose retention
    // period ends within ReminderMonths (or has already ended), and promotes
    // those Retention records to Disposal status in the background run. The
    // recipient is the record's owner — the user who logged it
    // (Records.CreatedByUserId).
    //
    // Used from two places so reminders show up promptly:
    //  - RecordRetentionReminderService: every hour, for all owners.
    //  - NotificationsController.List: for the signed-in user whenever their
    //    notification bell loads, so a reminder appears on their next page
    //    view instead of waiting for the hourly run.
    //
    // Records is not in the EF model, so it is read with raw SQL like
    // RecordsController. Only records with a computed RetentionDueDate are
    // covered — event-based retention text has no date to count from.
    public class RecordRetentionReminder
    {
        public const int ReminderMonths = 6;

        private readonly string? _connectionString;
        private readonly NotificationService _notifications;
        private readonly ILogger<RecordRetentionReminder> _logger;

        public RecordRetentionReminder(
            IConfiguration configuration,
            NotificationService notifications,
            ILogger<RecordRetentionReminder> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _notifications = notifications;
            _logger = logger;
        }

        private sealed record DueRecord(int RecordId, string Code, string Title, int OwnerUserId, DateTime RetentionDueDate);

        // ownerUserId null = every owner (background run); otherwise only that
        // user's records. Returns how many reminders were sent.
        public async Task<int> SendDueRemindersAsync(int? ownerUserId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                return 0;
            }

            // Retention dates are calendar dates in the office's time zone.
            var today = TimeZoneHelper.ToPhilippineTime(DateTime.UtcNow).Date;
            var notifyThrough = today.AddMonths(ReminderMonths);

            var dueRecords = new List<DueRecord>();
            await using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(cancellationToken);

                if (!await HasRetentionColumnsAsync(conn, cancellationToken))
                {
                    return 0;
                }

                // Background run only: keep every Retention record's due date
                // in line with the current counting rule (end of Period
                // Covered, else the date logged) — this also corrects records
                // saved before the rule existed — then promote any that are
                // now due into Disposal status.
                if (ownerUserId == null)
                {
                    await RecalculateDueDatesAsync(conn, cancellationToken);
                    await PromoteDueRecordsToDisposalAsync(conn, notifyThrough, cancellationToken);
                }

                // Retention or Disposal records whose retention ends on or
                // before six months from today, owned by an active user, not
                // yet reminded. Disposal is included because a per-user
                // bell-load run (ownerUserId != null) can land between
                // background runs, before this user's own records have been
                // promoted yet.
                await using var cmd = new SqlCommand(@"
SELECT r.RecordId, r.Code, r.Title, r.CreatedByUserId, r.RetentionDueDate
FROM dbo.Records r
JOIN dbo.Users u ON u.Id = r.CreatedByUserId AND u.IsActive = 1
WHERE r.RecordStatus IN (N'Retention', N'Disposal')
  AND r.RetentionDueDate IS NOT NULL
  AND r.RetentionDueDate <= @NotifyThrough
  AND (@OwnerUserId IS NULL OR r.CreatedByUserId = @OwnerUserId)
  AND NOT EXISTS (
        SELECT 1 FROM dbo.Notifications n
        WHERE n.RecipientUserId = r.CreatedByUserId
          AND n.RelatedRecordId = r.RecordId
          AND n.Type = @Type)", conn);
                cmd.Parameters.Add("@NotifyThrough", SqlDbType.Date).Value = notifyThrough;
                cmd.Parameters.Add("@OwnerUserId", SqlDbType.Int).Value = (object?)ownerUserId ?? DBNull.Value;
                cmd.Parameters.Add("@Type", SqlDbType.Int).Value = (int)NotificationType.RecordDisposalDue;

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    dueRecords.Add(new DueRecord(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader.GetDateTime(4)));
                }
            }

            var sentCount = 0;
            foreach (var record in dueRecords)
            {
                // One record's notification failing (e.g. a transient DB
                // error) must not stop every other record due a reminder in
                // this same run from getting theirs.
                try
                {
                    await _notifications.NotifyRecordDisposalDueAsync(
                        record.OwnerUserId, record.RecordId, record.Code, record.Title, record.RetentionDueDate, today);
                    sentCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Could not send a disposal reminder for record {RecordId}.", record.RecordId);
                }
            }

            if (sentCount > 0)
            {
                _logger.LogInformation("Sent {Count} record disposal reminder(s).", sentCount);
            }

            return sentCount;
        }

        // Recomputes RetentionDueDate for Retention records and saves any that
        // changed. A changed record's earlier disposal reminder is removed —
        // it named the old date — so a correct one is sent when it is due.
        private async Task RecalculateDueDatesAsync(SqlConnection conn, CancellationToken cancellationToken)
        {
            var changes = new List<(int RecordId, DateTime? NewDueDate)>();

            await using (var read = new SqlCommand(@"
SELECT RecordId, RecordDate, PeriodCovered, RetentionPeriod, RetentionDueDate
FROM dbo.Records
WHERE RecordStatus = N'Retention' AND RecordDate IS NOT NULL", conn))
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var recordDate = reader.GetDateTime(1);
                    var periodCovered = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var retentionPeriod = reader.IsDBNull(3) ? null : reader.GetString(3);
                    DateTime? stored = reader.IsDBNull(4) ? null : reader.GetDateTime(4).Date;

                    var computed = RetentionPeriodParser.TryComputeDueDate(recordDate, periodCovered, retentionPeriod)?.Date;
                    if (computed != stored)
                    {
                        changes.Add((reader.GetInt32(0), computed));
                    }
                }
            }

            if (changes.Count == 0)
            {
                return;
            }

            await using var transaction = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);
            foreach (var (recordId, newDueDate) in changes)
            {
                await using var update = new SqlCommand(@"
UPDATE dbo.Records SET RetentionDueDate = @Due WHERE RecordId = @Id;
DELETE FROM dbo.Notifications WHERE RelatedRecordId = @Id AND Type = @Type;", conn, transaction);
                update.Parameters.Add("@Due", SqlDbType.Date).Value = (object?)newDueDate ?? DBNull.Value;
                update.Parameters.Add("@Id", SqlDbType.Int).Value = recordId;
                update.Parameters.Add("@Type", SqlDbType.Int).Value = (int)NotificationType.RecordDisposalDue;
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Recalculated the retention end date of {Count} record(s).", changes.Count);
        }

        // Moves a Retention record into Disposal once its retention period
        // ends within the reminder window (or has already ended) — the
        // office's own definition of Disposal: "near or needs disposal".
        // Retention records with no computable due date (event-based
        // retention text) are never promoted automatically; MarkForDisposal
        // remains available for those.
        private async Task PromoteDueRecordsToDisposalAsync(SqlConnection conn, DateTime notifyThrough, CancellationToken cancellationToken)
        {
            await using var update = new SqlCommand(@"
UPDATE dbo.Records
SET RecordStatus = N'Disposal'
WHERE RecordStatus = N'Retention'
  AND RetentionDueDate IS NOT NULL
  AND RetentionDueDate <= @NotifyThrough", conn);
            update.Parameters.Add("@NotifyThrough", SqlDbType.Date).Value = notifyThrough;
            var promoted = await update.ExecuteNonQueryAsync(cancellationToken);

            if (promoted > 0)
            {
                _logger.LogInformation("Promoted {Count} record(s) from Retention to Disposal.", promoted);
            }
        }

        // The owner/status/due-date columns come from migration
        // 20260925000000_AddRecordSystemFields; skip quietly without them.
        private static async Task<bool> HasRetentionColumnsAsync(SqlConnection conn, CancellationToken cancellationToken)
        {
            await using var cmd = new SqlCommand(@"
SELECT COUNT(*) FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.Records')
  AND name IN (N'CreatedByUserId', N'RecordStatus', N'RetentionDueDate')", conn);
            var count = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
            return count == 3;
        }
    }
}
