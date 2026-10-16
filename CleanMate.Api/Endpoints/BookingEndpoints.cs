using CleanMate.Api.Contracts;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace CleanMate.Api.Endpoints;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var bookings = app.MapGroup("/api/bookings").WithTags("Bookings").RequireAuthorization();
        bookings.MapPost("/", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, CreateBookingDto dto) =>
        {
            var user = await users.GetCurrentUserAsync(db, principal);
            if (user is null) return Results.Unauthorized();
            if (dto.RequestId == Guid.Empty)
                return Results.BadRequest(new { message = "Mã yêu cầu đặt lịch không hợp lệ." });
            var requestHash = dto.RequestId is null ? null : Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(new { dto.ServiceKey, start = dto.StartTime.UtcTicks,
                    dto.DurationHours, address = dto.Address?.Trim(), notes = dto.Notes?.Trim() ?? "",
                    dto.PaymentMethod, contactName = dto.ContactName?.Trim(), contactPhone = dto.ContactPhone?.Trim() })));
            if (dto.RequestId is not null)
            {
                var existing = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b =>
                    b.UserId == user.Id && b.RequestId == dto.RequestId);
                if (existing is not null) return Replay(existing, requestHash!);
            }
            // Preserve the booking contact rather than reading a mutable profile later.
            // Older clients can omit these fields and use their current profile once.
            dto = dto with { ContactName = dto.ContactName ?? user.FullName,
                ContactPhone = dto.ContactPhone ?? user.PhoneNumber };
            var error = BookingRules.Validate(dto, DateTimeOffset.UtcNow);
            if (error is not null) return Results.BadRequest(new { message = error });
            var booking = new Booking
            {
                UserId = user.Id, RequestId = dto.RequestId, RequestHash = requestHash,
                StartTime = dto.StartTime.UtcDateTime, DurationHours = dto.DurationHours,
                Price = BookingRules.HourlyRates[dto.ServiceKey] * dto.DurationHours,
                Address = dto.Address!.Trim(), Notes = $"[{dto.ServiceKey}] {dto.Notes?.Trim()}".Trim(),
                ContactName = dto.ContactName?.Trim(), ContactPhone = dto.ContactPhone?.Trim(),
                PaymentMethod = "cash", CreatedAt = DateTime.UtcNow, Status = BookingStatuses.Pending
            };
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Bookings.Add(booking);
            try
            {
                await db.SaveChangesAsync();
                BookingNotifications.Add(db, user.Id, booking.Id, $"Created:{booking.Id}", "Đặt lịch thành công", $"Đơn CM{booking.Id} đang chờ cleaner nhận.");
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException) when (dto.RequestId is not null)
            {
                // The unique owner/request key arbitrates concurrent retries across API instances.
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                var existing = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b =>
                    b.UserId == user.Id && b.RequestId == dto.RequestId);
                if (existing is null) throw;
                return Replay(existing, requestHash!);
            }
            return BookingReceipt(booking);
        });
        var orders = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization();
        orders.MapGet("/", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var userId = users.GetCurrentUserId(principal);
            if (userId is null) return Results.Unauthorized();
            return Results.Ok(await db.Bookings.Where(b => b.UserId == userId.Value)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.Id, orderCode = "CM" + b.Id, date = b.StartTime,
                    b.Price, b.Address, b.ContactName, b.ContactPhone,
                    cleaner = b.Cleaner == null ? null : new
                    {
                        name = b.Cleaner.User!.FullName,
                        phoneNumber = b.Cleaner.User.PhoneNumber
                    },
                    paymentMethod = b.Payment == null ? b.PaymentMethod : b.Payment.Method,
                    paymentStatus = b.Payment == null ? "Unpaid" : b.Payment.Status,
                    paidAt = b.Payment == null ? (DateTime?)null : b.Payment.PaidAt,
                    b.CreatedAt, b.Status, b.StartedAt, b.CompletedAt,
                    b.CustomerConfirmedAt, b.CustomerRating, b.CustomerReview }).ToListAsync());
        });
        // Compatibility for old clients: retain records instead of deleting history.
        orders.MapDelete("/{id:int}", CancelAsync);
        orders.MapPut("/{id:int}/cancel", CancelAsync);
        orders.MapPut("/{id:int}/reschedule", RescheduleAsync);
        orders.MapPut("/{id:int}/confirm-completion", ConfirmCompletionAsync);
        return app;
    }

    public record ConfirmCompletionDto(int Rating, string? Review);
    public record RescheduleDto(DateTimeOffset StartTime, DateTimeOffset ExpectedStartTime);

    private static async Task<IResult> RescheduleAsync(int id, RescheduleDto dto, AppDbContext db,
        CurrentUserService users, ClaimsPrincipal principal)
    {
        var userId = users.GetCurrentUserId(principal);
        if (userId is null) return Results.Unauthorized();
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        if (booking is null) return Results.NotFound();
        if (booking.UserId != userId) return Results.Forbid();
        var now = DateTimeOffset.UtcNow;
        if (BookingRules.ValidateStartTime(dto.StartTime, now) is { } error)
            return Results.BadRequest(new { message = error });
        var start = dto.StartTime.UtcDateTime;
        var expected = dto.ExpectedStartTime.UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Compare the schedule the customer saw, and update only while still unassigned.
        var count = await db.Bookings.Where(b => b.Id == id && b.UserId == userId &&
                b.Status == BookingStatuses.Pending && b.CleanerId == null &&
                b.StartTime == expected && b.StartTime > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.StartTime, start));
        if (count != 1)
            return Results.Conflict(new { message = "Đơn đã thay đổi, đã có cleaner nhận hoặc đã đến giờ làm. Vui lòng tải lại trước khi đổi lịch." });
        if (start != expected)
            BookingNotifications.Add(db, userId.Value, id, $"Rescheduled:{id}:{Guid.NewGuid():N}", "Đã đổi lịch", $"Đơn CM{id} đã cập nhật thời gian làm. Xem lịch mới trong đơn hàng.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(new { id, date = start, booking.Price, status = BookingStatuses.Pending });
    }

    private static IResult BookingReceipt(Booking booking) =>
        Results.Ok(new { booking.Id, booking.StartTime, booking.Price, booking.Status });

    private static IResult Replay(Booking booking, string hash) => booking.RequestHash == hash
        ? BookingReceipt(booking)
        : Results.Conflict(new { message = "Mã yêu cầu này đã được dùng cho thông tin đặt lịch khác. Vui lòng tải lại đơn hàng." });

    private static async Task<IResult> ConfirmCompletionAsync(int id, ConfirmCompletionDto dto, AppDbContext db,
        CurrentUserService users, ClaimsPrincipal principal)
    {
        var userId = users.GetCurrentUserId(principal);
        if (userId is null) return Results.Unauthorized();
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        if (booking is null) return Results.NotFound();
        if (booking.UserId != userId) return Results.Forbid();
        if (booking.Status != BookingStatuses.Completed)
            return Results.Conflict(new { message = "Chỉ xác nhận và đánh giá sau khi cleaner hoàn thành ca." });
        if (dto.Rating is < 1 or > 5 || dto.Review?.Length > 1000)
            return Results.BadRequest(new { message = "Chọn từ 1 đến 5 sao, nhận xét tối đa 1000 ký tự." });
        var review = string.IsNullOrWhiteSpace(dto.Review) ? null : dto.Review.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var count = await db.Bookings.Where(b => b.Id == id && b.UserId == userId && b.Status == BookingStatuses.Completed && b.CustomerConfirmedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.CustomerConfirmedAt, (DateTime?)DateTime.UtcNow)
                .SetProperty(b => b.CustomerRating, (int?)dto.Rating).SetProperty(b => b.CustomerReview, review));
        if (count == 1)
        {
            var cleanerUserId = await db.CleanerProfiles.Where(c => c.Id == booking.CleanerId).Select(c => (int?)c.UserId).SingleOrDefaultAsync();
            if (cleanerUserId is not null)
                BookingNotifications.Add(db, cleanerUserId.Value, id, $"Feedback:{id}", "Khách đã đánh giá ca làm", $"Ca CM{id} được khách đánh giá {dto.Rating}/5 sao.", true);
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        var saved = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == id);
        if (saved.CustomerRating != dto.Rating || saved.CustomerReview != review)
            return Results.Conflict(new { message = "Ca này đã được xác nhận và đánh giá. Vui lòng tải lại đơn." });
        // Customer feedback confirms service quality, never receipt of cash.
        return Results.Ok(new { saved.Id, saved.CustomerConfirmedAt, saved.CustomerRating, saved.CustomerReview });
    }

    private static async Task<IResult> CancelAsync(AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int id)
    {
        var userId = users.GetCurrentUserId(principal);
        if (userId is null) return Results.Unauthorized();
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return Results.NotFound();
        if (booking.UserId != userId.Value) return Results.Forbid();
        if (booking.Status == BookingStatuses.Cancelled) return Results.NoContent();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var count = await db.Bookings.Where(b => b.Id == id && b.UserId == userId.Value &&
                b.Status == BookingStatuses.Pending && b.CleanerId == null && b.StartTime > DateTime.UtcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Status, BookingStatuses.Cancelled));
        if (count != 1) return Results.Conflict(new { message = "Chỉ có thể hủy đơn đang chờ và chưa đến giờ làm." });
        BookingNotifications.Add(db, userId.Value, id, $"Cancelled:{id}", "Đã hủy đơn", $"Đơn CM{id} đã được hủy.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }
}
