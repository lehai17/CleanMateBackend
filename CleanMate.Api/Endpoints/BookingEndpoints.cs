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
            var error = BookingRules.Validate(dto, DateTimeOffset.UtcNow);
            if (error is not null) return Results.BadRequest(new { message = error });
            var booking = new Booking
            {
                UserId = user.Id, StartTime = dto.StartTime.UtcDateTime, DurationHours = dto.DurationHours,
                Price = BookingRules.HourlyRates[dto.ServiceKey] * dto.DurationHours,
                Address = dto.Address.Trim(), Notes = $"[{dto.ServiceKey}] {dto.Notes?.Trim()}".Trim(),
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
                    b.Price, b.Address, b.PaymentMethod, b.CreatedAt, b.Status, b.StartedAt, b.CompletedAt }).ToListAsync());
        });
        // Compatibility for old clients: retain records instead of deleting history.
        orders.MapDelete("/{id:int}", CancelAsync);
        orders.MapPut("/{id:int}/cancel", CancelAsync);
        return app;
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
