namespace CleanMate.Api.Domain.Entities;

public class SupportTicket
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public int ReporterId { get; set; }
    public Guid RequestId { get; set; }
    public string Category { get; set; } = "Other";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Open";
    public string Response { get; set; } = "";
    public int Version { get; set; } = 1;
    public int? RespondedById { get; set; }
    public int? AssignedToId { get; set; }
    public DateTime? AssignedAt { get; set; }
    public User? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public User? Reporter { get; set; }
}
