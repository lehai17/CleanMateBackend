using CleanMate.Api.Contracts;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
            // Preserve the booking contact rather than reading a mutable profile later.
            // Older clients can omit these fields and use their current profile once.
            dto = dto with { ContactName = dto.ContactName ?? user.FullName,
                ContactPhone = dto.ContactPhone ?? user.PhoneNumber };
            var error = BookingRules.Validate(dto, DateTimeOffset.UtcNow);
            if (error is not null) return Results.BadRequest(new { message = error });
            var booking = new Booking
            {
                UserId = user.Id, StartTime = dto.StartTime.UtcDateTime, DurationHours = dto.DurationHours,
                Price = BookingRules.HourlyRates[dto.ServiceKey] * dto.DurationHours,
                Address = dto.Address.Trim(), Notes = $"[{dto.ServiceKey}] {dto.Notes?.Trim()}".Trim(),
                ContactName = dto.ContactName?.Trim(), ContactPhone = dto.ContactPhone?.Trim(),
                PaymentMethod = "cash", CreatedAt = DateTime.UtcNow, Status = BookingStatuses.Pending
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            return Results.Ok(new { booking.Id, booking.StartTime, booking.Price, booking.Status });
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
        orders.MapPut("/{id:int}/confirm-completion", ConfirmCompletionAsync);
        return app;
    }

    public record ConfirmCompletionDto(int Rating, string? Review);

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
        await db.Bookings.Where(b => b.Id == id && b.UserId == userId && b.Status == BookingStatuses.Completed && b.CustomerConfirmedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.CustomerConfirmedAt, (DateTime?)DateTime.UtcNow)
                .SetProperty(b => b.CustomerRating, (int?)dto.Rating).SetProperty(b => b.CustomerReview, review));
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
        var count = await db.Bookings.Where(b => b.Id == id && b.UserId == userId.Value &&
                b.Status == BookingStatuses.Pending && b.CleanerId == null && b.StartTime > DateTime.UtcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Status, BookingStatuses.Cancelled));
        return count == 1 ? Results.NoContent() : Results.Conflict(new { message = "Chỉ có thể hủy đơn đang chờ và chưa đến giờ làm." });
    }
}
