namespace CleanMate.Api.Domain.Entities;

public class CleanerProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string ReviewStatus { get; set; } = "Pending";
    public string? ReviewReason { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedById { get; set; }

    public string Bio { get; set; } = "";
    public decimal HourlyRate { get; set; } = 120000; // VND/h
    public double AvgRating { get; set; } = 0;

    // cho Google Maps sau này
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string City { get; set; } = "Hanoi";
    public string AddressText { get; set; } = "";
}
