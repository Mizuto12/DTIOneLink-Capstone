using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    public enum NotificationType
    {
    Task,
    User,
    System,
    Record,
    DueSoon,
    Overdue
    }

    public class Notification
    {
        public int Id { get; set; }

        [Required]
        public int RecipientUserId { get; set; }
        public User? Recipient { get; set; }

        [Required]
        public NotificationType Type { get; set; }

        [Required]
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        // Optional links back to the entity that triggered this notification.
        // Nullable — a notification doesn't have to be about a specific task/record.
        public int? RelatedTaskId { get; set; }
        public TaskItem? RelatedTask { get; set; }

        public int? RelatedRecordId { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optional override — where clicking the notification should navigate.
        // If null, the controller/view can fall back to building a URL from
        // RelatedTaskId/RelatedRecordId.
        [MaxLength(300)]
        public string? Link { get; set; }
    }
}