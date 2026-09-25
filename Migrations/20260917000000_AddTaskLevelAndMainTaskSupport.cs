using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260917000000_AddTaskLevelAndMainTaskSupport")]
    /// <inheritdoc />
    public partial class AddTaskLevelAndMainTaskSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing row becomes "subtask" — correct, since every
            // task before this feature was an ordinary, assignee-bearing
            // task. Nothing existing is reinterpreted as a Main Task.
            migrationBuilder.AddColumn<string>(
                name: "TaskLevel",
                table: "TaskItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "subtask");

            migrationBuilder.AddColumn<string>(
                name: "TargetDepartment",
                table: "TaskItems",
                type: "nvarchar(max)",
                nullable: true);

            // AssigneeId becomes optional. Drop/recreate the FK rather than
            // just altering the column, so its delete behavior can move
            // from Cascade to Restrict in the same migration.
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Users_AssigneeId",
                table: "TaskItems");

            migrationBuilder.AlterColumn<int>(
                name: "AssigneeId",
                table: "TaskItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Users_AssigneeId",
                table: "TaskItems",
                column: "AssigneeId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskItems_Users_AssigneeId",
                table: "TaskItems");

            migrationBuilder.AlterColumn<int>(
                name: "AssigneeId",
                table: "TaskItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskItems_Users_AssigneeId",
                table: "TaskItems",
                column: "AssigneeId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropColumn(name: "TargetDepartment", table: "TaskItems");
            migrationBuilder.DropColumn(name: "TaskLevel", table: "TaskItems");
        }
    }
}
