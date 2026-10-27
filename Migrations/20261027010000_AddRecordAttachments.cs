using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // Decision 7: an attachment is tied to the record itself, not to a
    // specific masterlist entry — one reusable row per upload, visible
    // anywhere the record shows up, never re-attached per masterlist.
    // Raw SQL (decision 8), same as every other Records table, since
    // Records is not part of the EF model.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261027010000_AddRecordAttachments")]
    /// <inheritdoc />
    public partial class AddRecordAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.RecordAttachments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecordAttachments (
        Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecordAttachments PRIMARY KEY,
        RecordId         int            NOT NULL,
        FileName         nvarchar(260)  NOT NULL,
        StoredFileName   nvarchar(100)  NOT NULL,
        ContentType      nvarchar(100)  NULL,
        FileSizeBytes    bigint         NOT NULL,
        UploadedByUserId int            NULL,
        UploadedAt       datetime2      NOT NULL CONSTRAINT DF_RecordAttachments_UploadedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_RecordAttachments_Records_RecordId
            FOREIGN KEY (RecordId) REFERENCES dbo.Records (RecordId) ON DELETE CASCADE,
        CONSTRAINT FK_RecordAttachments_Users_UploadedByUserId
            FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_RecordAttachments_RecordId ON dbo.RecordAttachments (RecordId);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.RecordAttachments', N'U') IS NOT NULL
    DROP TABLE dbo.RecordAttachments;");
        }
    }
}
