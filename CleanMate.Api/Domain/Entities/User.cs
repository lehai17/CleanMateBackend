namespace CleanMate.Api.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Customer;
    public bool IsActive { get; set; } = true;
    public int TokenVersion { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }

    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }

    public CleanerProfile? CleanerProfile { get; set; }
}
