using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    /// <inheritdoc />
    public partial class RebalanceSubtaskWeightsToPercentages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Subtask weighting changed meaning the same day it shipped
            // (AddTaskItemWeight, just before this migration): Weight used
            // to be a relative share normalized against siblings, so every
            // existing subtask sitting at the migrated default of 1 behaved
            // as an equal split. Under the new percentage model, Weight is a
            // literal share of the parent's progress — leaving every
            // existing subtask at a literal "1" would instantly cap any
            // directive with 2+ subtasks at a 1-2% progress ceiling until
            // every one of them is individually marked Completed.
            //
            // This redistributes 100% evenly across each Main Task's
            // existing subtasks (by ParentTaskId), reproducing the exact
            // progress number those directives already showed under the old
            // scheme — e.g. 2 subtasks -> 50/50, 3 subtasks -> 34/33/33 (the
            // remainder goes to the lowest-Id subtask so the group still
            // sums to exactly 100). A subtask with no siblings (the only one
            // under its parent) gets the full 100. Nothing changes for a
            // Main Task that has no subtasks, or for a standalone task —
            // this only ever touches rows with TaskLevel = 'subtask' and a
            // non-null ParentTaskId.
            migrationBuilder.Sql(@"
;WITH SubtaskGroups AS (
    SELECT
        Id,
        ParentTaskId,
        COUNT(*) OVER (PARTITION BY ParentTaskId) AS SiblingCount,
        ROW_NUMBER() OVER (PARTITION BY ParentTaskId ORDER BY Id) AS RowNum
    FROM TaskItems
    WHERE ParentTaskId IS NOT NULL AND TaskLevel = 'subtask'
)
UPDATE t
SET t.Weight = CASE
    WHEN g.RowNum = 1 THEN 100 - (100 / g.SiblingCount) * (g.SiblingCount - 1)
    ELSE 100 / g.SiblingCount
END
FROM TaskItems t
JOIN SubtaskGroups g ON g.Id = t.Id;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reversal, not a perfect undo: restores every
            // subtask to Weight = 1, the value every row actually had
            // immediately before this migration ran. If a weight was
            // deliberately changed after this migration applied (someone
            // using the new percentage model for real), that specific edit
            // is not recoverable by rolling back — same limitation any
            // migration has once real data has moved past the state it
            // captured.
            migrationBuilder.Sql(@"
UPDATE TaskItems
SET Weight = 1
WHERE ParentTaskId IS NOT NULL AND TaskLevel = 'subtask';
");
        }
    }
}
