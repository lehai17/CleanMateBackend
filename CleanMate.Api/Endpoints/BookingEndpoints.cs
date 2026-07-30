using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CleanMate.Api.Endpoints;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var bookingGroup = app.MapGroup("/api/bookings").WithTags("Bookings").RequireAuthorization();

        bookingGroup.MapPost("/", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal, Booking booking) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(db, principal);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            booking.UserId = user.Id;
            booking.CleanerId = null;
            booking.CreatedAt = DateTime.UtcNow;
            booking.Status = "Pending";

            if (string.IsNullOrWhiteSpace(booking.Address))
            {
                booking.Address = user.Address ?? string.Empty;
            }

            db.Bookings.Add(booking);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                booking.Id,
                booking.StartTime,
                booking.Price,
                booking.Status
            });
        });

        var orderGroup = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization();

        orderGroup.MapGet("/", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal) =>
        {
            var userId = currentUserService.GetCurrentUserId(principal);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var list = await db.Bookings
                .Where(booking => booking.UserId == userId.Value)
                .OrderByDescending(booking => booking.CreatedAt)
                .Select(booking => new
                {
                    id = booking.Id,
                    orderCode = "CM" + booking.Id,
                    date = booking.StartTime,
                    price = booking.Price,
                    address = booking.Address,
                    paymentMethod = booking.PaymentMethod,
                    createdAt = booking.CreatedAt,
                    status = booking.Status
                })
                .ToListAsync();

            return Results.Ok(list);
        });

        orderGroup.MapDelete("/{id:int}", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal, int id) =>
        {
            var userId = currentUserService.GetCurrentUserId(principal);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var booking = await db.Bookings.FirstOrDefaultAsync(entry => entry.Id == id);
            if (booking is null)
            {
                return Results.NotFound(new { message = $"Booking ID {id} not found." });
            }

            if (booking.UserId != userId.Value)
            {
                return Results.Forbid();
            }

            db.Bookings.Remove(booking);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }
}
