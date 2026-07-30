using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Infrastructure
{
    public class AppDbContextSqlServer : AppDbContext
    {
        public AppDbContextSqlServer(DbContextOptions options)
            : base(options)
        {
        }
    }
}
