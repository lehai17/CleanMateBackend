using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CleanMate.Api.Endpoints;

public static class CleanerJobEndpoints
{
    public static IEndpointRouteBuilder MapCleanerJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cleaner/jobs").WithTags("Cleaner Jobs").RequireAuthorization();
        group.MapGet("/available", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var cleaner = await users.GetCleanerProfileAsync(db, principal);
            if (cleaner is null) return Results.Forbid();
            return Results.Ok(await db.Bookings.Where(b => b.Status == "Pending" && b.CleanerId == null &&
                    b.UserId != cleaner.UserId && b.StartTime > DateTime.UtcNow)
                .OrderBy(b => b.StartTime).Select(b => new { b.Id, customerName = b.User!.FullName,
                    customerPhone = b.User.PhoneNumber, b.StartTime, b.DurationHours, b.Price,
                    b.Address, b.PaymentMethod, b.Notes, b.Status }).ToListAsync());
        });
        group.MapGet("/mine", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var cleaner = await users.GetCleanerProfileAsync(db, principal);
            if (cleaner is null) return Results.Forbid();
            return Results.Ok(await db.Bookings.Where(b => b.CleanerId == cleaner.Id)
                .OrderByDescending(b => b.CreatedAt).Select(b => new { b.Id, customerName = b.User!.FullName,
                    customerPhone = b.User.PhoneNumber, b.StartTime, b.DurationHours, b.Price,
                    b.Address, b.PaymentMethod, b.Notes, b.Status }).ToListAsync());
        });
        group.MapPut("/{id:int}/accept", AcceptAsync);
        app.MapPut("/api/bookings/{id:int}/accept", AcceptAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> AcceptAsync(AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int id)
    {
        var cleaner = await users.GetCleanerProfileAsync(db, principal);
        if (cleaner is null) return Results.Forbid();
        // One conditional UPDATE serializes claims against other claims and cancellation.
        var count = await db.Bookings.Where(b => b.Id == id && b.Status == "Pending" &&
                b.CleanerId == null && b.UserId != cleaner.UserId && b.StartTime > DateTime.UtcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.CleanerId, (int?)cleaner.Id)
                .SetProperty(b => b.Status, "Accepted"));
        if (count == 1) return Results.Ok(new { message = "Nhận việc thành công." });
        if (!await db.Bookings.AnyAsync(b => b.Id == id)) return Results.NotFound();
        return Results.Conflict(new { message = "Đơn không còn khả dụng để nhận." });
    }
}