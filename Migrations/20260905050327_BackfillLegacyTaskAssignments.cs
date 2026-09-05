using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLegacyTaskAssignments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfills one TaskAssignment per legacy TaskItem, skipping any
            // TaskId/UserId pair that already has a TaskAssignment row (whether
            // from AddTaskAssignments or normal app usage since). Purely additive:
            // no existing TaskAssignments, TaskItems, TaskSubmissions, activities,
            // or notifications rows are touched.
            migrationBuilder.Sql(@"
INSERT INTO TaskAssignments
    (TaskId, UserId, AssignedAt, AssignedByUserId, Progress, Status, IsPrimaryAssignee)
SELECT
    t.Id,
    t.AssigneeId,
    t.CreatedAt,
    NULL,
    t.Progress,
    CASE LOWER(REPLACE(LTRIM(RTRIM(t.Status)), ' ', '_'))
        WHEN 'pending'      THEN 'pending'
        WHEN 'in_progress'  THEN 'in_progress'
        WHEN 'for_review'   THEN 'for_review'
        WHEN 'completed'    THEN 'completed'
        WHEN 'rejected'     THEN 'rejected'
        WHEN 'overdue'      THEN 'overdue'
        ELSE 'pending'
    END,
    1
FROM TaskItems t
WHERE t.AssigneeId IS NOT NULL
  AND NOT EXISTS (
        SELECT 1
        FROM TaskAssignments ta
        WHERE ta.TaskId = t.Id
          AND ta.UserId = t.AssigneeId
  );
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort only: this deletes rows matching the exact shape this
            // migration inserts (AssignedByUserId NULL, IsPrimaryAssignee = 1,
            // AssignedAt == the TaskItem's CreatedAt). It cannot distinguish a
            // backfilled row from a real one that happens to share that shape.
            // If you need a bulletproof rollback, add a nullable marker column
            // (e.g. "Source") before running Up and stamp backfilled rows with it.
            migrationBuilder.Sql(@"
DELETE ta
FROM TaskAssignments ta
INNER JOIN TaskItems t
    ON t.Id = ta.TaskId
   AND t.AssigneeId = ta.UserId
WHERE ta.AssignedByUserId IS NULL
  AND ta.IsPrimaryAssignee = 1
  AND ta.AssignedAt = t.CreatedAt;
");
        }
    }
}