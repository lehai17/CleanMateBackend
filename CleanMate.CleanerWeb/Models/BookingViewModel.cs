namespace CleanMate.CleanerWeb.Models
{
    public class BookingViewModel
    {
        public int Id { get; set; }
        public string OrderCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string? CustomerPhone { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public decimal Price { get; set; }
        public string Address { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public string PaymentStatus { get; set; } = "Unpaid";
        public DateTime? PaidAt { get; set; }
        public DateTime? CustomerConfirmedAt { get; set; }
        public int? CustomerRating { get; set; }
        public int DurationHours { get; set; }
        public string Notes { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
