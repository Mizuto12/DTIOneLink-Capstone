namespace DTIOneLink.Models
{
    public class SuperAdminDashboardViewModel
    {
        public List<TaskItem> Tasks { get; set; } = new();

        public int TotalTasks => Tasks.Count;
        public int TodoCount { get; set; }
        public int InProgressCount { get; set; }
        public int CompletedCount { get; set; }

        public List<TaskItem> OverdueTasks { get; set; } = new();
        public int OverdueCount => OverdueTasks.Count;

        public List<EmployeeWorkloadSummary> EmployeeWorkloads { get; set; } = new();

        public int OverallEfficiencyPercent =>
            TotalTasks == 0 ? 0 : (int)Math.Round(CompletedCount * 100.0 / TotalTasks);

        public int TodoPercent => TotalTasks == 0 ? 0 : (int)Math.Round(TodoCount * 100.0 / TotalTasks);
        public int InProgressPercent => TotalTasks == 0 ? 0 : (int)Math.Round(InProgressCount * 100.0 / TotalTasks);
        public int CompletedPercent => TotalTasks == 0 ? 0 : (int)Math.Round(CompletedCount * 100.0 / TotalTasks);
    }

    public class EmployeeWorkloadSummary
    {
        public string FullName { get; set; } = string.Empty;
        public int TotalAssigned { get; set; }
        public int ToDo { get; set; }
        public int InProgress { get; set; }
        public int Completed { get; set; }
        public int Overdue { get; set; }
        public int EfficiencyPercent { get; set; }
    }
}