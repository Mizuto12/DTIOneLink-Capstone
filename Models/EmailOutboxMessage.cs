using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // A notification email waiting to be sent. It is saved in the same
    // database save as the change that caused it, so a task that gets rolled
    // back never produces an email; EmailDispatchService sends it afterwards
    // and retries if Brevo is briefly unreachable.
    // Verification codes never go through here — that would store the code
    // in plain text. They use OtpEmailQueue instead.
    public class EmailOutboxMessage
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(256)]
        public string ToEmail { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ToName { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string TextBody { get; set; } = string.Empty;

        [Required]
        public string HtmlBody { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
        public int Attempts { get; set; }
        public DateTime? SentAtUtc { get; set; }

        [MaxLength(500)]
        public string? LastError { get; set; }
    }
}
