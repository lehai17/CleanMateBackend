using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CleanMate.Api.Services;

public class CurrentUserService
{
    public int? GetCurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(sub, out var userId) ? userId : null;
    }

    public async Task<User?> GetCurrentUserAsync(AppDbContext db, ClaimsPrincipal principal)
    {
        var userId = GetCurrentUserId(principal);
        if (userId is null)
        {
            return null;
        }

        return await db.Users
            .Include(user => user.CleanerProfile)
            .FirstOrDefaultAsync(user => user.Id == userId.Value);
    }

    public async Task<CleanerProfile?> GetCleanerProfileAsync(AppDbContext db, ClaimsPrincipal principal)
    {
        var user = await GetCurrentUserAsync(db, principal);
        return user?.Role == UserRole.Cleaner ? user.CleanerProfile : null;
    }

    public UserResponseDto MapUser(User user)
    {
        var cleanerProfile = user.CleanerProfile is null
            ? null
            : new CleanerProfileResponseDto(
                user.CleanerProfile.Id,
                user.CleanerProfile.Bio,
                user.CleanerProfile.City,
                user.CleanerProfile.AddressText,
                user.CleanerProfile.HourlyRate,
                user.CleanerProfile.AvgRating);

        return new UserResponseDto(
            user.Id,
            user.FullName,
            user.Email,
            user.Role.ToString(),
            user.PhoneNumber,
            user.Address,
            user.CleanerProfile is not null,
            cleanerProfile);
    }

    public string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
