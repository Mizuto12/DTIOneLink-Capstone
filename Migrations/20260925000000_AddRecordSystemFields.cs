using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // The Records table is not part of the EF model — RecordsController reads
    // and writes it with raw SQL — so this migration uses guarded SQL instead
    // of AddColumn, and the model snapshot is intentionally unchanged.
    //
    // Everything here is additive and safe on a populated table: new columns
    // are nullable (or NOT NULL with a default), nothing is dropped or
    // rewritten, and each step checks whether it already ran.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260925000000_AddRecordSystemFields")]
    /// <inheritdoc />
    public partial class AddRecordSystemFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Databases that never had the hand-made Records table (e.g. a
            // teammate's fresh database) get it here, matching the columns
            // RecordsController already expects. Existing tables are untouched.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Records', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Records (
        RecordId        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Records PRIMARY KEY,
        Code            nvarchar(100) NOT NULL,
        Title           nvarchar(500) NOT NULL,
        Medium          nvarchar(50)  NOT NULL,
        Location        nvarchar(255) NOT NULL,
        PeriodCovered   nvarchar(100) NOT NULL,
        FilingSystem    nvarchar(50)  NOT NULL,
        AccessControl   nvarchar(50)  NOT NULL,
        RetentionPeriod nvarchar(100) NOT NULL,
        CreatedAt       datetime2     NOT NULL CONSTRAINT DF_Records_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Records_Code UNIQUE (Code)
    );
END");

            // Each column is added in its own batch so the backfills below can
            // reference them (SQL Server compiles a batch before running it).
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'CreatedByUserId') IS NULL
    ALTER TABLE dbo.Records ADD CreatedByUserId int NULL;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'OwningDepartment') IS NULL
    ALTER TABLE dbo.Records ADD OwningDepartment nvarchar(50) NULL;");

            // Existing rows become Active through the default — they are live
            // records today, so that is the true status, not a guess.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RecordStatus') IS NULL
    ALTER TABLE dbo.Records ADD RecordStatus nvarchar(20) NOT NULL
        CONSTRAINT DF_Records_RecordStatus DEFAULT N'Active';");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RecordDate') IS NULL
    ALTER TABLE dbo.Records ADD RecordDate date NULL;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RetentionDueDate') IS NULL
    ALTER TABLE dbo.Records ADD RetentionDueDate date NULL;");

            // RecordDate for existing rows = the Philippine calendar date they
            // were logged (CreatedAt is stored in UTC). Dynamic SQL so this
            // still compiles on a table that somehow lacks CreatedAt.
            // CreatedByUserId and OwningDepartment are deliberately NOT
            // backfilled: nothing trustworthy records who logged old rows.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'CreatedAt') IS NOT NULL
    EXEC(N'UPDATE dbo.Records
           SET RecordDate = CAST(SWITCHOFFSET(CAST(CreatedAt AS datetimeoffset), ''+08:00'') AS date)
           WHERE RecordDate IS NULL AND CreatedAt IS NOT NULL;');");

            // Same grammar as Services/RetentionPeriodParser: "N year(s)" or
            // "N month(s)" only. TRY_CAST everywhere because SQL Server does
            // not guarantee WHERE-clause evaluation order.
            migrationBuilder.Sql(@"
WITH trimmed AS (
    SELECT RecordId, RecordDate, v = LTRIM(RTRIM(RetentionPeriod))
    FROM dbo.Records
    WHERE RetentionDueDate IS NULL AND RecordDate IS NOT NULL AND RetentionPeriod IS NOT NULL
), parts AS (
    SELECT RecordId, RecordDate,
           num  = CASE WHEN CHARINDEX(N' ', v) > 1 THEN LEFT(v, CHARINDEX(N' ', v) - 1) END,
           unit = CASE WHEN CHARINDEX(N' ', v) > 1 THEN LOWER(LTRIM(SUBSTRING(v, CHARINDEX(N' ', v) + 1, 4000))) END
    FROM trimmed
), valid AS (
    SELECT RecordId, RecordDate, unit, amount = TRY_CAST(num AS int)
    FROM parts
    WHERE LEN(num) BETWEEN 1 AND 4 AND num NOT LIKE N'%[^0-9]%'
)
UPDATE r
SET RetentionDueDate = CASE WHEN v.unit IN (N'year', N'years')
                            THEN DATEADD(year,  v.amount, v.RecordDate)
                            ELSE DATEADD(month, v.amount, v.RecordDate) END
FROM dbo.Records r
JOIN valid v ON v.RecordId = r.RecordId
WHERE (v.unit IN (N'year', N'years')   AND v.amount BETWEEN 1 AND 100)
   OR (v.unit IN (N'month', N'months') AND v.amount BETWEEN 1 AND 1200);");

            // NO ACTION (not cascade): deleting a user must never delete the
            // records they logged.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.FK_Records_Users_CreatedByUserId', N'F') IS NULL
    ALTER TABLE dbo.Records WITH CHECK ADD CONSTRAINT FK_Records_Users_CreatedByUserId
        FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users (Id) ON DELETE NO ACTION;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_OwningDepartment' AND object_id = OBJECT_ID(N'dbo.Records'))
    CREATE INDEX IX_Records_OwningDepartment ON dbo.Records (OwningDepartment);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_RetentionDueDate' AND object_id = OBJECT_ID(N'dbo.Records'))
    CREATE INDEX IX_Records_RetentionDueDate ON dbo.Records (RetentionDueDate);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_CreatedByUserId' AND object_id = OBJECT_ID(N'dbo.Records'))
    CREATE INDEX IX_Records_CreatedByUserId ON dbo.Records (CreatedByUserId);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removes only what Up added. The Records table itself is never
            // dropped here — it may predate this migration and hold real data.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.FK_Records_Users_CreatedByUserId', N'F') IS NOT NULL
    ALTER TABLE dbo.Records DROP CONSTRAINT FK_Records_Users_CreatedByUserId;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_OwningDepartment' AND object_id = OBJECT_ID(N'dbo.Records'))
    DROP INDEX IX_Records_OwningDepartment ON dbo.Records;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_RetentionDueDate' AND object_id = OBJECT_ID(N'dbo.Records'))
    DROP INDEX IX_Records_RetentionDueDate ON dbo.Records;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_CreatedByUserId' AND object_id = OBJECT_ID(N'dbo.Records'))
    DROP INDEX IX_Records_CreatedByUserId ON dbo.Records;
IF OBJECT_ID(N'dbo.DF_Records_RecordStatus', N'D') IS NOT NULL
    ALTER TABLE dbo.Records DROP CONSTRAINT DF_Records_RecordStatus;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RetentionDueDate') IS NOT NULL ALTER TABLE dbo.Records DROP COLUMN RetentionDueDate;
IF COL_LENGTH(N'dbo.Records', N'RecordDate')       IS NOT NULL ALTER TABLE dbo.Records DROP COLUMN RecordDate;
IF COL_LENGTH(N'dbo.Records', N'RecordStatus')     IS NOT NULL ALTER TABLE dbo.Records DROP COLUMN RecordStatus;
IF COL_LENGTH(N'dbo.Records', N'OwningDepartment') IS NOT NULL ALTER TABLE dbo.Records DROP COLUMN OwningDepartment;
IF COL_LENGTH(N'dbo.Records', N'CreatedByUserId')  IS NOT NULL ALTER TABLE dbo.Records DROP COLUMN CreatedByUserId;");
        }
    }
}
