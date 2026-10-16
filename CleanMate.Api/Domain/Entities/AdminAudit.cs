namespace CleanMate.Api.Domain.Entities;

public class AdminAudit
{
    public int Id { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
