using System.Security.Claims;
using System.Text.Json;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class SupportConversationEndpoints
{
    public record SendDto(string Body, Guid RequestId);

    public static void MapConversation(this RouteGroupBuilder group, bool management)
    {
        var route = management ? "/support/{id:int}/messages" : "/{id:int}/messages";
        group.MapGet(route, async (int id, int? beforeId, AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var actor = users.GetCurrentUserId(principal)!.Value;
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && (management || t.ReporterId == actor));
            if (ticket is null) return Results.NotFound();
            if (beforeId is <= 0) return Results.BadRequest();
            var query = db.SupportMessages.AsNoTracking().Where(m => m.TicketId == id);
            if (beforeId.HasValue) query = query.Where(m => m.Id < beforeId.Value);
            var messages = await query.OrderByDescending(m => m.Id).Take(21)
                .Select(m => new { m.Id, m.Body, m.FromStaff, m.CreatedAt }).ToListAsync();
            var more = messages.Count > 20;
            var items = messages.Take(20).Reverse().ToArray();
            return Results.Ok(new { items, nextBeforeId = more ? (int?)items[0].Id : null, ticket.Status });
        });
        group.MapPost(route, async (int id, SendDto dto, AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var actor = users.GetCurrentUserId(principal)!.Value;
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && (management || t.ReporterId == actor));
            if (ticket is null) return Results.NotFound();
            if (management && ticket.ReporterId == actor) return Results.Forbid();
            if (dto.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(dto.Body) || dto.Body.Trim().Length > 2000)
                return Results.BadRequest(new { message = "Nhập nội dung từ 1 đến 2000 ký tự." });
            var body = dto.Body.Trim();
            async Task<IResult?> Replay()
            {
                var existing = await db.SupportMessages.AsNoTracking().SingleOrDefaultAsync(m => m.AuthorId == actor && m.RequestId == dto.RequestId);
                if (existing is null) return null;
                if (existing.TicketId != id || existing.Body != body || existing.FromStaff != management)
                    return Results.Conflict(new { message = "Mã gửi lại đã được dùng với nội dung khác." });
                var current = await db.SupportTickets.AsNoTracking().Where(t => t.Id == id).Select(t => new { t.Version,t.Status }).SingleAsync();
                return Results.Ok(new { existing.Id,current.Version,current.Status });
            }
            var replay = await Replay();
            if (replay is not null) return replay;
            if (management && ticket.Status == "Resolved")
                return Results.Conflict(new { message = "Mở lại yêu cầu trước khi gửi trao đổi mới." });
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var reopened = !management && ticket.Status == "Resolved";
            var count = await db.SupportTickets.Where(t => t.Id == id && t.Version == ticket.Version).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.Version,t => t.Version+1).SetProperty(t => t.UpdatedAt,now)
                    .SetProperty(t => t.Status,reopened ? "Open" : ticket.Status));
            if (count != 1)
            {
                await transaction.RollbackAsync();
                return await Replay() ?? Results.Conflict(new { message = "Yêu cầu vừa thay đổi. Vui lòng tải lại và thử gửi." });
            }
            var message = new SupportMessage { TicketId=id,AuthorId=actor,RequestId=dto.RequestId,Body=body,FromStaff=management,CreatedAt=now };
            db.SupportMessages.Add(message);
            try
            {
                await db.SaveChangesAsync();
                db.AdminAudits.Add(new AdminAudit { ActorId=actor,Action="ReplySupport",Target=$"Support:{id}",CreatedAt=now,
                    Details=JsonSerializer.Serialize(new { messageId=message.Id,fromStaff=management,bodyLength=body.Length,reopened }) });
                if (management) db.Notifications.Add(new Notification { UserId=ticket.ReporterId,BookingId=ticket.BookingId,Target="Support",
                    EventKey=$"SupportMessage:{message.Id}",Title="Có tin nhắn hỗ trợ mới",Message=$"Nhân viên đã trả lời HT{id} cho CM{ticket.BookingId}. Mở Hỗ trợ để đọc trao đổi.",CreatedAt=now });
                await db.SaveChangesAsync();await transaction.CommitAsync();
                return Results.Ok(new { message.Id,version=ticket.Version+1,status=reopened ? "Open" : ticket.Status });
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();db.ChangeTracker.Clear();
                replay = await Replay();if (replay is not null) return replay;
                throw;
            }
        }).RequireRateLimiting("support-reply");
    }
}
