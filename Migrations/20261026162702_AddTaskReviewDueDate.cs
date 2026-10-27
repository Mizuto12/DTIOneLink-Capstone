using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReviewDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewDueDate",
                table: "TaskItems",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewDueDate",
                table: "TaskItems");
        }
    }
}
