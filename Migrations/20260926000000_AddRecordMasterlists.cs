using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // "Save Masterlist" on the Records page turns the records a user has
    // added since their last save into a DTI Masterlist of Records Excel
    // file. RecordMasterlists keeps each saved file so it can be downloaded
    // again; Records.MasterlistId marks which masterlist a record went into
    // (NULL = still on the working table). Records stay in the database
    // either way, so retention reminders and Reports keep working.
    //
    // Records is not in the EF model (RecordsController uses raw SQL), so
    // this is guarded SQL like AddRecordSystemFields, and the model snapshot
    // is intentionally unchanged. Everything is additive.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260926000000_AddRecordMasterlists")]
    /// <inheritdoc />
    public partial class AddRecordMasterlists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.RecordMasterlists', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecordMasterlists (
        MasterlistId       int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecordMasterlists PRIMARY KEY,
        CreatedByUserId    int            NOT NULL,
        OwningDepartment   nvarchar(50)   NULL,
        CreatedAt          datetime2      NOT NULL CONSTRAINT DF_RecordMasterlists_CreatedAt DEFAULT SYSUTCDATETIME(),
        RecordCount        int            NOT NULL,
        FileName           nvarchar(200)  NOT NULL,
        FileContent        varbinary(max) NOT NULL,
        PreparedByName     nvarchar(100)  NOT NULL CONSTRAINT DF_RecordMasterlists_PreparedByName DEFAULT N'',
        PreparedByPosition nvarchar(100)  NOT NULL CONSTRAINT DF_RecordMasterlists_PreparedByPosition DEFAULT N'',
        ReviewedByName     nvarchar(100)  NOT NULL CONSTRAINT DF_RecordMasterlists_ReviewedByName DEFAULT N'',
        ReviewedByPosition nvarchar(100)  NOT NULL CONSTRAINT DF_RecordMasterlists_ReviewedByPosition DEFAULT N'',
        NotedByName        nvarchar(100)  NOT NULL CONSTRAINT DF_RecordMasterlists_NotedByName DEFAULT N'',
        NotedByPosition    nvarchar(100)  NOT NULL CONSTRAINT DF_RecordMasterlists_NotedByPosition DEFAULT N'',
        -- NO ACTION: deleting a user must never delete saved masterlists.
        CONSTRAINT FK_RecordMasterlists_Users_CreatedByUserId
            FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_RecordMasterlists_CreatedByUserId ON dbo.RecordMasterlists (CreatedByUserId);
END");

            // Own batch so the FK/index below can reference the new column.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'MasterlistId') IS NULL
    ALTER TABLE dbo.Records ADD MasterlistId int NULL;");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.FK_Records_RecordMasterlists_MasterlistId', N'F') IS NULL
    ALTER TABLE dbo.Records WITH CHECK ADD CONSTRAINT FK_Records_RecordMasterlists_MasterlistId
        FOREIGN KEY (MasterlistId) REFERENCES dbo.RecordMasterlists (MasterlistId) ON DELETE NO ACTION;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_MasterlistId' AND object_id = OBJECT_ID(N'dbo.Records'))
    CREATE INDEX IX_Records_MasterlistId ON dbo.Records (MasterlistId);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.FK_Records_RecordMasterlists_MasterlistId', N'F') IS NOT NULL
    ALTER TABLE dbo.Records DROP CONSTRAINT FK_Records_RecordMasterlists_MasterlistId;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Records_MasterlistId' AND object_id = OBJECT_ID(N'dbo.Records'))
    DROP INDEX IX_Records_MasterlistId ON dbo.Records;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'MasterlistId') IS NOT NULL
    ALTER TABLE dbo.Records DROP COLUMN MasterlistId;
IF OBJECT_ID(N'dbo.RecordMasterlists', N'U') IS NOT NULL
    DROP TABLE dbo.RecordMasterlists;");
        }
    }
}
