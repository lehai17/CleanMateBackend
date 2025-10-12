namespace CleanMate.Api.Domain.Entities;

public enum UserRole { Customer, Cleaner, Admin }

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Customer;

    public CleanerProfile? CleanerProfile { get; set; }
}
