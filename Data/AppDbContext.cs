using Microsoft.EntityFrameworkCore;
using DTIOneLink.Models;

namespace DTIOneLink.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<TaskItem> TaskItems { get; set; }
        public DbSet<UserItem> UserItems { get; set; }
    }
}
