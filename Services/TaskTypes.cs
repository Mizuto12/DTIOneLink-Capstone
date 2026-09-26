namespace DTIOneLink.Services
{
    // Allowed values for TaskItem.TaskType. Only meaningful when
    // TaskItem.TaskLevel == TaskLevels.Main.
    public static class TaskTypes
    {
        // SuperAdmin assigns the Main Task straight to the Responsible
        // Admin — a TaskAssignment row is created for that Admin at
        // creation time, and no one else can be assigned to it directly;
        // the Admin works it themselves via the normal Employee workflow.
        public const string DirectAdmin = "direct-admin";

        // SuperAdmin sends the Main Task to OwningDepartment with a
        // Responsible Admin, but creates no TaskAssignment rows. The
        // receiving Admin assigns employees or creates subtasks afterward.
        public const string DepartmentDirective = "department-directive";

        // SuperAdmin gives the Main Task to everyone in the office (every
        // active Admin and Employee, e.g. the Monthly CSF Report). Each
        // person gets their own TaskAssignment and submits their own proof,
        // which the OPD reviews — same as a Direct Admin Task, just with
        // many assignees. No Responsible Admin, no subtasks.
        public const string WholeOffice = "whole-office";

        public static bool IsValid(string? taskType) =>
            taskType == DirectAdmin || taskType == DepartmentDirective || taskType == WholeOffice;

        // The OPD assigns people to these directly and reviews their
        // submissions itself (no Division Chief in between).
        public static bool IsAssignedByOpd(string? taskType) =>
            taskType == DirectAdmin || taskType == WholeOffice;
    }
}