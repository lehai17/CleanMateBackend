using CleanMate.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CleanMate.Api.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<CleanerProfile> CleanerProfiles => Set<CleanerProfile>();
    public DbSet<Booking> Bookings => Set<Booking>();


    protected override void OnModelCreating(ModelBuilder b)
    {
        // SQL Server datetime2 does not retain DateTime.Kind. Restore UTC on read so
        // browser/mobile clients do not interpret UTC timestamps as their local time.
        var utc = new ValueConverter<DateTime, DateTime>(value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        b.Entity<Booking>().Property(p => p.StartTime).HasConversion(utc);
        b.Entity<Booking>().Property(p => p.CreatedAt).HasConversion(utc);
        b.Entity<Booking>().Property(p => p.StartedAt).HasConversion(utc);
        b.Entity<Booking>().Property(p => p.CompletedAt).HasConversion(utc);
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<User>()
            .HasOne(u => u.CleanerProfile)
            .WithOne(cp => cp.User!)
            .HasForeignKey<CleanerProfile>(cp => cp.UserId);

        // Preserve existing database precision; booking quotes are still calculated in whole VND.
        if (Database.IsNpgsql())
            b.Entity<Booking>().Property(p => p.Price).HasColumnType("numeric");
        else
            b.Entity<Booking>().Property(p => p.Price).HasPrecision(18, 2);

        b.Entity<CleanerProfile>()
            .Property(p => p.HourlyRate)
            .HasPrecision(18, 0);
    }
}
