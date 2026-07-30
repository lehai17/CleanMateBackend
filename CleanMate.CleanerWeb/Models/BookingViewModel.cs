namespace CleanMate.CleanerWeb.Models
{
    public class BookingViewModel
    {
        public int Id { get; set; }
        public string OrderCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public decimal Price { get; set; }
        public string Address { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public int DurationHours { get; set; }
        public string Notes { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
