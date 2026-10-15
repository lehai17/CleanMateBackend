using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Ok("CleanMate API is running!"));

        app.MapGet("/api/cleaners", async (AppDbContext db) =>
        {
            var list = await db.CleanerProfiles
                .Include(profile => profile.User)
                .Select(profile => new
                {
                    profile.Id,
                    name = profile.User!.FullName,
                    email = profile.User!.Email,
                    phoneNumber = profile.User!.PhoneNumber,
                    city = profile.City,
                    hourlyRate = profile.HourlyRate,
                    avgRating = db.Bookings.Where(booking => booking.CleanerId == profile.Id && booking.CustomerConfirmedAt != null)
                        .Average(booking => (double?)booking.CustomerRating) ?? profile.AvgRating,
                    bio = profile.Bio,
                    addressText = profile.AddressText
                })
                .ToListAsync();

            return Results.Ok(list);
        });

        return app;
    }
}
