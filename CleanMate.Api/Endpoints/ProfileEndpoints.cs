using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using System.Security.Claims;

namespace CleanMate.Api.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(db, principal);
            return user is null ? Results.Unauthorized() : Results.Ok(currentUserService.MapUser(user));
        }).RequireAuthorization();

        var group = app.MapGroup("/api/profile").WithTags("Profile").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(db, principal);
            return user is null ? Results.Unauthorized() : Results.Ok(currentUserService.MapUser(user));
        });

        group.MapPut("/", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal, ProfileUpdateDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return Results.BadRequest(new { message = "Họ tên là bắt buộc." });
            var user = await currentUserService.GetCurrentUserAsync(db, principal);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            user.FullName = dto.FullName.Trim();
            user.PhoneNumber = currentUserService.NormalizeOptional(dto.PhoneNumber);
            user.Address = currentUserService.NormalizeOptional(dto.Address);

            await db.SaveChangesAsync();
            return Results.Ok(currentUserService.MapUser(user));
        });

        group.MapPost("/register-cleaner", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal, CleanerRegistrationDto dto) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(db, principal);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (user.CleanerProfile is not null)
            {
                return Results.BadRequest(new { message = "User is already registered as cleaner." });
            }

            user.Role = UserRole.Cleaner;
            user.CleanerProfile = new CleanerProfile
            {
                UserId = user.Id,
                Bio = dto.Bio?.Trim() ?? string.Empty,
                City = string.IsNullOrWhiteSpace(dto.City) ? "Ha Noi" : dto.City.Trim(),
                AddressText = string.IsNullOrWhiteSpace(dto.AddressText)
                    ? user.Address ?? string.Empty
                    : dto.AddressText.Trim(),
                HourlyRate = dto.HourlyRate <= 0 ? 120000m : dto.HourlyRate
            };

            await db.SaveChangesAsync();
            return Results.Ok(currentUserService.MapUser(user));
        });

        app.MapPut("/api/cleaner/profile", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal, CleanerProfileUpdateDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return Results.BadRequest(new { message = "Họ tên là bắt buộc." });
            var user = await currentUserService.GetCurrentUserAsync(db, principal);
            if (user?.CleanerProfile is null)
            {
                return Results.NotFound(new { message = "Cleaner profile not found." });
            }

            user.FullName = dto.FullName.Trim();
            user.PhoneNumber = currentUserService.NormalizeOptional(dto.PhoneNumber);
            user.CleanerProfile.AddressText = dto.AddressText?.Trim() ?? string.Empty;
            user.CleanerProfile.Bio = dto.Bio?.Trim() ?? string.Empty;
            user.CleanerProfile.City = string.IsNullOrWhiteSpace(dto.City) ? "Ha Noi" : dto.City.Trim();
            user.CleanerProfile.HourlyRate = dto.HourlyRate <= 0
                ? user.CleanerProfile.HourlyRate
                : dto.HourlyRate;

            await db.SaveChangesAsync();
            return Results.Ok(currentUserService.MapUser(user));
        }).RequireAuthorization();

        return app;
    }
}
