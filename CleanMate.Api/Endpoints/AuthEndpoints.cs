using CleanMate.Api.Contracts;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using CleanMate.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace CleanMate.Api.Endpoints;

public static class AuthEndpoints
{
    public record ChangePasswordDto(string CurrentPassword, string NewPassword);
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/logout-all", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var user = await users.GetCurrentUserAsync(db,principal);
            if (user is null) return Results.Unauthorized();
            var claim = principal.FindFirst("token_version")?.Value;
            var expected = claim is null ? 0 : int.Parse(claim,System.Globalization.CultureInfo.InvariantCulture);
            if(user.TokenVersion != expected)return Results.Unauthorized();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var changed = await db.Users.Where(u=>u.Id==user.Id&&u.IsActive&&u.TokenVersion==expected)
                .ExecuteUpdateAsync(s=>s.SetProperty(u=>u.TokenVersion,u=>u.TokenVersion+1));
            if(changed!=1)return Results.Conflict(new {message="Phiên đăng nhập đã thay đổi. Vui lòng đăng nhập lại."});
            db.AdminAudits.Add(new AdminAudit {ActorId=user.Id,Action="LogoutAll",Target=$"User:{user.Id}",Details="All previous login sessions revoked.",CreatedAt=DateTime.UtcNow});
            db.Notifications.Add(new Notification {UserId=user.Id,Target="Profile",EventKey=$"LogoutAll:{user.Id}:{expected+1}",Title="Đã đăng xuất tất cả thiết bị",Message="Các phiên đăng nhập CleanMate trước đó đã hết hiệu lực, gồm cả thiết bị vừa thao tác.",CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync();await transaction.CommitAsync();
            return Results.Ok(new {message="Đã đăng xuất tất cả thiết bị. Vui lòng đăng nhập lại."});
        }).RequireAuthorization().RequireRateLimiting("password-change");
        group.MapPost("/change-password", async (ChangePasswordDto dto, AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            if (string.IsNullOrEmpty(dto.CurrentPassword) || dto.CurrentPassword.Length > 128 ||
                string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 12)
                return Results.BadRequest(new { message = "Nhập mật khẩu hiện tại và mật khẩu mới ít nhất 12 ký tự." });
            // BCrypt has a 72-byte input limit; do not silently truncate a new password.
            if (Encoding.UTF8.GetByteCount(dto.NewPassword) > 72)
                return Results.BadRequest(new { message = "Mật khẩu mới quá dài, vui lòng rút ngắn." });
            var user = await users.GetCurrentUserAsync(db,principal);
            if (user is null) return Results.Unauthorized();
            var versionClaim = principal.FindFirst("token_version")?.Value;
            var expected = versionClaim is null ? 0 : int.Parse(versionClaim,System.Globalization.CultureInfo.InvariantCulture);
            if (user.TokenVersion != expected) return Results.Unauthorized();
            if (string.IsNullOrEmpty(user.PasswordHash))
                return Results.BadRequest(new { message = "Tài khoản Google chưa có mật khẩu riêng. Vui lòng quản lý đăng nhập qua Google." });
            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword,user.PasswordHash))
                return Results.BadRequest(new { message = "Mật khẩu hiện tại không đúng." });
            if (BCrypt.Net.BCrypt.Verify(dto.NewPassword,user.PasswordHash))
                return Results.BadRequest(new { message = "Mật khẩu mới phải khác mật khẩu hiện tại." });
            var hash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var changed = await db.Users.Where(u => u.Id == user.Id && u.IsActive && u.TokenVersion == expected && u.PasswordHash == user.PasswordHash)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash,hash).SetProperty(u => u.TokenVersion,u => u.TokenVersion+1));
            if (changed != 1) return Results.Conflict(new { message = "Tài khoản đã thay đổi. Đăng nhập lại trước khi đổi mật khẩu." });
            db.AdminAudits.Add(new AdminAudit { ActorId = user.Id,Action = "ChangePassword",Target = $"User:{user.Id}",Details = "Password changed; previous sessions revoked.",CreatedAt = DateTime.UtcNow });
            db.Notifications.Add(new Notification { UserId=user.Id,Target="Profile",EventKey=$"PasswordChanged:{user.Id}:{expected+1}",Title="Mật khẩu đã được thay đổi",Message="Tất cả phiên đăng nhập cũ đã hết hiệu lực. Hãy đăng nhập bằng mật khẩu mới.",CreatedAt=DateTime.UtcNow });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return Results.Ok(new { message = "Đã đổi mật khẩu. Vui lòng đăng nhập lại trên các thiết bị." });
        }).RequireAuthorization().RequireRateLimiting("password-change");

        group.MapPost("/register", async (AppDbContext db, AuthService authService, RegisterDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.FullName) || dto.FullName.Length > 200 || string.IsNullOrWhiteSpace(dto.Email) || dto.Email.Length > 254
                || !System.Net.Mail.MailAddress.TryCreate(dto.Email.Trim(), out var address)
                || address.Address != dto.Email.Trim() || string.IsNullOrWhiteSpace(dto.Password)
                || dto.Password.Length < 8 || !Enum.IsDefined(dto.Role))
            {
                return Results.BadRequest(new { message = "Vui lòng nhập họ tên, email hợp lệ và mật khẩu ít nhất 8 ký tự." });
            }

            if (Encoding.UTF8.GetByteCount(dto.Password) > 72)
                return Results.BadRequest(new { message = "Mật khẩu quá dài. Vui lòng dùng tối đa 72 byte UTF-8 (ký tự có dấu có thể chiếm nhiều byte)." });

            var email = dto.Email.Trim().ToLowerInvariant();
            if (await authService.EmailExistsAsync(db, email))
            {
                return Results.BadRequest("Email already used");
            }

            var result = await authService.RegisterAsync(db, dto);
            return Results.Ok(result);
        }).RequireRateLimiting("auth-register");

        group.MapPost("/login", async (AppDbContext db, AuthService authService, LoginDto dto) =>
        {
            var result = await authService.LoginAsync(db, dto);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireRateLimiting("auth-login");

        group.MapPost("/google", async (AppDbContext db, AuthService authService, GoogleLoginDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.IdToken) || dto.IdToken.Length > 16384)
            {
                return Results.BadRequest("Missing idToken");
            }

            var result = await authService.LoginWithGoogleAsync(db, dto);
            return result is null
                ? Results.BadRequest("Invalid Google token")
                : Results.Ok(result);
        }).RequireRateLimiting("auth-google");

        return app;
    }
}
