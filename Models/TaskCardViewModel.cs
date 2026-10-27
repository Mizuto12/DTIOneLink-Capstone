namespace DTIOneLink.Models
{
    // Pure display shape for one task card on a Kanban-style status board —
    // shared by AdminDashboard's board (Views/Shared/_DashboardContent.cshtml)
    // and SuperAdmin's office-wide board (Views/SuperAdmin.cshtml), the two
    // confirmed duplicate implementations of "a task card in a status
    // column" (Employee's own dashboard board is a deliberately different,
    // reduced design — not a duplicate — and is left untouched).
    //
    // Each dashboard maps its own data (TaskItem rows for Admin,
    // DashboardController's WorkItemViewModel rows for SuperAdmin) into this
    // before rendering _TaskCard, so the partial never needs to know about
    // either caller's own model types or business rules.
    public class TaskCardViewModel
    {
        public required string Url { get; init; }

        // true: a real <a href> (SuperAdmin's board — keyboard/middle-click/
        // open-in-new-tab all work natively). false: a JS-driven div
        // (Admin's board — dashboard.js already treats every row/card in
        // both its Gantt and Kanban views uniformly via [data-href]).
        public bool IsRealLink { get; init; } = true;

        public required string TaskName { get; init; }

        // null = not shown. Admin's board shows this; SuperAdmin's never
        // has (office-wide board, less room per card) — preserved as-is.
        public string? Description { get; init; }

        // null = not shown. Admin's board is already scoped to one
        // department so it never sets this; SuperAdmin's office-wide board
        // does.
        public string? DivisionTag { get; init; }

        // Full department name, emitted as data-division on the card's own
        // root element — superadmin-dashboard.js's division filter reads
        // this directly off each card. null = omit the attribute (Admin's
        // board doesn't need client-side division filtering).
        public string? DivisionValue { get; init; }

        // null = not shown, matching each board's own existing behavior
        // (SuperAdmin's board has never shown a priority badge per card;
        // Admin's has).
        public string? Priority { get; init; }

        public bool IsOverdue { get; init; }
        public bool IsAtRisk { get; init; }
        public int Progress { get; init; }
        public string AssigneeInitials { get; init; } = "?";
        public string AssigneeName { get; init; } = "Unassigned";
        public required string DueText { get; init; }
    }
}
