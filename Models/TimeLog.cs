using System.ComponentModel.DataAnnotations.Schema;

namespace DTIOneLink.Models
{
    // One row per sign-in session. Created with TimeInUtc when the user signs
    // in (AccountController.SignIn); TimeOutUtc is filled in only on an
    // explicit Logout — a session that merely expires from inactivity leaves
    // TimeOutUtc null, same as LastLogoutAtUtc on User.
    public class TimeLog
    {
        public int Id { get; set; }

        [ForeignKey(nameof(User))]
        public int UserId { get; set; }
        public User? User { get; set; }

        public DateTime TimeInUtc { get; set; } = DateTime.UtcNow;
        public DateTime? TimeOutUtc { get; set; }
    }
}
