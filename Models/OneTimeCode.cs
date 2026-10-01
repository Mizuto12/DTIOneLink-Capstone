using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    public enum OneTimeCodePurpose
    {
        // Stored as an int, so new values must only ever be appended.
        ConfirmEmail = 1,
        PasswordReset = 2
    }

    // A 6-digit code emailed to a user. Only a keyed hash of the code is
    // stored (see OneTimeCodeService), never the code itself.
    public class OneTimeCode
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        public OneTimeCodePurpose Purpose { get; set; }

        [Required]
        [MaxLength(64)]
        public string CodeHash { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAtUtc { get; set; }

        // Wrong entries against this code; it stops working at the limit.
        public int FailedAttempts { get; set; }

        // Set when the code is used, or cancelled by a newer code.
        public DateTime? ConsumedAtUtc { get; set; }
    }
}
