using CleanMate.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<CleanerProfile> CleanerProfiles => Set<CleanerProfile>();
    public DbSet<Booking> Bookings => Set<Booking>();


    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<User>()
            .HasOne(u => u.CleanerProfile)
            .WithOne(cp => cp.User!)
            .HasForeignKey<CleanerProfile>(cp => cp.UserId);

        b.Entity<CleanerProfile>()
        .Property(p => p.HourlyRate)
        .HasPrecision(18, 0);
    }
}
