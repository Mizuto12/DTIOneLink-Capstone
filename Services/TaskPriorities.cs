namespace DTIOneLink.Services
{
    // Allowed values for TaskItem.Priority / TaskCreateViewModel.Priority /
    // TaskEditViewModel.Priority. Previously only "[Required]" on the
    // string — any non-empty value (e.g. "ultra-mega-urgent") was accepted
    // and persisted, unlike every sibling field on the same forms
    // (TaskType, OwningDepartment, Recurrence), which all have their own
    // IsValid() allow-list check.
    public static class TaskPriorities
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";

        public static bool IsValid(string? priority) =>
            priority == Low || priority == Medium || priority == High;
    }
}
