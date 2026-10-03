namespace DTIOneLink.Models
{
    public class SuperAdminDashboardViewModel
    {
        public List<DashboardAssignmentSummary> WorkItems { get; set; } = new();

        public int TotalTasks => WorkItems.Count;
        public int TodoCount { get; set; }
        public int InProgressCount { get; set; }
        public int ForReviewCount { get; set; }
        public int ReturnedForCorrectionCount { get; set; }
        public int CompletedCount { get; set; }
        public int AtRiskCount { get; set; }

        public List<DashboardAssignmentSummary> OverdueItems { get; set; } = new();
        public int OverdueCount => OverdueItems.Count;

        public List<EmployeeWorkloadSummary> EmployeeWorkloads { get; set; } = new();

        public int OverallEfficiencyPercent =>
            TotalTasks == 0 ? 0 : (int)Math.Round(CompletedCount * 100.0 / TotalTasks);

        public int TodoPercent => TotalTasks == 0 ? 0 : (int)Math.Round(TodoCount * 100.0 / TotalTasks);
        public int InProgressPercent => TotalTasks == 0 ? 0 : (int)Math.Round(InProgressCount * 100.0 / TotalTasks);
        public int ForReviewPercent => TotalTasks == 0 ? 0 : (int)Math.Round(ForReviewCount * 100.0 / TotalTasks);
        public int ReturnedForCorrectionPercent => TotalTasks == 0 ? 0 : (int)Math.Round(ReturnedForCorrectionCount * 100.0 / TotalTasks);
        public int CompletedPercent => TotalTasks == 0 ? 0 : (int)Math.Round(CompletedCount * 100.0 / TotalTasks);
    }

    public class EmployeeWorkloadSummary
    {
        public string FullName { get; set; } = string.Empty;
        public int TotalAssigned { get; set; }
        public int ToDo { get; set; }
        public int InProgress { get; set; }
        public int ForReview { get; set; }
        public int ReturnedForCorrection { get; set; }
        public int Completed { get; set; }
        public int Overdue { get; set; }
        public int AtRisk { get; set; }
        public int EfficiencyPercent { get; set; }
    }

    public class DashboardAssignmentSummary
    {
        public string TaskName { get; set; } = string.Empty;
        public string AssigneeName { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public int Progress { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
