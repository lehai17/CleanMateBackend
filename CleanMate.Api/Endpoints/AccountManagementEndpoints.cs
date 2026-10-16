using System.Text.Json;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class AccountManagementEndpoints
{
    public record AccessDto(bool IsActive, bool ExpectedActive, string? Reason);

    public static void MapAccountManagement(this RouteGroupBuilder management)
    {
        var group = management.MapGroup("/accounts");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return context.HttpContext.Items["Manager"] is User { Role: UserRole.Admin }
                ? await next(context) : Results.Forbid();
        });

        group.MapGet("", async (AppDbContext db, string? role, string? status, string? search, int? page) =>
        {
            var number = page ?? 1;
            if (number is < 1 or > 100000 || search?.Length > 100 ||
                role is not (null or "All" or "Customer" or "Cleaner") ||
                status is not (null or "All" or "Active" or "Locked")) return Results.BadRequest();
            var query = db.Users.AsNoTracking().Where(u => u.Role == UserRole.Customer || u.Role == UserRole.Cleaner);
            if (role is "Customer") query = query.Where(u => u.Role == UserRole.Customer);
            if (role is "Cleaner") query = query.Where(u => u.Role == UserRole.Cleaner);
            if (status is "Active") query = query.Where(u => u.IsActive);
            if (status is "Locked") query = query.Where(u => !u.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                var id = int.TryParse(text.TrimStart('#'), out var parsed) ? parsed : -1;
                query = query.Where(u => u.Id == id || u.FullName.Contains(text) || u.Email.Contains(text) ||
                    (u.PhoneNumber != null && u.PhoneNumber.Contains(text)));
            }
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(u => u.Id).Skip((number - 1) * 20).Take(20)
                .Select(u => new
                {
                    u.Id, u.FullName, u.Email, u.PhoneNumber, role = u.Role.ToString(), u.IsActive,
                    activeBookings = db.Bookings.Count(b =>
                        (b.UserId == u.Id || (b.Cleaner != null && b.Cleaner.UserId == u.Id)) &&
                        (b.Status == BookingStatuses.Accepted || b.Status == BookingStatuses.InProgress)),
                    pendingBookings = db.Bookings.Count(b => b.UserId == u.Id && b.Status == BookingStatuses.Pending)
                }).ToListAsync();
            return Results.Ok(new { items, total, page = number, pageSize = 20 });
        });

        group.MapPut("/{id:int}/access", async (int id, AccessDto dto, AppDbContext db, HttpContext context) =>
        {
            var reason = dto.Reason?.Trim();
            if (reason is null || reason.Length is < 10 or > 1000)
                return Results.BadRequest(new { message = "Lý do thay đổi truy cập cần từ 10 đến 1000 ký tự." });
            var target = await db.Users.AsNoTracking().Where(u => u.Id == id &&
                (u.Role == UserRole.Customer || u.Role == UserRole.Cleaner))
                .Select(u => new { u.Id, u.Role, u.IsActive }).SingleOrDefaultAsync();
            if (target is null) return Results.NotFound();
            if (target.IsActive != dto.ExpectedActive)
                return Results.Conflict(new { message = "Tài khoản không còn khớp trạng thái. Vui lòng tải lại." });
            if (dto.IsActive == target.IsActive) return Results.Ok(new { id, dto.IsActive });

            await using var transaction = await db.Database.BeginTransactionAsync();
            var changed = await db.Users.Where(u => u.Id == id && u.Role == target.Role && u.IsActive == dto.ExpectedActive)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, dto.IsActive)
                    .SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1));
            if (changed != 1) return Results.Conflict(new { message = "Tài khoản không còn khớp trạng thái. Vui lòng tải lại." });
            var actor = (User)context.Items["Manager"]!;
            db.AdminAudits.Add(new AdminAudit
            {
                ActorId = actor.Id, Action = "AccountAccess", Target = $"User:{id}", CreatedAt = DateTime.UtcNow,
                Details = JsonSerializer.Serialize(new { dto.IsActive, role = target.Role.ToString(), reason, sessionsRevoked = true })
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { id, dto.IsActive });
        });
    }
}
