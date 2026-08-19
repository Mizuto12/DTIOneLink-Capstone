using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // Maps to the existing "Users" table — this is the credentials table used
    // for login, distinct from UserItems (the User Management listing page).
    public class User
    {
        public int Id { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string Department { get; set; } = string.Empty;
    }
}
