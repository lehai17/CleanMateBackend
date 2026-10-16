using System.Net.Mail;
using System.Text.Json;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class ManagementEndpoints
{
    public record ReviewDto(string Status, string? Reason);
    public record ServiceDto(string Name, decimal HourlyRate, int MinHours, int MaxHours, bool IsActive, int Version);
    public record StaffDto(string FullName, string Email, string Password);
    public record StaffAccessDto(bool IsActive, bool ExpectedActive);
    private static User Actor(HttpContext context) => (User)context.Items["Manager"]!;
    private static void Audit(AppDbContext db, User actor, string action, string target, object details) =>
        db.AdminAudits.Add(new AdminAudit { ActorId = actor.Id, Action = action, Target = target,
            Details = JsonSerializer.Serialize(details), CreatedAt = DateTime.UtcNow });

    public static IEndpointRouteBuilder MapManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/services", async (AppDbContext db) => Results.Ok(await db.ServiceOfferings.AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Name, s.HourlyRate, s.MinHours, s.MaxHours, s.Version }).ToListAsync()));
        var group = app.MapGroup("/api/management").RequireAuthorization().WithTags("Management");
        group.AddEndpointFilter(async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var users = context.HttpContext.RequestServices.GetRequiredService<CurrentUserService>();
            var user = await users.GetCurrentUserAsync(db, context.HttpContext.User);
            if (user is null || !user.IsActive || user.Role is not (UserRole.Admin or UserRole.Staff)) return Results.Forbid();
            context.HttpContext.Items["Manager"] = user;
            return await next(context);
        });
        SupportEndpoints.MapManagementSupport(group);
        group.MapGet("/dashboard", async (AppDbContext db) =>
        {
            var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).Date;
            var from = new DateTimeOffset(today, TimeSpan.FromHours(7)).UtcDateTime;
            return Results.Ok(new {
                totalOrders = await db.Bookings.CountAsync(),
                completedOrders = await db.Bookings.CountAsync(b => b.Status == BookingStatuses.Completed),
                pendingOrders = await db.Bookings.CountAsync(b => b.Status == BookingStatuses.Pending),
                todayOrders = await db.Bookings.CountAsync(b => b.StartTime >= from && b.StartTime < from.AddDays(1)),
                pendingApplications = await db.CleanerProfiles.CountAsync(c => c.ReviewStatus == "Pending"),
                approvedCleaners = await db.CleanerProfiles.CountAsync(c => c.ReviewStatus == "Approved" && c.User!.IsActive),
                customers = await db.Users.CountAsync(u => u.Role == UserRole.Customer),
                completedServiceValue = await db.Bookings.Where(b => b.Status == BookingStatuses.Completed).SumAsync(b => (decimal?)b.Price) ?? 0,
                byStatus = await db.Bookings.GroupBy(b => b.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync()
            });
        });
        group.MapGet("/applications", async (AppDbContext db, string? status, string? search, int? page) =>
        {
            var number = page ?? 1;
            if (number < 1 || number > 100000 || search?.Length > 100) return Results.BadRequest();
            if (status is not (null or "All" or "Pending" or "Approved" or "Rejected" or "NeedsInfo")) return Results.BadRequest();
            var query = db.CleanerProfiles.AsNoTracking().AsQueryable();
            if (status != "All") query = query.Where(c => c.ReviewStatus == (status ?? "Pending"));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var text=search.Trim();
                if (text.StartsWith("HS",StringComparison.OrdinalIgnoreCase) && int.TryParse(text[2..],out var profileId) && profileId > 0)
                    query=query.Where(c=>c.Id==profileId);
                else query=query.Where(c=>c.User!.FullName.Contains(text)||c.User.Email.Contains(text)||c.City.Contains(text));
            }
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(c => c.SubmittedAt).ThenByDescending(c => c.Id).Skip((number - 1) * 20).Take(20)
                .Select(c => new { c.Id, c.UserId, name = c.User!.FullName, email = c.User.Email, phone = c.User.PhoneNumber,
                    c.City, c.AddressText, c.Bio, c.ReviewStatus, c.ReviewReason, c.SubmittedAt, c.ReviewedAt }).ToListAsync();
            return Results.Ok(new { items, total, page = number, pageSize = 20 });
        });
        group.MapPut("/applications/{id:int}/review", async (int id, ReviewDto dto, AppDbContext db, HttpContext context) =>
        {
            if (dto.Status is not ("Approved" or "Rejected" or "NeedsInfo") || dto.Reason?.Length > 1000 ||
                (dto.Status != "Approved" && string.IsNullOrWhiteSpace(dto.Reason)))
                return Results.BadRequest(new { message = "Chọn kết quả hợp lệ; từ chối hoặc yêu cầu bổ sung phải có lý do (tối đa 1000 ký tự)." });
            var profile = await db.CleanerProfiles.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
            if (profile is null) return Results.NotFound();
            var actor = Actor(context);
            if (profile.UserId == actor.Id) return Results.Forbid();
            var reason = dto.Reason?.Trim();
            var shortReason = reason?.Length > 800 ? reason[..800] + "…" : reason;
            await using var transaction = await db.Database.BeginTransactionAsync();
            var count = await db.CleanerProfiles.Where(c => c.Id == id && c.ReviewStatus == "Pending")
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReviewStatus, dto.Status).SetProperty(c => c.ReviewReason, dto.Reason == null ? null : dto.Reason.Trim())
                    .SetProperty(c => c.ReviewedAt, (DateTime?)DateTime.UtcNow).SetProperty(c => c.ReviewedById, (int?)actor.Id));
            if (count != 1) return Results.Conflict(new { message = "Hồ sơ không còn chờ duyệt. Vui lòng tải lại." });
            if (dto.Status == "Approved")
            {
                var promoted = await db.Users.Where(u => u.Id == profile.UserId && u.IsActive && u.Role == UserRole.Customer)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.Cleaner));
                if (promoted != 1) return Results.Conflict(new { message = "Tài khoản không còn đủ điều kiện duyệt." });
            }
            db.Notifications.Add(new Notification { UserId = profile.UserId, Target = "Profile",
                EventKey = $"CleanerReview:{id}:{Guid.NewGuid():N}", Title = dto.Status == "Approved" ? "Hồ sơ cleaner đã được duyệt" : "Cập nhật hồ sơ cleaner",
                Message = dto.Status == "Approved" ? "Bạn đã được phép nhận ca làm. Mở hồ sơ để cập nhật quyền mới." : $"Kết quả: {(dto.Status == "NeedsInfo" ? "Cần bổ sung" : "Từ chối")}. {shortReason}", CreatedAt = DateTime.UtcNow });
            Audit(db, actor, "ReviewCleaner", $"Cleaner:{id}", new { dto.Status, reason = dto.Reason?.Trim() });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { id, status = dto.Status });
        });
        group.MapGet("/services", async (AppDbContext db) => Results.Ok(await db.ServiceOfferings.AsNoTracking().OrderBy(s => s.Key).ToListAsync()));
        group.MapPut("/services/{key}", async (string key, ServiceDto dto, AppDbContext db, HttpContext context) =>
        {
            var actor = Actor(context);
            if (actor.Role != UserRole.Admin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 200 || dto.HourlyRate is < 1000 or > 10000000 ||
                dto.HourlyRate != decimal.Truncate(dto.HourlyRate) || dto.MinHours < 1 || dto.MaxHours > 8 || dto.MaxHours < dto.MinHours)
                return Results.BadRequest(new { message = "Tên tối đa 200 ký tự, giá nguyên từ 1.000 đến 10.000.000đ/giờ, thời lượng từ 1 đến 8 giờ." });
            var old = await db.ServiceOfferings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key);
            if (old is null) return Results.NotFound();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var count = await db.ServiceOfferings.Where(s => s.Key == key && s.Version == dto.Version)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, dto.Name.Trim()).SetProperty(x => x.HourlyRate, dto.HourlyRate)
                    .SetProperty(x => x.MinHours, dto.MinHours).SetProperty(x => x.MaxHours, dto.MaxHours).SetProperty(x => x.IsActive, dto.IsActive)
                    .SetProperty(x => x.Version, x => x.Version + 1).SetProperty(x => x.UpdatedAt, (DateTime?)DateTime.UtcNow));
            if (count != 1) return Results.Conflict(new { message = "Cấu hình đã được sửa ở nơi khác. Vui lòng tải lại." });
            Audit(db, actor, "UpdateService", key, new { before = old, after = dto });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { version = dto.Version + 1 });
        });
        group.MapGet("/orders", async (AppDbContext db, string? status, string? search, int? page) =>
        {
            var number = page ?? 1;
            if (number < 1 || number > 100000 || search?.Length > 100) return Results.BadRequest();
            if (status is not (null or "All" or "Pending" or "Accepted" or "InProgress" or "Completed" or "Cancelled")) return Results.BadRequest();
            var query = db.Bookings.AsNoTracking().AsQueryable();
            if (status is not (null or "All")) query = query.Where(b => b.Status == status);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                var id = int.TryParse(text.StartsWith("CM", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, out var parsed) ? parsed : -1;
                query = query.Where(b => b.Id == id || b.User!.FullName.Contains(text) || b.Address.Contains(text) ||
                    (b.ContactPhone != null && b.ContactPhone.Contains(text)));
            }
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(b => b.Id).Skip((number - 1) * 20).Take(20).Select(b => new {
                b.Id, b.StartTime, b.DurationHours, b.Price, b.Status, b.Address, b.ContactName, b.ContactPhone,
                customerName = b.User!.FullName, customerEmail = b.User.Email,
                cleanerName = b.Cleaner == null ? null : b.Cleaner.User!.FullName,
                b.StartedAt, b.CompletedAt, b.CustomerRating,
                paymentStatus = b.Payment == null ? "Unpaid" : b.Payment.Status }).ToListAsync();
            return Results.Ok(new { items, total, page = number, pageSize = 20 });
        });
        group.MapGet("/staff", async (AppDbContext db, HttpContext context) => Actor(context).Role != UserRole.Admin ? Results.Forbid() :
            Results.Ok(await db.Users.Where(u => u.Role == UserRole.Staff).OrderBy(u => u.Id)
                .Select(u => new { u.Id, u.FullName, u.Email, u.IsActive }).ToListAsync()));
        group.MapPost("/staff", async (StaffDto dto, AppDbContext db, HttpContext context) =>
        {
            var actor = Actor(context);
            if (actor.Role != UserRole.Admin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(dto.FullName) || dto.FullName.Trim().Length > 200 ||
                dto.Email?.Length > 254 || !MailAddress.TryCreate(dto.Email?.Trim(), out var email) || email.Address != dto.Email?.Trim() ||
                string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 12 || System.Text.Encoding.UTF8.GetByteCount(dto.Password) > 72)
                return Results.BadRequest(new { message = "Nhập họ tên, email hợp lệ và mật khẩu ít nhất 12 ký tự, tối đa 72 byte UTF-8." });
            var normalized = email.Address.ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == normalized)) return Results.Conflict(new { message = "Email đã được sử dụng." });
            var user = new User { FullName = dto.FullName.Trim(), Email = normalized, Role = UserRole.Staff, PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password) };
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Users.Add(user);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return Results.Conflict(new { message = "Không thể tạo tài khoản. Email có thể vừa được sử dụng." }); }
            Audit(db, actor, "CreateStaff", $"User:{user.Id}", new { user.Email });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { user.Id, user.FullName, user.Email, user.IsActive });
        });
        group.MapPut("/staff/{id:int}/access", async (int id, StaffAccessDto dto, AppDbContext db, HttpContext context) =>
        {
            var actor = Actor(context);
            if (actor.Role != UserRole.Admin) return Results.Forbid();
            if (dto.IsActive == dto.ExpectedActive)
                return await db.Users.AnyAsync(u => u.Id == id && u.Role == UserRole.Staff && u.IsActive == dto.ExpectedActive)
                    ? Results.Ok(new { id, dto.IsActive })
                    : Results.Conflict(new { message = "Tài khoản không còn khớp trạng thái. Vui lòng tải lại." });
            await using var transaction = await db.Database.BeginTransactionAsync();
            var count = await db.Users.Where(u => u.Id == id && u.Role == UserRole.Staff && u.IsActive == dto.ExpectedActive)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, dto.IsActive)
                    .SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1));
            if (count != 1) return Results.Conflict(new { message = "Tài khoản không còn khớp trạng thái. Vui lòng tải lại." });
            Audit(db, actor, "StaffAccess", $"User:{id}", new { dto.IsActive, sessionsRevoked = true });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { id, dto.IsActive });
        });
        group.MapGet("/audit", async (AppDbContext db, HttpContext context, int? beforeId) =>
        {
            if (beforeId is <= 0) return Results.BadRequest();
            var actor = Actor(context);
            return Results.Ok(await db.AdminAudits.AsNoTracking().Where(a => (actor.Role == UserRole.Admin || a.ActorId == actor.Id) &&
                    (beforeId == null || a.Id < beforeId)).OrderByDescending(a => a.Id).Take(50)
                .Select(a => new { a.Id, a.Action, a.Target, a.Details, a.CreatedAt, actorName = db.Users.Where(u => u.Id == a.ActorId).Select(u => u.FullName).First() }).ToListAsync());
        });
        return app;
    }
}
