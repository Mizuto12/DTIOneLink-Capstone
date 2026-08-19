using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    public class TaskCreateViewModel
    {
        public TaskItem Task { get; set; } = new();

        public List<UserItem> Employees { get; set; } = [];
    }
}
