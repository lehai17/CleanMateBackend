using CleanMate.Api.Domain.Entities;

public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; } // người đặt (customer)
    public int? CleanerId { get; set; } // người nhận việc (cleaner) <-- thêm

    public DateTime StartTime { get; set; }
    public decimal Price { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public DateTime CreatedAt { get; set; }

    // Optional
    public string? Notes { get; set; }
    public int DurationHours { get; set; }

    // Navigation
    public User? User { get; set; } // Customer
    public CleanerProfile? Cleaner { get; set; } // Cleaner
}
