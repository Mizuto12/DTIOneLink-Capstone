using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskChecklistAndRequiresReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // true, not the bool CLR default — every task that already
            // exists keeps requiring review exactly as it does today. Only
            // a task created after this ships can have it set to false, and
            // only by its creator at creation time.
            migrationBuilder.AddColumn<bool>(
                name: "RequiresReview",
                table: "TaskItems",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "TaskChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskAssignmentId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    IsDone = table.Column<bool>(type: "bit", nullable: false),
                    DoneAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskChecklistItems_TaskAssignments_TaskAssignmentId",
                        column: x => x.TaskAssignmentId,
                        principalTable: "TaskAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskChecklistItems_TaskAssignmentId",
                table: "TaskChecklistItems",
                column: "TaskAssignmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskChecklistItems");

            migrationBuilder.DropColumn(
                name: "RequiresReview",
                table: "TaskItems");
        }
    }
}
