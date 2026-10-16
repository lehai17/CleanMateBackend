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
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ServiceOffering> ServiceOfferings => Set<ServiceOffering>();
    public DbSet<AdminAudit> AdminAudits => Set<AdminAudit>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<AccountCode> AccountCodes => Set<AccountCode>();


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
        b.Entity<Notification>().HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Notification>().HasOne<Booking>().WithMany().HasForeignKey(n => n.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Notification>().Property(n => n.EventKey).HasMaxLength(128);
        b.Entity<Notification>().Property(n => n.Title).HasMaxLength(200);
        b.Entity<Notification>().Property(n => n.Message).HasMaxLength(1000);
        b.Entity<Notification>().Property(n => n.CreatedAt).HasConversion(utc);
        b.Entity<Notification>().Property(n => n.ReadAt).HasConversion(utc);
        b.Entity<Notification>().HasIndex(n => new { n.UserId, n.EventKey }).IsUnique();
        b.Entity<Notification>().HasIndex(n => new { n.UserId, n.Id });
        b.Entity<Notification>().HasIndex(n => new { n.UserId, n.ReadAt });
        b.Entity<Notification>().Property(n => n.Target).HasMaxLength(32);
        b.Entity<User>().Property(u => u.IsActive).HasDefaultValue(true);
        b.Entity<User>().Property(u => u.TokenVersion).HasDefaultValue(0);
        b.Entity<User>().Property(u => u.EmailVerifiedAt).HasConversion(utc);
        b.Entity<AccountCode>().HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccountCode>().Property(c => c.Purpose).HasMaxLength(32);
        b.Entity<AccountCode>().Property(c => c.Hash).HasMaxLength(64);
        b.Entity<AccountCode>().HasIndex(c => c.Hash).IsUnique();
        b.Entity<AccountCode>().HasIndex(c => new { c.UserId, c.Purpose, c.IssueWindow }).IsUnique();
        b.Entity<AccountCode>().HasIndex(c => c.ExpiresAt);
        b.Entity<AccountCode>().Property(c => c.CreatedAt).HasConversion(utc);
        b.Entity<AccountCode>().Property(c => c.ExpiresAt).HasConversion(utc);
        b.Entity<AccountCode>().Property(c => c.DeliveredAt).HasConversion(utc);
        b.Entity<AccountCode>().Property(c => c.ConsumedAt).HasConversion(utc);
        b.Entity<CleanerProfile>().Property(c => c.ReviewStatus).HasMaxLength(32).HasDefaultValue("Pending");
        b.Entity<CleanerProfile>().Property(c => c.ReviewReason).HasMaxLength(1000);
        b.Entity<CleanerProfile>().Property(c => c.SubmittedAt).HasConversion(utc);
        b.Entity<CleanerProfile>().Property(c => c.ReviewedAt).HasConversion(utc);
        b.Entity<CleanerProfile>().HasOne<User>().WithMany().HasForeignKey(c => c.ReviewedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ServiceOffering>().HasKey(s => s.Key);
        b.Entity<ServiceOffering>().Property(s => s.Key).HasMaxLength(32);
        b.Entity<ServiceOffering>().Property(s => s.Name).HasMaxLength(200);
        b.Entity<ServiceOffering>().Property(s => s.HourlyRate).HasPrecision(18, 2);
        b.Entity<ServiceOffering>().Property(s => s.UpdatedAt).HasConversion(utc);
        b.Entity<ServiceOffering>().HasData(
            new ServiceOffering { Key = "cleaning", Name = "Dọn Dẹp Tổng Quát", HourlyRate = 70000 },
            new ServiceOffering { Key = "cooking", Name = "Nấu Ăn & Rửa Bát", HourlyRate = 90000 },
            new ServiceOffering { Key = "laundry", Name = "Giặt Ủi Quần Áo", HourlyRate = 70000 },
            new ServiceOffering { Key = "childcare", Name = "Chăm Sóc Trẻ Em", HourlyRate = 100000 });
        b.Entity<AdminAudit>().HasOne<User>().WithMany().HasForeignKey(a => a.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AdminAudit>().Property(a => a.Action).HasMaxLength(64);
        b.Entity<AdminAudit>().Property(a => a.Target).HasMaxLength(128);
        b.Entity<AdminAudit>().Property(a => a.Details).HasMaxLength(12000);
        b.Entity<AdminAudit>().Property(a => a.CreatedAt).HasConversion(utc);
        b.Entity<SupportTicket>().HasOne<Booking>().WithMany().HasForeignKey(t => t.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne(t => t.Reporter).WithMany().HasForeignKey(t => t.ReporterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne<User>().WithMany().HasForeignKey(t => t.RespondedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasOne(t => t.AssignedTo).WithMany().HasForeignKey(t => t.AssignedToId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportTicket>().HasIndex(t => new { t.AssignedToId, t.Status, t.Id });
        b.Entity<SupportTicket>().Property(t => t.AssignedAt).HasConversion(utc);
        b.Entity<SupportTicket>().HasIndex(t => new { t.ReporterId, t.RequestId }).IsUnique();
        b.Entity<SupportTicket>().HasIndex(t => new { t.Status, t.Id });
        b.Entity<SupportTicket>().Property(t => t.Category).HasMaxLength(32);
        b.Entity<SupportTicket>().Property(t => t.Status).HasMaxLength(32);
        b.Entity<SupportTicket>().Property(t => t.Description).HasMaxLength(2000);
        b.Entity<SupportTicket>().Property(t => t.Response).HasMaxLength(2000);
        b.Entity<SupportTicket>().Property(t => t.CreatedAt).HasConversion(utc);
        b.Entity<SupportTicket>().Property(t => t.UpdatedAt).HasConversion(utc);
        b.Entity<SupportMessage>().HasOne<SupportTicket>().WithMany().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportMessage>().HasOne<User>().WithMany().HasForeignKey(m => m.AuthorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupportMessage>().HasIndex(m => new { m.AuthorId, m.RequestId }).IsUnique();
        b.Entity<SupportMessage>().HasIndex(m => new { m.TicketId, m.Id });
        b.Entity<SupportMessage>().Property(m => m.Body).HasMaxLength(2000);
        b.Entity<SupportMessage>().Property(m => m.CreatedAt).HasConversion(utc);
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
