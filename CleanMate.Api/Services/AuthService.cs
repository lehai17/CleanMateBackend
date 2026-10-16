using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Google.Apis.Auth;
using BCryptNet = BCrypt.Net.BCrypt;

namespace CleanMate.Api.Services;

public class AuthService
{
    private readonly CurrentUserService _currentUserService;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;

    public AuthService(CurrentUserService currentUserService, JwtTokenService jwtTokenService, IConfiguration configuration)
    {
        _currentUserService = currentUserService;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
    }

    public async Task<AuthResponseDto> RegisterAsync(AppDbContext db, RegisterDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        // Public registration never grants a staff role or immediate cleaner access.
        var requestedRole = UserRole.Customer;

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            PasswordHash = BCryptNet.HashPassword(dto.Password),
            PhoneNumber = _currentUserService.NormalizeOptional(dto.PhoneNumber),
            Address = _currentUserService.NormalizeOptional(dto.Address),
            Role = requestedRole
        };

        if (dto.Role == UserRole.Cleaner)
        {
            user.CleanerProfile = new CleanerProfile
            {
                UserId = user.Id,
                SubmittedAt = DateTime.UtcNow,
                Bio = "New cleaner",
                HourlyRate = 120000m,
                City = "Ha Noi",
                AddressText = user.Address ?? string.Empty
            };
        }

        db.Users.Add(user);
        if (user.CleanerProfile is null) await db.SaveChangesAsync();
        else
        {
            var recipients = await ManagementNotifications.RecipientsAsync(db,0);
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
            ManagementNotifications.Add(db,recipients,$"ApplicationSubmitted:{user.CleanerProfile.Id}:initial","Applications",user.CleanerProfile.Id,
                "Có hồ sơ cleaner mới",$"Hồ sơ HS{user.CleanerProfile.Id} đang chờ xét duyệt. Mở Hồ sơ cleaner để kiểm tra.");
            await db.SaveChangesAsync();await transaction.CommitAsync();
        }
        return _jwtTokenService.CreateAuthResponse(user, _currentUserService.MapUser(user));
    }

    public async Task<AuthResponseDto?> LoginAsync(AppDbContext db, LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || dto.Email.Length > 254 || string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length > 128) return null;
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(entry => entry.CleanerProfile)
            .FirstOrDefaultAsync(entry => entry.Email == email);

        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash) || !BCryptNet.Verify(dto.Password, user.PasswordHash))
        {
            return null;
        }

        return _jwtTokenService.CreateAuthResponse(user, _currentUserService.MapUser(user));
    }

    public async Task<AuthResponseDto?> LoginWithGoogleAsync(AppDbContext db, GoogleLoginDto dto)
    {
        var audiences = _configuration.GetSection("Authentication:Google:ClientIds").Get<string[]>()
            ?? Array.Empty<string>();
        var clientId = _configuration["Authentication:Google:ClientId"];
        if (!string.IsNullOrWhiteSpace(clientId)) audiences = audiences.Append(clientId).ToArray();
        if (audiences.Length == 0) return null;
        GoogleJsonWebSignature.Payload info;
        try
        {
            info = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = audiences });
        }
        catch (InvalidJwtException) { return null; }
        if (!info.EmailVerified) return null;
        var email = info.Email;
        var name = info.Name;

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

        return !user.IsActive ? null : _jwtTokenService.CreateAuthResponse(user, _currentUserService.MapUser(user));
    }

    public Task<bool> EmailExistsAsync(AppDbContext db, string email)
    {
        return db.Users.AnyAsync(user => user.Email == email);
    }
}
