namespace DTIOneLink.Models
{
    // View model for the Task Management Index page (workflow monitoring).
    // Carries the already-authorized, filtered, sorted, paged task list plus
    // the active search/filter/sort values, so the view can re-render the
    // controls in their submitted state and every link (status tiles,
    // pagination, Clear) can round-trip the same query string.
    public class TaskIndexViewModel
    {
        public const int PageSize = 10;

        // The current page of tasks.
        public List<TaskItem> Tasks { get; set; } = new();

        // Raw search text, as submitted (trimmed). Null/empty = no search.
        public string? Search { get; set; }

        // One of: all, pending, in-progress, for-review,
        // returned-for-correction, completed, overdue.
        public string Status { get; set; } = "all";

        // One of: all, high, medium, low.
        public string Priority { get; set; } = "all";

        // SuperAdmin-only. Null = no department filter applied.
        public string? Department { get; set; }

        // Null = every employee in scope.
        public int? EmployeeId { get; set; }

        // One of: all, today, next7, this-month.
        public string Due { get; set; } = "all";

        // One of: newest, due-asc, priority-desc.
        public string Sort { get; set; } = "newest";

        public int Page { get; set; } = 1;
        public int TotalCount { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

        // True for SuperAdmin (ManageOfficeWideTasks) — controls whether the
        // department filter is rendered at all. Never trust a posted
        // Department value from a caller for whom this is false.
        public bool IsOfficeWide { get; set; }

        // Dropdown options, already limited to what this user may see.
        public List<string> Departments { get; set; } = new();
        public List<EmployeeOption> Employees { get; set; } = new();

        // Workflow indicators: task counts per display status, computed with
        // every filter EXCEPT Status applied, so clicking a tile narrows the
        // current view to that status.
        public Dictionary<string, int> StatusCounts { get; set; } = new();

        public bool HasActiveFilters =>
            !string.IsNullOrEmpty(Search) || Status != "all" || Priority != "all" ||
            !string.IsNullOrEmpty(Department) || EmployeeId.HasValue || Due != "all";

        // Query-string values for links, with optional overrides. Defaults
        // are omitted to keep URLs short.
        public Dictionary<string, string> RouteValues(string? status = null, int? page = null)
        {
            var values = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(Search)) values["q"] = Search;

            var effectiveStatus = status ?? Status;
            if (effectiveStatus != "all") values["status"] = effectiveStatus;

            if (Priority != "all") values["priority"] = Priority;
            if (!string.IsNullOrEmpty(Department)) values["department"] = Department;
            if (EmployeeId.HasValue) values["employeeId"] = EmployeeId.Value.ToString();
            if (Due != "all") values["due"] = Due;
            if (Sort != "newest") values["sort"] = Sort;

            var effectivePage = page ?? 1;
            if (effectivePage > 1) values["page"] = effectivePage.ToString();
            return values;
        }

        public record EmployeeOption(int Id, string FullName, string Department, bool IsAdmin = false);
    }
}
