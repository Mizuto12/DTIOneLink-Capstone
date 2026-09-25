using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260918000000_AddTaskResponsibleAdminUserId")]
    /// <inheritdoc />
    public partial class AddTaskResponsibleAdminUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResponsibleAdminUserId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ResponsibleAdminUserId",
                table: "TaskItems",
                column: "ResponsibleAdminUserId");

            // NoAction — TaskItems already has cascading-avoidant Restrict
            // FKs to Users via AssigneeId and CreatedByUserId; a third path
            // to Users hits the same multiple-cascade-paths constraint.
            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Users_ResponsibleAdminUserId",
                table: "TaskItems",
                column: "ResponsibleAdminUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Users_ResponsibleAdminUserId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ResponsibleAdminUserId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ResponsibleAdminUserId",
                table: "TaskItems");
        }
    }
}
