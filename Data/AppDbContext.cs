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
        // TaskItem already has a cascading FK to Users via AssigneeId — a second
    // path to the same table would hit the familiar "multiple cascade paths"
    // error. Restrict here; the creator record outlives nothing.
    modelBuilder.Entity<TaskItem>()
        .HasOne(t => t.CreatedBy)
        .WithMany()
        .HasForeignKey(t => t.CreatedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    // Self-referencing FK (main task <-> subtasks). SQL Server rejects
    // cascade here outright — a row can't cascade-delete into itself — so
    // Restrict is required, not just conventional: deleting a main task
    // with subtasks must fail (or be handled explicitly in code) rather
    // than silently deleting or orphaning its children.
    modelBuilder.Entity<TaskItem>()
        .HasOne(t => t.ParentTask)
        .WithMany(t => t.Subtasks)
        .HasForeignKey(t => t.ParentTaskId)
        .OnDelete(DeleteBehavior.Restrict);

    // AssigneeId is now nullable (a Main Task has no individual assignee).
    // Configured explicitly, switching from the previous Cascade to
    // Restrict — deleting a User should no longer silently delete every
    // task ever assigned to them; Cascade only existed because AssigneeId
    // used to be a required, non-nullable FK.
    modelBuilder.Entity<TaskItem>()
        .HasOne(t => t.Assignee)
        .WithMany()
        .HasForeignKey(t => t.AssigneeId)
        .OnDelete(DeleteBehavior.Restrict);

    // Third FK from TaskItem to Users (after Assignee, CreatedBy) — same
    // multiple-cascade-paths constraint as those two, so Restrict here too.
    modelBuilder.Entity<TaskItem>()
        .HasOne(t => t.ResponsibleAdmin)
        .WithMany()
        .HasForeignKey(t => t.ResponsibleAdminUserId)
        .OnDelete(DeleteBehavior.Restrict);

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