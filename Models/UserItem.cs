using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DTIOneLink.Models
{
    public class UserItem
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Full name is required.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        // Not [Required]: only populated when reading existing rows (Index),
        // never posted by the Create form — Create derives Username from Email.
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email address.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role is required.")]
        public string Role { get; set; } = string.Empty;

        [Required(ErrorMessage = "Department is required.")]
        public string Department { get; set; } = string.Empty;

        [Required(ErrorMessage = "Status is required.")]
        public string Status { get; set; } = "active";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Sign-in status (UTC, read-only; see User.cs). Null = never. Read from
        // dbo.Users for User Management; [NotMapped] so the old UserItems
        // table (UserItem is also a DbSet) doesn't get these columns.
        [NotMapped] public DateTime? LastLoginAtUtc { get; set; }
        [NotMapped] public DateTime? LastLogoutAtUtc { get; set; }
        [NotMapped] public DateTime? LastSeenAtUtc { get; set; }

        // Logged in after their last Sign Out, and heard from within the
        // 30-minute session timeout (Program.cs AddSession).
        public bool IsSignedIn =>
            LastLoginAtUtc != null
            && (LastLogoutAtUtc == null || LastLogoutAtUtc < LastLoginAtUtc)
            && LastSeenAtUtc > DateTime.UtcNow.AddMinutes(-30);
    }
}