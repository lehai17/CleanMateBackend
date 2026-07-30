using System.ComponentModel.DataAnnotations.Schema;

namespace CleanMate.Domain.Entities
{
    public class CleanerProfile
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        // 🏙️ Thông tin hiển thị trên giao diện
        public string? City { get; set; }                 // Thành phố làm việc
        [Column("Address")]
        public string? AddressText { get; set; }          // Địa chỉ chi tiết
        public string? Bio { get; set; }                  // Mô tả bản thân

        // 💵 Thông tin dịch vụ
        public decimal HourlyRate { get; set; }           // Giá mỗi giờ (VND)
        public double AvgRating { get; set; }             // Điểm trung bình đánh giá

        // 🔗 Quan hệ với User
        public User? User { get; set; }
    }
}
