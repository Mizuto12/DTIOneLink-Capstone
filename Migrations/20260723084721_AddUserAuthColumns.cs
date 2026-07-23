using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAuthColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "UserItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "UserItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "UserItems");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "UserItems");
        }
    }
}
