using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskActivityRelatedSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RelatedSubmissionId",
                table: "TaskActivities",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_RelatedSubmissionId",
                table: "TaskActivities",
                column: "RelatedSubmissionId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskActivities_TaskSubmissions_RelatedSubmissionId",
                table: "TaskActivities",
                column: "RelatedSubmissionId",
                principalTable: "TaskSubmissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskActivities_TaskSubmissions_RelatedSubmissionId",
                table: "TaskActivities");

            migrationBuilder.DropIndex(
                name: "IX_TaskActivities_RelatedSubmissionId",
                table: "TaskActivities");

            migrationBuilder.DropColumn(
                name: "RelatedSubmissionId",
                table: "TaskActivities");
        }
    }
}
