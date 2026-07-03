using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext>options): base(options)
        {
            
        }
    }
}