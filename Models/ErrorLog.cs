using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    // An unhandled exception that reached HomeController.Error. Persisted so
    // it can be diagnosed without server/host log access — this app runs on
    // shared hosting with no IT staff to check IIS/stdout logs.
    public class ErrorLog
    {
        public int Id { get; set; }

        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(2048)]
        public string Path { get; set; } = string.Empty;

        [MaxLength(8)]
        public string Method { get; set; } = string.Empty;

        public int? UserId { get; set; }

        [MaxLength(50)]
        public string? UserRole { get; set; }

        [MaxLength(300)]
        public string ExceptionType { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        // Unbounded: a .NET stack trace for a deep call chain can run long,
        // and truncating it is exactly what makes a report useless.
        public string? StackTrace { get; set; }
    }
}
