using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Google.Apis.Auth;
using Microsoft.Data.SqlClient;
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
        var passwordHash = BCryptNet.HashPassword(dto.Password);
        for (var attempt = 0; ; attempt++)
        {
            try { return await RegisterOnceAsync(db, dto, email, passwordHash); }
            catch (Exception exception) when (attempt < 2 && db.Database.IsSqlServer() && IsDeadlock(exception))
            {
                // SQL Server rolls back deadlock victims. Retry the whole unit with fresh
                // entities/IDs; arbitrary SQL failures and ambiguous commits are not retried.
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(25, 101) * (attempt + 1));
            }
        }
    }

    private async Task<AuthResponseDto> RegisterOnceAsync(AppDbContext db, RegisterDto dto, string email, string passwordHash)
    {
        // Public registration never grants a staff role or immediate cleaner access.
        var requestedRole = UserRole.Customer;
        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            PasswordHash = passwordHash,
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
            // A large SQL Server MERGE can scan Users for FK validation while other
            // signups hold new-user locks. Single-recipient inserts avoid that lock cycle.
            foreach (var recipient in recipients.Order())
            {
                ManagementNotifications.Add(db,[recipient],$"ApplicationSubmitted:{user.CleanerProfile.Id}:initial","Applications",user.CleanerProfile.Id,
                    "Có hồ sơ cleaner mới",$"Hồ sơ HS{user.CleanerProfile.Id} đang chờ xét duyệt. Mở Hồ sơ cleaner để kiểm tra.");
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }
        return _jwtTokenService.CreateAuthResponse(user, _currentUserService.MapUser(user));
    }

    private static bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 }) return true;
        return false;
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
