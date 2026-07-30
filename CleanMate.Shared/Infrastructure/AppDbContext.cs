using Microsoft.EntityFrameworkCore;
using CleanMate.Domain.Entities;

namespace CleanMate.Infrastructure
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<CleanerProfile> CleanerProfiles { get; set; }
        public DbSet<Booking> Bookings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 🔧 Chặn cascade delete gây lỗi multiple path
            modelBuilder.Entity<Booking>()
                .HasOne(b => b.User)
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Restrict); // ✅ Thay vì Cascade

            modelBuilder.Entity<Booking>()
                .HasOne(b => b.Cleaner)
                .WithMany()
                .HasForeignKey(b => b.CleanerId)
                .OnDelete(DeleteBehavior.Restrict); // ✅ Cũng Restrict
        }
    }
}
