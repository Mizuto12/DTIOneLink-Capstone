using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // Renames two of RecordStatus's three values to match the office's own
    // words for them (Say, OPD, chat on 2026-10-20): Active -> Retention (not
    // yet due), ForDisposal -> Disposal (near or needs disposal). Disposed
    // keeps its name — RA 9470/NAP's own vocabulary reserves "archival" for a
    // record of permanent value preserved at the National Archives instead of
    // disposed of, the opposite of this status, not a synonym for it. Same
    // guarded-SQL approach as 20260925000000_AddRecordSystemFields, since
    // Records is not part of the EF model.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261020000000_RenameRecordStatusValues")]
    /// <inheritdoc />
    public partial class RenameRecordStatusValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows: old literal values become the new ones. Anything
            // already outside the old vocabulary (there shouldn't be any) is
            // left untouched rather than guessed at.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RecordStatus') IS NOT NULL
    UPDATE dbo.Records
    SET RecordStatus = CASE RecordStatus
        WHEN N'Active'      THEN N'Retention'
        WHEN N'ForDisposal' THEN N'Disposal'
        ELSE RecordStatus
    END
    WHERE RecordStatus IN (N'Active', N'ForDisposal');");

            // The column default is a separate, named constraint in SQL
            // Server — changing it means dropping and recreating it, not
            // altering it in place.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.DF_Records_RecordStatus', N'D') IS NOT NULL
    ALTER TABLE dbo.Records DROP CONSTRAINT DF_Records_RecordStatus;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RecordStatus') IS NOT NULL
   AND OBJECT_ID(N'dbo.DF_Records_RecordStatus', N'D') IS NULL
    ALTER TABLE dbo.Records ADD CONSTRAINT DF_Records_RecordStatus
        DEFAULT N'Retention' FOR RecordStatus;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.DF_Records_RecordStatus', N'D') IS NOT NULL
    ALTER TABLE dbo.Records DROP CONSTRAINT DF_Records_RecordStatus;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RecordStatus') IS NOT NULL
   AND OBJECT_ID(N'dbo.DF_Records_RecordStatus', N'D') IS NULL
    ALTER TABLE dbo.Records ADD CONSTRAINT DF_Records_RecordStatus
        DEFAULT N'Active' FOR RecordStatus;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'RecordStatus') IS NOT NULL
    UPDATE dbo.Records
    SET RecordStatus = CASE RecordStatus
        WHEN N'Retention' THEN N'Active'
        WHEN N'Disposal'  THEN N'ForDisposal'
        ELSE RecordStatus
    END
    WHERE RecordStatus IN (N'Retention', N'Disposal');");
        }
    }
}
