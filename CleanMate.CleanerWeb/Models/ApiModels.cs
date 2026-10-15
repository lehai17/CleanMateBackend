namespace CleanMate.CleanerWeb.Models;

public record ApiCleanerProfile(int Id, string Bio, string City, string AddressText, decimal HourlyRate, double AvgRating);
public record ApiUser(int Id, string FullName, string Email, string Role, string? PhoneNumber,
    string? Address, bool IsCleaner, ApiCleanerProfile? CleanerProfile);
public record ApiAuth(string Token, ApiUser User);
