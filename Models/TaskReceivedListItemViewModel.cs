using System;

namespace DTIOneLink.Models
{
    // Row shape for TasksController.ReceivedTasks — a read-only summary of
    // Main Tasks (OPD directives) targeted at the viewer's own department
    // (or, for SuperAdmin, every department). EmployeeAssignmentCount is the
    // distinct count of employees assigned across all of this Main Task's
    // subtasks — Main Tasks carry no Assignments of their own.
    public class TaskReceivedListItemViewModel
    {
        public int Id { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> AssigneeNames { get; set; } = new();
    }
}