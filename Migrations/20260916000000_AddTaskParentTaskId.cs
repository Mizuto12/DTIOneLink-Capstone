using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260916000000_AddTaskParentTaskId")]
    /// <inheritdoc />
    public partial class AddTaskParentTaskId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable, additive column — every existing row gets NULL,
            // which is exactly "standalone main task". No backfill needed;
            // nothing about current tasks changes meaning.
            migrationBuilder.AddColumn<int>(
                name: "ParentTaskId",
                table: "TaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ParentTaskId",
                table: "TaskItems",
                column: "ParentTaskId");

            // NoAction: a self-referencing cascade is rejected by SQL Server
            // anyway, and Restrict is also what we want behaviorally — a
            // main task with subtasks shouldn't be deletable out from
            // under them without that being an explicit, visible decision.
            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_TaskItems_ParentTaskId",
                table: "TaskItems",
                column: "ParentTaskId",
                principalTable: "TaskItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_TaskItems_ParentTaskId",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ParentTaskId",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "ParentTaskId",
                table: "TaskItems");
        }
    }
}
