using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTIOneLink.Models
{
    public class TaskComment
    {
        public int Id { get; set; }

        [ForeignKey(nameof(Task))]
        public int TaskId { get; set; }
        public TaskItem? Task { get; set; }

        [ForeignKey(nameof(Author))]
        public int AuthorUserId { get; set; }
        public User? Author { get; set; }

        [Required]
        [StringLength(1000)]
        public string Text { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}