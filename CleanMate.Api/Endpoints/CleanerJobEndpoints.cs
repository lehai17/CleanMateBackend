using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CleanMate.Api.Endpoints;

public static class CleanerJobEndpoints
{
    public static IEndpointRouteBuilder MapCleanerJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cleaner/jobs")
            .WithTags("Cleaner Jobs")
            .RequireAuthorization();

        group.MapGet("/available", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal) =>
        {
            var cleanerProfile = await currentUserService.GetCleanerProfileAsync(db, principal);
            if (cleanerProfile is null)
            {
                return Results.Forbid();
            }

            var list = await db.Bookings
                .Include(booking => booking.User)
                .Where(booking => booking.Status == "Pending" && booking.CleanerId == null)
                .OrderBy(booking => booking.StartTime)
                .Select(booking => new
                {
                    id = booking.Id,
                    customerName = booking.User!.FullName,
                    customerPhone = booking.User!.PhoneNumber,
                    startTime = booking.StartTime,
                    durationHours = booking.DurationHours,
                    price = booking.Price,
                    address = booking.Address,
                    paymentMethod = booking.PaymentMethod,
                    notes = booking.Notes,
                    status = booking.Status
                })
                .ToListAsync();

            return Results.Ok(list);
        });

        group.MapGet("/mine", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal) =>
        {
            var cleanerProfile = await currentUserService.GetCleanerProfileAsync(db, principal);
            if (cleanerProfile is null)
            {
                return Results.Forbid();
            }

            var list = await db.Bookings
                .Include(booking => booking.User)
                .Where(booking => booking.CleanerId == cleanerProfile.Id)
                .OrderByDescending(booking => booking.CreatedAt)
                .Select(booking => new
                {
                    id = booking.Id,
                    customerName = booking.User!.FullName,
                    customerPhone = booking.User!.PhoneNumber,
                    startTime = booking.StartTime,
                    durationHours = booking.DurationHours,
                    price = booking.Price,
                    address = booking.Address,
                    paymentMethod = booking.PaymentMethod,
                    notes = booking.Notes,
                    status = booking.Status
                })
                .ToListAsync();

            return Results.Ok(list);
        });

        group.MapPut("/{id:int}/accept", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal, int id) =>
        {
            var cleanerProfile = await currentUserService.GetCleanerProfileAsync(db, principal);
            if (cleanerProfile is null)
            {
                return Results.Forbid();
            }

            var booking = await db.Bookings.FirstOrDefaultAsync(entry => entry.Id == id);
            if (booking is null)
            {
                return Results.NotFound(new { message = $"Booking {id} not found." });
            }

            if (booking.Status != "Pending" || booking.CleanerId is not null)
            {
                return Results.BadRequest(new { message = "Booking has already been claimed." });
            }

            booking.CleanerId = cleanerProfile.Id;
            booking.Status = "Accepted";
            await db.SaveChangesAsync();

            return Results.Ok(new { message = "Booking accepted successfully." });
        });

        app.MapPut("/api/bookings/{id:int}/accept", async (AppDbContext db, CurrentUserService currentUserService, ClaimsPrincipal principal, int id) =>
        {
            var cleanerProfile = await currentUserService.GetCleanerProfileAsync(db, principal);
            if (cleanerProfile is null)
            {
                return Results.Forbid();
            }

            var booking = await db.Bookings.FirstOrDefaultAsync(entry => entry.Id == id);
            if (booking is null)
            {
                return Results.NotFound(new { message = $"Booking {id} not found." });
            }

            if (booking.Status != "Pending" || booking.CleanerId is not null)
            {
                return Results.BadRequest(new { message = "Booking has already been claimed." });
            }

            booking.CleanerId = cleanerProfile.Id;
            booking.Status = "Accepted";
            await db.SaveChangesAsync();

            return Results.Ok(new { message = "Booking accepted successfully." });
        }).RequireAuthorization();

        return app;
    }
}
