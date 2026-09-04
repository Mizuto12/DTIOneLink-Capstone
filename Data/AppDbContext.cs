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
    public DbSet<TaskSubmission> TaskSubmissions { get; set; }
public DbSet<TaskActivity> TaskActivities { get; set; }
public DbSet<TaskComment> TaskComments { get; set; }

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
}
}
}