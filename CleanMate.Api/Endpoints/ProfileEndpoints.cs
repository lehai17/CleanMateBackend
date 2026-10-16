using CleanMate.Api.Contracts;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

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
            if (user.Role != UserRole.Customer) return Results.Forbid();

            var recipients = await ManagementNotifications.RecipientsAsync(db,user.Id);
            await using var transaction = await db.Database.BeginTransactionAsync();
            user.CleanerProfile = new CleanerProfile
            {
                UserId = user.Id,
                SubmittedAt = DateTime.UtcNow,
                Bio = dto.Bio?.Trim() ?? string.Empty,
                City = string.IsNullOrWhiteSpace(dto.City) ? "Ha Noi" : dto.City.Trim(),
                AddressText = string.IsNullOrWhiteSpace(dto.AddressText)
                    ? user.Address ?? string.Empty
                    : dto.AddressText.Trim(),
                HourlyRate = dto.HourlyRate <= 0 ? 120000m : dto.HourlyRate
            };

            await db.SaveChangesAsync();
            ManagementNotifications.Add(db,recipients,$"ApplicationSubmitted:{user.CleanerProfile.Id}:initial","Applications",user.CleanerProfile.Id,
                "Có hồ sơ cleaner mới",$"Hồ sơ HS{user.CleanerProfile.Id} đang chờ xét duyệt. Mở Hồ sơ cleaner để kiểm tra.");
            await db.SaveChangesAsync();await transaction.CommitAsync();
            return Results.Ok(currentUserService.MapUser(user));
        });

        group.MapPut("/cleaner-application", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, CleanerRegistrationDto dto) =>
        {
            var user = await users.GetCurrentUserAsync(db, principal);
            if (user?.CleanerProfile is null) return Results.NotFound();
            if (user.CleanerProfile.ReviewStatus is not ("Rejected" or "NeedsInfo"))
                return Results.Conflict(new { message = "Chỉ gửi lại hồ sơ bị từ chối hoặc cần bổ sung." });
            if (string.IsNullOrWhiteSpace(dto.City) || dto.City.Length > 200 || dto.Bio?.Length > 2000 || dto.AddressText?.Length > 500)
                return Results.BadRequest(new { message = "Vui lòng nhập thành phố, giới thiệu tối đa 2000 ký tự và địa chỉ tối đa 500 ký tự." });
            var oldStatus = user.CleanerProfile.ReviewStatus;
            var recipients = await ManagementNotifications.RecipientsAsync(db,user.Id);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var count = await db.CleanerProfiles.Where(c => c.Id == user.CleanerProfile.Id && c.ReviewStatus == oldStatus)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReviewStatus, "Pending").SetProperty(c => c.ReviewReason, (string?)null)
                    .SetProperty(c => c.Bio, dto.Bio == null ? "" : dto.Bio.Trim()).SetProperty(c => c.City, dto.City.Trim())
                    .SetProperty(c => c.AddressText, dto.AddressText == null ? "" : dto.AddressText.Trim())
                    .SetProperty(c => c.ReviewedAt, (DateTime?)null).SetProperty(c => c.ReviewedById, (int?)null)
                    .SetProperty(c => c.SubmittedAt, (DateTime?)DateTime.UtcNow));
            if (count != 1) return Results.Conflict(new { message = "Hồ sơ đã thay đổi. Vui lòng tải lại." });
            ManagementNotifications.Add(db,recipients,$"ApplicationResubmitted:{user.CleanerProfile.Id}:{Guid.NewGuid():N}","Applications",user.CleanerProfile.Id,
                "Cleaner đã gửi lại hồ sơ",$"Hồ sơ HS{user.CleanerProfile.Id} đã bổ sung thông tin và chờ xét duyệt lại.");
            await db.SaveChangesAsync();await transaction.CommitAsync();
            db.ChangeTracker.Clear();
            return Results.Ok(users.MapUser((await users.GetCurrentUserAsync(db, principal))!));
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
            if (user.Role != UserRole.Cleaner || user.CleanerProfile.ReviewStatus != "Approved") return Results.Forbid();

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
