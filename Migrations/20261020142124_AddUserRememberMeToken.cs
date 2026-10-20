using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRememberMeToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RememberTokenExpiresUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RememberTokenHash",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RememberTokenExpiresUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RememberTokenHash",
                table: "Users");
        }
    }
}
