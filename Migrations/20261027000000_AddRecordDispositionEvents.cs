using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // One row per disposal-status change (RecordsController.SetDisposalStatus
    // inserts it in the same transaction as the status update, via
    // RecordDisposalWorkflow's gated transitions — decision 8: raw SQL,
    // same as every other Records table, since Records is not part of the
    // EF model). Widens Records.RecordStatus from nvarchar(20): the two
    // longest new pipeline states ("Awaiting NAP Authority", "Authorized
    // for Disposal") don't fit in 20. AttachmentId is intentionally not an
    // FK yet — the attachments table it will eventually point to ("Records
    // — masterlist attachments & file browser") doesn't exist until that
    // feature ships.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261027000000_AddRecordDispositionEvents")]
    /// <inheritdoc />
    public partial class AddRecordDispositionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RecordStatus') IS NOT NULL
    ALTER TABLE dbo.Records ALTER COLUMN RecordStatus nvarchar(30) NOT NULL;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.RecordDispositionEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecordDispositionEvents (
        Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecordDispositionEvents PRIMARY KEY,
        RecordId           int           NOT NULL,
        EventType          nvarchar(30)  NOT NULL,
        OccurredAt         datetime2     NOT NULL CONSTRAINT DF_RecordDispositionEvents_OccurredAt DEFAULT SYSUTCDATETIME(),
        ActorUserId        int           NOT NULL,
        AuthorityReference nvarchar(200) NULL,
        Notes              nvarchar(1000) NULL,
        AttachmentId       int           NULL,
        CONSTRAINT FK_RecordDispositionEvents_Records_RecordId
            FOREIGN KEY (RecordId) REFERENCES dbo.Records (RecordId) ON DELETE CASCADE,
        CONSTRAINT FK_RecordDispositionEvents_Users_ActorUserId
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_RecordDispositionEvents_RecordId ON dbo.RecordDispositionEvents (RecordId, OccurredAt);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.RecordDispositionEvents', N'U') IS NOT NULL
    DROP TABLE dbo.RecordDispositionEvents;");

            // Not reverted: a NOT NULL nvarchar(30) safely holds every value
            // nvarchar(20) could, so narrowing back on rollback risks
            // truncating a row written at the wider length in the meantime.
        }
    }
}
