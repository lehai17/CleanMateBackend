using Microsoft.EntityFrameworkCore;

namespace CleanMate.Infrastructure
{
    public class AppDbContextSqlServer : AppDbContext
    {
        public AppDbContextSqlServer(DbContextOptions<AppDbContextSqlServer> options)
            : base(options) // ✅ truyền đúng options
        {
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer("Server=(local);Database=CleanMateDb;User Id=sa;Password=sa;TrustServerCertificate=True;");
            }
        }
    }
}
