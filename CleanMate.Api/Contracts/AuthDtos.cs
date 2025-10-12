namespace CleanMate.Api.Contracts;

using CleanMate.Api.Domain.Entities;

public record RegisterDto(string FullName, string Email, string Password, UserRole Role);
public record LoginDto(string Email, string Password);
