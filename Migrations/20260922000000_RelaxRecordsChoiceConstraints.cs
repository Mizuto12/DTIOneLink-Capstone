using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // The Records table's CHECK constraints only allowed the original dropdown
    // values (Physical/Digital, Alphabetical/Chronological/Numerical,
    // Public/Restricted/Confidential). The Records form now uses DTI's actual
    // masterlist choices (e.g. "Hard Copy/Electronic", "Per Transaction",
    // "Public Record") plus a typed "Other / Specify" value, which no fixed
    // list can cover — so every save with a new choice failed with SQL error
    // 547. Each constraint keeps its name but now only rejects blank values;
    // RecordsController.Save validates the actual choices.
    //
    // Dated before 20260925000000_AddRecordSystemFields on purpose, so this can
    // be applied on its own ("dotnet ef database update
    // 20260922000000_RelaxRecordsChoiceConstraints") while that one stays
    // pending. Records is not in the EF model, so the snapshot is unchanged.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260922000000_RelaxRecordsChoiceConstraints")]
    /// <inheritdoc />
    public partial class RelaxRecordsChoiceConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Skipped entirely on a database without the Records table; the
            // later AddRecordSystemFields migration creates it without these
            // value lists.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Records', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.CK_Records_Medium', N'C') IS NOT NULL
        ALTER TABLE dbo.Records DROP CONSTRAINT CK_Records_Medium;
    IF OBJECT_ID(N'dbo.CK_Records_Filing', N'C') IS NOT NULL
        ALTER TABLE dbo.Records DROP CONSTRAINT CK_Records_Filing;
    IF OBJECT_ID(N'dbo.CK_Records_Access', N'C') IS NOT NULL
        ALTER TABLE dbo.Records DROP CONSTRAINT CK_Records_Access;

    ALTER TABLE dbo.Records WITH CHECK ADD CONSTRAINT CK_Records_Medium
        CHECK (LEN(LTRIM(RTRIM(Medium))) > 0);
    ALTER TABLE dbo.Records WITH CHECK ADD CONSTRAINT CK_Records_Filing
        CHECK (LEN(LTRIM(RTRIM(FilingSystem))) > 0);
    ALTER TABLE dbo.Records WITH CHECK ADD CONSTRAINT CK_Records_Access
        CHECK (LEN(LTRIM(RTRIM(AccessControl))) > 0);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the original value lists. WITH NOCHECK so rolling back
            // doesn't fail on rows already saved with the new choices; those
            // rows are kept, only new inserts are held to the old lists again.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Records', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.CK_Records_Medium', N'C') IS NOT NULL
        ALTER TABLE dbo.Records DROP CONSTRAINT CK_Records_Medium;
    IF OBJECT_ID(N'dbo.CK_Records_Filing', N'C') IS NOT NULL
        ALTER TABLE dbo.Records DROP CONSTRAINT CK_Records_Filing;
    IF OBJECT_ID(N'dbo.CK_Records_Access', N'C') IS NOT NULL
        ALTER TABLE dbo.Records DROP CONSTRAINT CK_Records_Access;

    ALTER TABLE dbo.Records WITH NOCHECK ADD CONSTRAINT CK_Records_Medium
        CHECK ([Medium]='Digital' OR [Medium]='Physical');
    ALTER TABLE dbo.Records WITH NOCHECK ADD CONSTRAINT CK_Records_Filing
        CHECK ([FilingSystem]='Alphabetical' OR [FilingSystem]='Chronological' OR [FilingSystem]='Numerical');
    ALTER TABLE dbo.Records WITH NOCHECK ADD CONSTRAINT CK_Records_Access
        CHECK ([AccessControl]='Confidential' OR [AccessControl]='Restricted' OR [AccessControl]='Public');
END");
        }
    }
}
