namespace CleanMate.Api.Domain.Entities;

public class SupportMessage
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int AuthorId { get; set; }
    public Guid RequestId { get; set; }
    public bool FromStaff { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
