namespace CleanMate.Api.Domain.Entities;

public class AccountCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Purpose { get; set; } = "";
    public string Hash { get; set; } = "";
    public int TokenVersion { get; set; }
    public long IssueWindow { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
