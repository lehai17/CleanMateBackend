using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CleanMate.Domain.Entities
{
    [Table("Bookings")]
    public class Booking
    {
        [Key]
        public int Id { get; set; }

        // 🔗 Khóa ngoại
        public int UserId { get; set; }
        public int CleanerId { get; set; }

        // 🕒 Thông tin thời gian
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        // 💰 Thông tin thanh toán
        public decimal Price { get; set; }                // Giá tiền
        public string? PaymentMethod { get; set; }        // Ví dụ: "Cash", "Momo", "CreditCard"

        // 🏡 Thông tin địa điểm
        public string? Address { get; set; }

        // 📋 Mô tả & ghi chú
        public int DurationHours { get; set; }            // Số giờ làm việc
        public string? Notes { get; set; }

        // 🔖 Trạng thái
        public string Status { get; set; } = "Pending";   // Pending / Accepted / Completed / Cancelled

        // 🔗 Navigation properties
        public User? User { get; set; }
        public CleanerProfile? Cleaner { get; set; }
    }
}
