namespace CleanMate.Api.Contracts;

// Bind only customer input, never price, owner or status.
public record CreateBookingDto(string ServiceKey, DateTimeOffset StartTime, int DurationHours,
    string Address, string? Notes, string PaymentMethod = "cash");