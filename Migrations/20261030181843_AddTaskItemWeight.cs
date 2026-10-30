using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskItemWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1 (TaskWorkflow.DefaultSubtaskWeight), not the int CLR default
            // of 0 — every subtask that already exists is backfilled to an
            // equal-weight 1, which is mathematically identical to the plain
            // average RecalculateMainTaskFromSubtasks used before this
            // column existed (Sum(Progress*1)/Sum(1) == Average(Progress)).
            // No existing Main Task's calculated progress changes until an
            // authorized user (the OPD) actually edits a subtask's weight.
            // A weight of 0 would both violate TaskItem.Weight's own
            // [Range(1, 20)] and, left unguarded, divide by zero in
            // RecalculateMainTaskFromSubtasks if every subtask under a task
            // somehow had it.
            migrationBuilder.AddColumn<int>(
                name: "Weight",
                table: "TaskItems",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Weight",
                table: "TaskItems");
        }
    }
}
