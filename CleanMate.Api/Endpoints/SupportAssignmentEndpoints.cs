using System.Text.Json;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class SupportAssignmentEndpoints
{
    public record AssignDto(int? AssignedToId, int Version);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/support-assignees", async (AppDbContext db, HttpContext context) =>
        {
            var actor = (User)context.Items["Manager"]!;
            if (actor.Role != UserRole.Admin) return Results.Forbid();
            return Results.Ok(await db.Users.AsNoTracking().Where(u => u.IsActive && (u.Role == UserRole.Admin || u.Role == UserRole.Staff))
                .OrderBy(u => u.FullName).ThenBy(u => u.Id).Select(u => new { u.Id, name = u.FullName }).ToListAsync());
        });
        group.MapPut("/support/{id:int}/assignment", async (int id, AssignDto dto, AppDbContext db, HttpContext context) =>
        {
            if (dto.Version < 1 || dto.AssignedToId is <= 0) return Results.BadRequest();
            var actor = (User)context.Items["Manager"]!;
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (ticket.ReporterId == actor.Id) return Results.Forbid();
            // Assignment coordinates responsibility; all managers retain access to help with the conversation.
            if (actor.Role != UserRole.Admin && (dto.AssignedToId is not null && dto.AssignedToId != actor.Id ||
                ticket.AssignedToId is not null && ticket.AssignedToId != actor.Id)) return Results.Forbid();
            if (dto.AssignedToId == ticket.ReporterId) return Results.BadRequest(new { message = "Không giao yêu cầu cho chính người báo." });
            if (dto.AssignedToId is int assignee && !await db.Users.AnyAsync(u => u.Id == assignee && u.IsActive && (u.Role == UserRole.Admin || u.Role == UserRole.Staff)))
                return Results.BadRequest(new { message = "Người phụ trách phải là Admin/Staff đang hoạt động. Vui lòng tải lại danh sách." });
            if (ticket.Version != dto.Version) return Conflict();
            if (ticket.AssignedToId == dto.AssignedToId) return Results.Ok(new { id, version = ticket.Version });
            var now = DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync();
            var count = await db.SupportTickets.Where(t => t.Id == id && t.Version == dto.Version).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.AssignedToId, dto.AssignedToId).SetProperty(t => t.AssignedAt, dto.AssignedToId == null ? (DateTime?)null : now)
                    .SetProperty(t => t.Version, t => t.Version + 1).SetProperty(t => t.UpdatedAt, now));
            if (count != 1) return Conflict();
            db.AdminAudits.Add(new AdminAudit { ActorId = actor.Id, Action = "AssignSupport", Target = $"Support:{id}", CreatedAt = now,
                Details = JsonSerializer.Serialize(new { before = ticket.AssignedToId, after = dto.AssignedToId, version = dto.Version + 1 }) });
            if (dto.AssignedToId is int recipient && recipient != actor.Id)
                ManagementNotifications.Add(db, [recipient], $"SupportAssigned:{id}:{dto.Version + 1}", "Support", id,
                    "Bạn được giao yêu cầu hỗ trợ", $"Bạn phụ trách HT{id} cho CM{ticket.BookingId}. Mở yêu cầu để xem chi tiết.", ticket.BookingId);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { id, version = dto.Version + 1 });
        });
    }

    private static IResult Conflict() => Results.Conflict(new { message = "Yêu cầu vừa thay đổi. Vui lòng tải lại trước khi phân công." });
}
