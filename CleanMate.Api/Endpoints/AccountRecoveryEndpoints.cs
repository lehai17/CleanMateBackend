using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class AccountRecoveryEndpoints
{
    public record RequestDto(string Email);
    public record ResetDto(string Email, string Code, string NewPassword);
    public record VerifyDto(string Email, string Code);
    private const string Accepted = "Nếu tài khoản phù hợp, mã sẽ được gửi đến email đã đăng ký. Kiểm tra cả thư rác; chỉ yêu cầu lại sau 2 phút.";
    private static IResult Invalid() => Results.BadRequest(new { message = "Mã không hợp lệ, đã dùng hoặc đã hết hạn. Vui lòng yêu cầu mã mới." });
    private static string? Normalize(string? value) => value?.Trim().ToLowerInvariant();
    private static bool EmailValid(string? value) => value is { Length: > 0 and <= 254 } && MailAddress.TryCreate(value, out var address) && address.Address == value;
    private static bool CodeValid(string? value) => value?.Trim() is { Length: 64 } code && code.All(Uri.IsHexDigit);
    private static IResult Queue(AccountEmailQueue queue, IAccountEmail sender, string email, string purpose)
    {
        if (!sender.IsConfigured || !queue.TryAdd(email, purpose))
            return Results.Json(new { message = "Gửi email hiện chưa sẵn sàng. Vui lòng thử lại sau hoặc liên hệ hỗ trợ." }, statusCode: 503);
        return Results.Json(new { message = Accepted }, statusCode: 202);
    }

    public static IEndpointRouteBuilder MapAccountRecovery(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Account recovery");
        group.AddEndpointFilter(async (context, next) => {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapPost("/forgot-password", (RequestDto dto, AccountEmailQueue queue, IAccountEmail sender) => {
            var email = Normalize(dto.Email);
            return !EmailValid(email) ? Results.BadRequest(new { message = "Vui lòng nhập email hợp lệ." }) : Queue(queue, sender, email!, "ResetPassword");
        }).RequireRateLimiting("auth-recovery-request");
        group.MapPost("/request-email-verification", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, AccountEmailQueue queue, IAccountEmail sender) => {
            var user = await users.GetCurrentUserAsync(db, principal);
            if (user is null) return Results.Unauthorized();
            if (user.EmailVerifiedAt is not null) return Results.Ok(new { message = "Email đã được xác minh." });
            return Queue(queue, sender, user.Email, "VerifyEmail");
        }).RequireAuthorization().RequireRateLimiting("auth-recovery-request");
        group.MapPost("/reset-password", async (ResetDto dto, AppDbContext db, AccountEmailQueue queue) => {
            var email = Normalize(dto.Email);
            if (!EmailValid(email) || !CodeValid(dto.Code)) return Invalid();
            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 12 || Encoding.UTF8.GetByteCount(dto.NewPassword) > 72)
                return Results.BadRequest(new { message = "Mật khẩu mới cần ít nhất 12 ký tự và tối đa 72 byte UTF-8." });
            var now = DateTime.UtcNow;
            var hash = AccountEmailWorker.Hash(dto.Code);
            var code = await db.AccountCodes.AsNoTracking().SingleOrDefaultAsync(c => c.Hash == hash && c.Purpose == "ResetPassword" &&
                c.DeliveredAt != null && c.ConsumedAt == null && c.ExpiresAt > now);
            if (code is null) return Invalid();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == code.UserId && u.Email == email && u.IsActive && u.TokenVersion == code.TokenVersion);
            if (user is null || string.IsNullOrEmpty(user.PasswordHash)) return Invalid();
            if (BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.PasswordHash))
                return Results.BadRequest(new { message = "Chọn mật khẩu mới khác mật khẩu hiện tại." });
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var changed = await db.Users.Where(u => u.Id == user.Id && u.Email == email && u.IsActive && u.TokenVersion == code.TokenVersion && u.PasswordHash == user.PasswordHash)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, passwordHash).SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1));
            if (changed != 1) return Invalid();
            var consumed = await db.AccountCodes.Where(c => c.Id == code.Id && c.ConsumedAt == null && c.DeliveredAt != null && c.ExpiresAt > DateTime.UtcNow)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, (DateTime?)now));
            if (consumed != 1) return Invalid();
            db.AdminAudits.Add(new AdminAudit { ActorId = user.Id, Action = "ResetPassword", Target = $"User:{user.Id}", CreatedAt = now,
                Details = "Password reset through email; previous sessions revoked." });
            db.Notifications.Add(new Notification { UserId = user.Id, Target = "Profile", EventKey = $"ResetPassword:{code.Id}", CreatedAt = now,
                Title = "Mật khẩu đã được đặt lại", Message = "Mật khẩu vừa được khôi phục bằng mã email. Các phiên đăng nhập cũ đã hết hiệu lực." });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            queue.TryAdd(user.Email, "PasswordChanged");
            return Results.Ok(new { message = "Đã đặt lại mật khẩu và thu hồi phiên cũ. Vui lòng đăng nhập bằng mật khẩu mới." });
        }).RequireRateLimiting("auth-recovery-consume");
        group.MapPost("/verify-email", async (VerifyDto dto, AppDbContext db) => {
            var email = Normalize(dto.Email);
            if (!EmailValid(email) || !CodeValid(dto.Code)) return Invalid();
            var now = DateTime.UtcNow;
            var hash = AccountEmailWorker.Hash(dto.Code);
            var code = await db.AccountCodes.AsNoTracking().SingleOrDefaultAsync(c => c.Hash == hash && c.Purpose == "VerifyEmail" &&
                c.DeliveredAt != null && c.ConsumedAt == null && c.ExpiresAt > now);
            if (code is null) return Invalid();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var changed = await db.Users.Where(u => u.Id == code.UserId && u.Email == email && u.IsActive && u.TokenVersion == code.TokenVersion && u.EmailVerifiedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerifiedAt, (DateTime?)now));
            if (changed != 1) return Invalid();
            var consumed = await db.AccountCodes.Where(c => c.Id == code.Id && c.ConsumedAt == null && c.DeliveredAt != null && c.ExpiresAt > DateTime.UtcNow)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, (DateTime?)now));
            if (consumed != 1) return Invalid();
            db.AdminAudits.Add(new AdminAudit { ActorId = code.UserId, Action = "VerifyEmail", Target = $"User:{code.UserId}", Details = "Email ownership verified through a single-use code.", CreatedAt = now });
            db.Notifications.Add(new Notification { UserId = code.UserId, Target = "Profile", EventKey = $"VerifyEmail:{code.Id}", CreatedAt = now,
                Title = "Email đã được xác minh", Message = "Email đăng ký của bạn đã được xác minh thành công." });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { message = "Đã xác minh email. Bạn có thể tải lại hồ sơ để xem trạng thái." });
        }).RequireRateLimiting("auth-recovery-consume");
        return app;
    }
}
