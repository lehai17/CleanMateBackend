using CleanMate.Api.Domain.Entities;

public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; } // người đặt (customer)
    public int? CleanerId { get; set; } // người nhận việc (cleaner) <-- thêm
    public Guid? RequestId { get; set; }
    public string? RequestHash { get; set; }

    public DateTime StartTime { get; set; }
    public decimal Price { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CustomerConfirmedAt { get; set; }
    public int? CustomerRating { get; set; }
    public string? CustomerReview { get; set; }
    public Payment? Payment { get; set; }

    // Optional
    public string? Notes { get; set; }
    public int DurationHours { get; set; }

    // Navigation
    public User? User { get; set; } // Customer
    public CleanerProfile? Cleaner { get; set; } // Cleaner

    public string Status { get; set; } = BookingStatuses.Pending;

}
