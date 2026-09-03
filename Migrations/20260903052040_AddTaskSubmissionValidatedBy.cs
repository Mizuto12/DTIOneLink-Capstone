using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSubmissionValidatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValidatedByUserId",
                table: "TaskSubmissions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSubmissions_ValidatedByUserId",
                table: "TaskSubmissions",
                column: "ValidatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskSubmissions_Users_ValidatedByUserId",
                table: "TaskSubmissions",
                column: "ValidatedByUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskSubmissions_Users_ValidatedByUserId",
                table: "TaskSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_TaskSubmissions_ValidatedByUserId",
                table: "TaskSubmissions");

            migrationBuilder.DropColumn(
                name: "ValidatedByUserId",
                table: "TaskSubmissions");
        }
    }
}
