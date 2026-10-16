using System.Security.Claims;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class RebookingEndpoints
{
    public static void Map(RouteGroupBuilder orders)
    {
        orders.MapGet("/{id:int}/rebooking", async (int id, AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var ownerId = users.GetCurrentUserId(principal);
            var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && b.UserId == ownerId);
            if (booking is null) return Results.NotFound();
            if (booking.Status is not (BookingStatuses.Completed or BookingStatuses.Cancelled))
                return Results.Conflict(new { message = "Chỉ đặt lại đơn đã hoàn thành hoặc đã hủy. Đơn đang chờ/đang làm vẫn được giữ để theo dõi." });
            // Existing bookings store the service key as a prefix; never infer it from a display name.
            var notes = booking.Notes ?? "";
            string? serviceKey = null;
            var close = notes.IndexOf(']');
            if (notes.StartsWith('[') && close is > 1 and <= 33)
            {
                var key = notes[1..close];
                if (key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && await db.ServiceOfferings.AnyAsync(s => s.Key == key))
                {
                    serviceKey = key;
                    notes = notes[(close + 1)..].TrimStart();
                }
            }
            return Results.Ok(new { sourceId = booking.Id, ownerId = booking.UserId, serviceKey, booking.DurationHours,
                booking.Address, booking.ContactName, booking.ContactPhone, notes });
        });
    }
}
