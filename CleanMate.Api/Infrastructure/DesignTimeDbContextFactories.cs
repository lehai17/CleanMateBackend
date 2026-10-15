using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanMate.Api.Infrastructure;

// Schema generation never connects to or loads credentials for an application database.
public class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContextSqlServer>
{
    public AppDbContextSqlServer CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AppDbContextSqlServer>()
            .UseSqlServer("Server=localhost;Database=CleanMateDesign;Integrated Security=true;Encrypt=false")
            .Options);
}

public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=cleanmate_design;Username=design;Password=design-only")
            .Options);
}
