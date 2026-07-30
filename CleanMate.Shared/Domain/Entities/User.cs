namespace CleanMate.Domain.Entities
{
    public class User
    {
        public int Id { get; set; }

        // 🧍 Thông tin tài khoản
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // 🔒 Mật khẩu mã hoá
        public string PasswordHash { get; set; } = string.Empty;

        // 🧩 Phân quyền
        public int Role { get; set; }


        // 📞 Thông tin liên hệ
        public string? PhoneNumber { get; set; }

        // 🏠 Địa chỉ chung (nếu có)
        public string? Address { get; set; }

        // 🧭 Liên kết sang bảng khác
        public CleanerProfile? CleanerProfile { get; set; }
        public ICollection<Booking>? Bookings { get; set; }
    }
}
