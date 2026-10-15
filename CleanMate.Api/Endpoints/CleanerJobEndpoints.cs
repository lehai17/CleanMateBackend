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
            return Results.Ok(await db.Bookings.Where(b => b.Status == BookingStatuses.Pending && b.CleanerId == null &&
                    b.UserId != cleaner.UserId && b.StartTime > DateTime.UtcNow)
                .OrderBy(b => b.StartTime).Select(b => new { b.Id, customerName = b.User!.FullName,
                    customerPhone = b.User.PhoneNumber, b.StartTime, b.DurationHours, b.Price,
                    b.Address, b.PaymentMethod,
                    paymentStatus = b.Payment == null ? "Unpaid" : b.Payment.Status,
                    paidAt = b.Payment == null ? (DateTime?)null : b.Payment.PaidAt,
                    b.Notes, b.Status, b.StartedAt, b.CompletedAt }).ToListAsync());
        });
        group.MapGet("/mine", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var cleaner = await users.GetCleanerProfileAsync(db, principal);
            if (cleaner is null) return Results.Forbid();
            return Results.Ok(await db.Bookings.Where(b => b.CleanerId == cleaner.Id)
                .OrderByDescending(b => b.CreatedAt).Select(b => new { b.Id, customerName = b.User!.FullName,
                    customerPhone = b.User.PhoneNumber, b.StartTime, b.DurationHours, b.Price,
                    b.Address, b.PaymentMethod,
                    paymentStatus = b.Payment == null ? "Unpaid" : b.Payment.Status,
                    paidAt = b.Payment == null ? (DateTime?)null : b.Payment.PaidAt,
                    b.Notes, b.Status, b.StartedAt, b.CompletedAt }).ToListAsync());
        });
        group.MapPut("/{id:int}/accept", AcceptAsync);
        group.MapPut("/{id:int}/cash-payment", CashPaymentEndpoints.ConfirmAsync);
        group.MapPut("/{id:int}/start", (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int id) =>
            TransitionAsync(db, users, principal, id, BookingStatuses.Accepted, BookingStatuses.InProgress));
        group.MapPut("/{id:int}/complete", (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int id) =>
            TransitionAsync(db, users, principal, id, BookingStatuses.InProgress, BookingStatuses.Completed));
        app.MapPut("/api/bookings/{id:int}/accept", AcceptAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> TransitionAsync(AppDbContext db, CurrentUserService users,
        ClaimsPrincipal principal, int id, string expectedStatus, string nextStatus)
    {
        var cleaner = await users.GetCleanerProfileAsync(db, principal);
        if (cleaner is null) return Results.Forbid();
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return Results.NotFound();
        if (booking.CleanerId != cleaner.Id) return Results.Forbid();
        if (booking.Status != expectedStatus)
            return Results.Conflict(new { message = "Trạng thái đơn không cho phép thao tác này. Vui lòng tải lại." });

        var query = db.Bookings.Where(b => b.Id == id && b.CleanerId == cleaner.Id && b.Status == expectedStatus);
        var now = DateTime.UtcNow;
        // State, ownership and timestamp change in one conditional UPDATE. Replays cannot
        // advance the workflow twice or overwrite an already recorded timestamp.
        var count = nextStatus == BookingStatuses.InProgress
            ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Status, nextStatus)
                .SetProperty(b => b.StartedAt, (DateTime?)now))
            : await query.ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Status, nextStatus)
                .SetProperty(b => b.CompletedAt, (DateTime?)now));
        return count == 1
            ? Results.Ok(new { id, status = nextStatus })
            : Results.Conflict(new { message = "Trạng thái đơn đã thay đổi hoặc không cho phép thao tác này. Vui lòng tải lại." });
    }

    private static async Task<IResult> AcceptAsync(AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int id)
    {
        var cleaner = await users.GetCleanerProfileAsync(db, principal);
        if (cleaner is null) return Results.Forbid();
        // One conditional UPDATE serializes claims against other claims and cancellation.
        var count = await db.Bookings.Where(b => b.Id == id && b.Status == BookingStatuses.Pending &&
                b.CleanerId == null && b.UserId != cleaner.UserId && b.StartTime > DateTime.UtcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.CleanerId, (int?)cleaner.Id)
                .SetProperty(b => b.Status, BookingStatuses.Accepted));
        if (count == 1) return Results.Ok(new { message = "Nhận việc thành công." });
        if (!await db.Bookings.AnyAsync(b => b.Id == id)) return Results.NotFound();
        return Results.Conflict(new { message = "Đơn không còn khả dụng để nhận." });
    }
}
