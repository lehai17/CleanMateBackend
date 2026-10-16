namespace CleanMate.Api.Contracts;

public record CleanerProfileResponseDto(
    int Id,
    string Bio,
    string City,
    string AddressText,
    decimal HourlyRate,
    double AvgRating,
    string ReviewStatus,
    string? ReviewReason);

public record UserResponseDto(
    int Id,
    string FullName,
    string Email,
    string Role,
    string? PhoneNumber,
    string? Address,
    bool IsCleaner,
    CleanerProfileResponseDto? CleanerProfile);

public record AuthResponseDto(
    string Token,
    UserResponseDto User);
