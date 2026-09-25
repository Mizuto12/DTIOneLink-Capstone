using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260915000000_AddTaskCreatedByUserId")]
    /// <inheritdoc />
    public partial class AddTaskCreatedByUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable, so this is additive and safe on a populated table —
            // existing rows get NULL and keep working. No backfill: there's no
            // trustworthy source for who created a pre-existing task, and
            // defaulting to AssigneeId would record a falsehood.
            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_CreatedByUserId",
                table: "TaskItems",
                column: "CreatedByUserId");

            // NoAction, not Cascade — TaskItems already has a cascading FK to
            // Users via AssigneeId, and SQL Server rejects a second cascade
            // path to the same table.
            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Users_CreatedByUserId",
                table: "TaskItems",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Users_CreatedByUserId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_CreatedByUserId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "TaskItems");
        }
    }
}
