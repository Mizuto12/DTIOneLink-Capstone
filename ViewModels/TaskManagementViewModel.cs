using System.Collections.Generic;
using DTIOneLink.Models;

namespace DTIOneLink.ViewModels
{
    public class TaskManagementViewModel
    {
        public IEnumerable<TaskItem> Tasks { get; set; } = new List<TaskItem>();
        public IEnumerable<UserItem> Users { get; set; } = new List<UserItem>();
        public TaskItem NewTask { get; set; } = new();

        // When true, the Create Task modal is rendered open (e.g. after a validation error).
        public bool OpenModal { get; set; }
    }
}
