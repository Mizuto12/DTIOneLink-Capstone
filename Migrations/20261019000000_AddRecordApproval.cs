using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // Adds PD (SuperAdmin) approval tracking to Records, matching
    // 20260925000000_AddRecordSystemFields's guarded-SQL approach since
    // Records is not part of the EF model.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261019000000_AddRecordApproval")]
    /// <inheritdoc />
    public partial class AddRecordApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'ApprovedByUserId') IS NULL
    ALTER TABLE dbo.Records ADD ApprovedByUserId int NULL;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'ApprovedAt') IS NULL
    ALTER TABLE dbo.Records ADD ApprovedAt datetime2 NULL;");

            // NO ACTION (not cascade): deleting a user must never delete the
            // records they approved.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.FK_Records_Users_ApprovedByUserId', N'F') IS NULL
    ALTER TABLE dbo.Records WITH CHECK ADD CONSTRAINT FK_Records_Users_ApprovedByUserId
        FOREIGN KEY (ApprovedByUserId) REFERENCES dbo.Users (Id) ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.FK_Records_Users_ApprovedByUserId', N'F') IS NOT NULL
    ALTER TABLE dbo.Records DROP CONSTRAINT FK_Records_Users_ApprovedByUserId;");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Records', N'ApprovedAt')      IS NOT NULL ALTER TABLE dbo.Records DROP COLUMN ApprovedAt;
IF COL_LENGTH(N'dbo.Records', N'ApprovedByUserId') IS NOT NULL ALTER TABLE dbo.Records DROP COLUMN ApprovedByUserId;");
        }
    }
}
