using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using BCryptNet = BCrypt.Net.BCrypt;

namespace CleanMate.Api.Services;

public class AuthService
{
    private readonly CurrentUserService _currentUserService;
    private readonly JwtTokenService _jwtTokenService;

    public AuthService(CurrentUserService currentUserService, JwtTokenService jwtTokenService)
    {
        _currentUserService = currentUserService;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResponseDto> RegisterAsync(AppDbContext db, RegisterDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var requestedRole = dto.Role == UserRole.Admin ? UserRole.Customer : dto.Role;

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            PasswordHash = BCryptNet.HashPassword(dto.Password),
            PhoneNumber = _currentUserService.NormalizeOptional(dto.PhoneNumber),
            Address = _currentUserService.NormalizeOptional(dto.Address),
            Role = requestedRole
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (requestedRole == UserRole.Cleaner)
        {
            db.CleanerProfiles.Add(new CleanerProfile
            {
                UserId = user.Id,
                Bio = "New cleaner",
                HourlyRate = 120000m,
                City = "Ha Noi",
                AddressText = user.Address ?? string.Empty
            });
            await db.SaveChangesAsync();
        }

        await db.Entry(user).Reference(entry => entry.CleanerProfile).LoadAsync();
        return _jwtTokenService.CreateAuthResponse(user, _currentUserService.MapUser(user));
    }

    public async Task<AuthResponseDto?> LoginAsync(AppDbContext db, LoginDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(entry => entry.CleanerProfile)
            .FirstOrDefaultAsync(entry => entry.Email == email);

        if (user is null || !BCryptNet.Verify(dto.Password, user.PasswordHash))
        {
            return null;
        }

        return _jwtTokenService.CreateAuthResponse(user, _currentUserService.MapUser(user));
    }

    public async Task<AuthResponseDto?> LoginWithGoogleAsync(AppDbContext db, GoogleLoginDto dto)
    {
        using var client = new HttpClient();
        var response = await client.GetStringAsync(
            $"https://oauth2.googleapis.com/tokeninfo?id_token={dto.IdToken}");
        var info = JsonDocument.Parse(response).RootElement;

        var email = info.GetProperty("email").GetString();
        var name = info.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(entry => entry.CleanerProfile)
            .FirstOrDefaultAsync(entry => entry.Email == normalizedEmail);

        if (user is null)
        {
            user = new User
            {
                FullName = string.IsNullOrWhiteSpace(name) ? "Google User" : name.Trim(),
                Email = normalizedEmail,
                PasswordHash = string.Empty,
                Role = UserRole.Customer
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        return _jwtTokenService.CreateAuthResponse(user, _currentUserService.MapUser(user));
    }

    public Task<bool> EmailExistsAsync(AppDbContext db, string email)
    {
        return db.Users.AnyAsync(user => user.Email == email);
    }
}
