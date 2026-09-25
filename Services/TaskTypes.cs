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

        public static bool IsValid(string? taskType) =>
            taskType == DirectAdmin || taskType == DepartmentDirective;
    }
}