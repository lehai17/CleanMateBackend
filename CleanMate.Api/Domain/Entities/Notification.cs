namespace CleanMate.Api.Domain.Entities;

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BookingId { get; set; }
    public string EventKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool ForCleaner { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
