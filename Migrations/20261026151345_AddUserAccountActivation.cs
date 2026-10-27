using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            // Grandfather every account that already existed before this
            // column shipped — only accounts created after this point go
            // through the new activation-link flow (UserManagementController
            // .Create / AccountController.Activate).
            migrationBuilder.Sql("UPDATE dbo.Users SET ActivatedAtUtc = CreatedAt WHERE ActivatedAtUtc IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                table: "Users");
        }
    }
}
