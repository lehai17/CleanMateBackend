using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanMate.Infrastructure
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContextSqlServer>
    {
        public AppDbContextSqlServer CreateDbContext(string[] args)
        {
            var connectionString = "Server=(local);Database=CleanMateDb;User Id=sa;Password=sa;TrustServerCertificate=True;";

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContextSqlServer>();
            optionsBuilder.UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly("CleanMate.Shared")); 

            return new AppDbContextSqlServer(optionsBuilder.Options);
        }
    }
}
