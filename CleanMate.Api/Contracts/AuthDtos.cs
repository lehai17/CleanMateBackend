namespace CleanMate.Api.Contracts;

using CleanMate.Api.Domain.Entities;

public record RegisterDto(
    string FullName,
    string Email,
    string Password,
    string? PhoneNumber,
    string? Address,
    UserRole Role = UserRole.Customer);

public record LoginDto(string Email, string Password);

public record ProfileUpdateDto(
    string FullName,
    string? PhoneNumber,
    string? Address);

public record CleanerRegistrationDto(
    string Bio,
    string City,
    string AddressText,
    decimal HourlyRate);
