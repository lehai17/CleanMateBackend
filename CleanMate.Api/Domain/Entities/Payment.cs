namespace CleanMate.Api.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public string Method { get; set; } = "cash";
    public string Status { get; set; } = "Paid";
    public decimal Amount { get; set; }
    public int CollectedByCleanerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime PaidAt { get; set; }
}
