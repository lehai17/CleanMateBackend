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
    public DbSet<Payment> Payments => Set<Payment>();


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
        b.Entity<Booking>().Property(p => p.CustomerConfirmedAt).HasConversion(utc);
        b.Entity<Booking>().Property(p => p.CustomerReview).HasMaxLength(1000);
        b.Entity<Booking>().Property(p => p.ContactName).HasMaxLength(200);
        b.Entity<Booking>().Property(p => p.ContactPhone).HasMaxLength(40);
        b.Entity<Booking>().Property(p => p.RequestHash).HasMaxLength(64);
        b.Entity<Booking>().HasIndex(p => new { p.UserId, p.RequestId }).IsUnique();
        b.Entity<Booking>().HasIndex(p => p.UserId);
        b.Entity<Payment>().Property(p => p.CreatedAt).HasConversion(utc);
        b.Entity<Payment>().Property(p => p.PaidAt).HasConversion(utc);
        b.Entity<Payment>().HasOne(p => p.Booking).WithOne(p => p.Payment)
            .HasForeignKey<Payment>(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasIndex(p => p.BookingId).IsUnique();
        b.Entity<Payment>().HasIndex(p => p.PayOsOrderCode).IsUnique();
        b.Entity<Payment>().HasIndex(p => p.PayOsLinkId).IsUnique();
        b.Entity<Payment>().Property(p => p.PayOsLinkId).HasMaxLength(64);
        b.Entity<Payment>().Property(p => p.BankAccountNumber).HasMaxLength(64);
        b.Entity<Payment>().Property(p => p.BankAccountName).HasMaxLength(256);
        b.Entity<Payment>().Property(p => p.BankTransactionReference).HasMaxLength(128);
        b.Entity<Payment>().HasIndex(p => new { p.ProviderPartnerCode, p.BankTransactionReference }).IsUnique();
        b.Entity<Payment>().HasIndex(p => new { p.ProviderEnvironment, p.ProviderTransactionId }).IsUnique();
        b.Entity<Payment>().Property(p => p.ProviderPartnerCode).HasMaxLength(50);
        b.Entity<Payment>().Property(p => p.ProviderEnvironment).HasMaxLength(16);
        b.Entity<Payment>().Property(p => p.PayUrl).HasMaxLength(2048);
        b.Entity<Payment>().Property(p => p.Method).HasMaxLength(16);
        b.Entity<Payment>().Property(p => p.Status).HasMaxLength(16);
        b.Entity<Payment>().Property(p => p.Amount).HasPrecision(18, 2);
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
