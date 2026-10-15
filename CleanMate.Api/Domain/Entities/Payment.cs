namespace CleanMate.Api.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public string Method { get; set; } = "cash";
    public string Status { get; set; } = "Paid";
    public decimal Amount { get; set; }
    public int? CollectedByCleanerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    // Legacy provider columns are retained only to preserve existing schema/history.
    // Runtime payment handling is direct cash; no provider service is registered.
    public string? PayUrl { get; set; }
    public string? ProviderPartnerCode { get; set; }
    public string? ProviderEnvironment { get; set; }
    public long? ProviderTransactionId { get; set; }
    public long? PayOsOrderCode { get; set; }
    public string? PayOsLinkId { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankTransactionReference { get; set; }
}
