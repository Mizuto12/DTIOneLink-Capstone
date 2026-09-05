using Microsoft.EntityFrameworkCore;
using DTIOneLink.Models;

namespace DTIOneLink.Data
{
    public class AppDbContext : DbContext
{
    public string connectionString = "Data Source=localhost\\SQLEXPRESS02;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True" ;
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TaskItem> TaskItems { get; set; }
    public DbSet<UserItem> UserItems { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<TaskAssignment> TaskAssignments { get; set; }
    public DbSet<TaskSubmission> TaskSubmissions { get; set; }
    public DbSet<TaskActivity> TaskActivities { get; set; }
    public DbSet<TaskComment> TaskComments { get; set; }
    public DbSet<Notification> Notifications { get; set; }

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Deleting a User should not cascade-delete their activity log
    // entries — TaskActivity already cascades from TaskItem, and
    // SQL Server refuses a second cascade path to the same table
    // via Users. Restrict here; TaskItem's own cascade still applies.
    modelBuilder.Entity<TaskActivity>()
        .HasOne(a => a.PerformedBy)
        .WithMany()
        .HasForeignKey(a => a.PerformedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    // Same multiple-cascade-path issue as TaskActivity above — restrict
    // the Author FK so deleting a User doesn't try to cascade through
    // two different paths to reach TaskComments.
    modelBuilder.Entity<TaskComment>()
    .HasOne(c => c.Author)
    .WithMany()
    .HasForeignKey(c => c.AuthorUserId)
    .OnDelete(DeleteBehavior.Restrict);

// TaskActivity already cascades from TaskItem — adding a second cascade
// path via TaskSubmission would hit the same SQL Server "multiple cascade
// paths" error we saw earlier. Restrict here for the same reason.
modelBuilder.Entity<TaskActivity>()
    .HasOne(a => a.RelatedSubmission)
    .WithMany()
    .HasForeignKey(a => a.RelatedSubmissionId)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<Notification>()
    .HasOne(n => n.Recipient)
    .WithMany()
    .HasForeignKey(n => n.RecipientUserId)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<Notification>()
    .HasOne(n => n.RelatedTask)
    .WithMany()
    .HasForeignKey(n => n.RelatedTaskId)
    .OnDelete(DeleteBehavior.SetNull);

modelBuilder.Entity<Notification>()
    .HasIndex(n => new { n.RecipientUserId, n.IsRead });

    // ── Multi-employee task assignment ───────────────────────────
    // TaskAssignment.TaskId keeps its default cascade from TaskItem (that's
    // the only cascade path reaching it, so no SQL Server conflict). The
    // User-facing FKs are restricted for the same "multiple cascade paths"
    // reason as TaskActivity/TaskComment above.
    modelBuilder.Entity<TaskAssignment>()
        .HasIndex(a => new { a.TaskId, a.UserId })
        .IsUnique();

    modelBuilder.Entity<TaskAssignment>()
        .HasOne(a => a.User)
        .WithMany()
        .HasForeignKey(a => a.UserId)
        .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<TaskAssignment>()
        .HasOne(a => a.AssignedBy)
        .WithMany()
        .HasForeignKey(a => a.AssignedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    // A submission now belongs to one assignee's TaskAssignment. Restrict
    // (not cascade) so deleting a TaskAssignment can't create a second
    // cascade path alongside TaskSubmission's existing cascade from Task.
    modelBuilder.Entity<TaskSubmission>()
        .HasOne(s => s.TaskAssignment)
        .WithMany()
        .HasForeignKey(s => s.TaskAssignmentId)
        .OnDelete(DeleteBehavior.Restrict);
}
}
}