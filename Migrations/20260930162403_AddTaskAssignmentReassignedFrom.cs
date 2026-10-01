using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAssignmentReassignedFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReassignedFromUserId",
                table: "TaskAssignments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_ReassignedFromUserId",
                table: "TaskAssignments",
                column: "ReassignedFromUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskAssignments_Users_ReassignedFromUserId",
                table: "TaskAssignments",
                column: "ReassignedFromUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskAssignments_Users_ReassignedFromUserId",
                table: "TaskAssignments");

            migrationBuilder.DropIndex(
                name: "IX_TaskAssignments_ReassignedFromUserId",
                table: "TaskAssignments");

            migrationBuilder.DropColumn(
                name: "ReassignedFromUserId",
                table: "TaskAssignments");
        }
    }
}
