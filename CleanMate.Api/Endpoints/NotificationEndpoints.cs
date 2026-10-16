using System.Security.Claims;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class NotificationEndpoints
{
    public record ReadThroughDto(int ThroughId);

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();
        group.MapGet("/", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int? beforeId) =>
        {
            var userId = users.GetCurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();
            if (beforeId is <= 0) return Results.BadRequest(new { message = "Mốc phân trang không hợp lệ." });
            var rows = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId &&
                    (beforeId == null || n.Id < beforeId))
                .OrderByDescending(n => n.Id).Take(21)
                .Select(n => new { n.Id, n.BookingId, n.Title, n.Message, n.CreatedAt, n.ReadAt,
                    target = n.Target ?? (n.ForCleaner ? "CleanerJobs" : "Orders") }).ToListAsync();
            var items = rows.Take(20).ToList();
            return Results.Ok(new { items,
                unreadCount = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null),
                nextBeforeId = rows.Count > 20 ? (int?)items[^1].Id : null });
        });
        group.MapGet("/unread-count", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var userId = users.GetCurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();
            return Results.Ok(new { unreadCount = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null) });
        });
        group.MapPut("/{id:int}/read", async (int id, AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var userId = users.GetCurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();
            await db.Notifications.Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, (DateTime?)DateTime.UtcNow));
            var saved = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (saved is null) return Results.NotFound();
            return Results.Ok(new { saved.Id, saved.ReadAt,
                unreadCount = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null) });
        });
        group.MapPut("/read-all", async (ReadThroughDto dto, AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var userId = users.GetCurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();
            if (dto.ThroughId <= 0) return Results.BadRequest(new { message = "Mốc thông báo không hợp lệ." });
            var readAt = DateTime.UtcNow;
            await db.Notifications.Where(n => n.UserId == userId && n.Id <= dto.ThroughId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, (DateTime?)readAt));
            return Results.Ok(new { readAt, unreadCount = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null) });
        });
        return app;
    }
}
