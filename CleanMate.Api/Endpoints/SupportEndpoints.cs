using System.Security.Claims;
using System.Text.Json;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class SupportEndpoints
{
    public record CreateDto(int BookingId, string Category, string Description, Guid RequestId);
    public record RespondDto(string Status, string Response, int Version);
    private static bool ValidStatus(string? status) => status is null or "All" or "Open" or "InProgress" or "Resolved";
    private static bool Same(SupportTicket ticket, CreateDto dto) => ticket.BookingId == dto.BookingId &&
        ticket.Category == dto.Category && ticket.Description == dto.Description.Trim();

    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/support").RequireAuthorization().WithTags("Support");
        group.MapConversation(management:false);
        group.MapGet("/bookings", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var id = users.GetCurrentUserId(principal);
            return Results.Ok(await db.Bookings.AsNoTracking().Where(b => b.UserId == id || (b.Cleaner != null && b.Cleaner.UserId == id))
                .OrderByDescending(b => b.Id).Take(100).Select(b => new { b.Id, b.Status, b.StartTime, b.Address }).ToListAsync());
        });
        group.MapGet("/", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int? page) =>
        {
            var number = page ?? 1;
            if (number is < 1 or > 100000) return Results.BadRequest();
            var id = users.GetCurrentUserId(principal);
            var query = db.SupportTickets.AsNoTracking().Where(t => t.ReporterId == id);
            return Results.Ok(new { items = await query.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id).Skip((number-1)*20).Take(20)
                .Select(t => new { t.Id, t.BookingId, t.Category, t.Description, t.Status, t.Response, t.CreatedAt, t.UpdatedAt }).ToListAsync(),
                total = await query.CountAsync(), page = number, pageSize = 20 });
        });
        group.MapPost("/", async (CreateDto dto, AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            if (dto.RequestId == Guid.Empty || dto.BookingId <= 0 || dto.Category is not ("Scheduling" or "Quality" or "CashPayment" or "Other") ||
                string.IsNullOrWhiteSpace(dto.Description) || dto.Description.Trim().Length is < 10 or > 2000)
                return Results.BadRequest(new { message = "Chọn đơn, loại sự cố và mô tả từ 10 đến 2000 ký tự." });
            var id = users.GetCurrentUserId(principal)!.Value;
            if (!await db.Bookings.AnyAsync(b => b.Id == dto.BookingId && (b.UserId == id || (b.Cleaner != null && b.Cleaner.UserId == id))))
                return Results.NotFound(new { message = "Không tìm thấy đơn thuộc tài khoản của bạn." });
            var existing = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.ReporterId == id && t.RequestId == dto.RequestId);
            if (existing is not null) return Same(existing,dto) ? Results.Ok(new { existing.Id }) : Results.Conflict(new { message = "Mã gửi lại đã được dùng với nội dung khác." });
            var recipients = await ManagementNotifications.RecipientsAsync(db,id);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var ticket = new SupportTicket { BookingId = dto.BookingId, ReporterId = id, RequestId = dto.RequestId, Category = dto.Category, Description = dto.Description.Trim() };
            db.SupportTickets.Add(ticket);
            try
            {
                await db.SaveChangesAsync();
                ManagementNotifications.Add(db,recipients,$"ManagerSupportCreated:{ticket.Id}","Support",ticket.Id,
                    "Có yêu cầu hỗ trợ mới",$"Yêu cầu HT{ticket.Id} cho CM{dto.BookingId} cần được kiểm tra.",dto.BookingId);
                db.Notifications.Add(new Notification { UserId = id, BookingId = dto.BookingId, Target = "Support", EventKey = $"SupportCreated:{ticket.Id}",
                    Title = "Đã tiếp nhận yêu cầu hỗ trợ", Message = $"Yêu cầu HT{ticket.Id} cho đơn CM{dto.BookingId} đã được gửi tới nhân viên.", CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync(); await transaction.CommitAsync();
                return Results.Ok(new { ticket.Id });
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(); db.ChangeTracker.Clear();
                existing = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.ReporterId == id && t.RequestId == dto.RequestId);
                if (existing is null) throw;
                return Same(existing,dto) ? Results.Ok(new { existing.Id }) : Results.Conflict(new { message = "Mã gửi lại đã được dùng với nội dung khác." });
            }
        });
        return app;
    }

    public static void MapManagementSupport(RouteGroupBuilder group)
    {
        group.MapConversation(management:true);
        group.MapGet("/support", async (AppDbContext db, string? status, string? search, int? page) =>
        {
            var number = page ?? 1;
            if (!ValidStatus(status) || search?.Length > 100 || number is < 1 or > 100000) return Results.BadRequest();
            var query = db.SupportTickets.AsNoTracking().AsQueryable();
            if (status is not (null or "All")) query = query.Where(t => t.Status == status);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                var code = text.StartsWith("CM",StringComparison.OrdinalIgnoreCase) || text.StartsWith("HT",StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
                var id = int.TryParse(code,out var parsed) ? parsed : -1;
                query = query.Where(t => t.Id == id || t.BookingId == id || t.Reporter!.Email.Contains(text) || t.Reporter.FullName.Contains(text));
            }
            return Results.Ok(new { items = await query.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id).Skip((number-1)*20).Take(20)
                .Select(t => new { t.Id,t.BookingId,t.Category,t.Description,t.Status,t.Response,t.Version,t.CreatedAt,t.UpdatedAt,
                    reporterName = t.Reporter!.FullName, reporterEmail = t.Reporter.Email }).ToListAsync(),
                total = await query.CountAsync(), page = number, pageSize = 20 });
        });
        group.MapPut("/support/{id:int}", async (int id, RespondDto dto, AppDbContext db, HttpContext context) =>
        {
            if (dto.Status is not ("Open" or "InProgress" or "Resolved") || dto.Version < 1 || dto.Response?.Length > 2000 ||
                (dto.Status == "Resolved" && (string.IsNullOrWhiteSpace(dto.Response) || dto.Response.Trim().Length < 10)))
                return Results.BadRequest(new { message = "Chọn trạng thái hợp lệ. Khi giải quyết phải có phản hồi 10–2000 ký tự." });
            var actor = (User)context.Items["Manager"]!;
            var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
            if (ticket is null) return Results.NotFound();
            if (ticket.ReporterId == actor.Id) return Results.Forbid();
            var now = DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync();
            var count = await db.SupportTickets.Where(t => t.Id == id && t.Version == dto.Version).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.Status,dto.Status).SetProperty(t => t.Response,dto.Response == null ? "" : dto.Response.Trim())
                    .SetProperty(t => t.Version,t => t.Version+1).SetProperty(t => t.UpdatedAt,now).SetProperty(t => t.RespondedById,(int?)actor.Id));
            if (count != 1) return Results.Conflict(new { message = "Yêu cầu vừa được nhân viên khác cập nhật. Vui lòng tải lại." });
            var response = dto.Response?.Trim() ?? "";
            if (response.Length > 0 && response != ticket.Response)
                db.SupportMessages.Add(new SupportMessage { TicketId=id,AuthorId=actor.Id,RequestId=Guid.NewGuid(),FromStaff=true,Body=response,CreatedAt=now });
            db.AdminAudits.Add(new AdminAudit { ActorId = actor.Id, Action = "UpdateSupport", Target = $"Support:{id}", CreatedAt = now,
                Details = JsonSerializer.Serialize(new { before = ticket.Status, after = dto.Status, responseLength = dto.Response?.Trim().Length ?? 0 }) });
            db.Notifications.Add(new Notification { UserId = ticket.ReporterId, BookingId = ticket.BookingId, Target = "Support", EventKey = $"SupportUpdated:{id}:{dto.Version+1}",
                Title = "Yêu cầu hỗ trợ đã được cập nhật", Message = $"Nhân viên đã cập nhật HT{id} cho CM{ticket.BookingId}. Mở mục Hỗ trợ để xem trạng thái và phản hồi.", CreatedAt = now });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { id, version = dto.Version+1 });
        });
    }
}
