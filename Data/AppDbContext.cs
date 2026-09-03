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
    }
}
}