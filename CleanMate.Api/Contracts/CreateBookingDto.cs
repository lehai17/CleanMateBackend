namespace CleanMate.Api.Contracts;

// Bind only customer input, never price, owner or status.
public record CreateBookingDto(string ServiceKey, DateTimeOffset StartTime, int DurationHours,
    string Address, string? Notes, string PaymentMethod = "cash",
    string? ContactName = null, string? ContactPhone = null, Guid? RequestId = null, decimal? ExpectedPrice = null);
