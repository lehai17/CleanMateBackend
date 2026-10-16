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
                    b.UserId != cleaner.UserId && b.StartTime > DateTime.UtcNow &&
                    !db.Bookings.Any(assigned => assigned.CleanerId == cleaner.Id &&
                        (assigned.Status == BookingStatuses.Accepted || assigned.Status == BookingStatuses.InProgress) &&
                        assigned.StartTime < b.StartTime.AddHours(b.DurationHours) &&
                        assigned.StartTime.AddHours(assigned.DurationHours) > b.StartTime))
                .OrderBy(b => b.StartTime).Select(b => new { b.Id, customerName = b.ContactName ?? b.User!.FullName,
                    customerPhone = b.ContactName == null ? b.User!.PhoneNumber : b.ContactPhone, b.StartTime, b.DurationHours, b.Price,
                    b.Address, paymentMethod = b.Payment == null ? b.PaymentMethod : b.Payment.Method,
                    paymentStatus = b.Payment == null ? "Unpaid" : b.Payment.Status,
                    paidAt = b.Payment == null ? (DateTime?)null : b.Payment.PaidAt,
                    b.Notes, b.Status, b.StartedAt, b.CompletedAt, b.CustomerConfirmedAt, b.CustomerRating }).ToListAsync());
        });
        group.MapGet("/mine", async (AppDbContext db, CurrentUserService users, ClaimsPrincipal principal) =>
        {
            var cleaner = await users.GetCleanerProfileAsync(db, principal);
            if (cleaner is null) return Results.Forbid();
            return Results.Ok(await db.Bookings.Where(b => b.CleanerId == cleaner.Id)
                .OrderByDescending(b => b.CreatedAt).Select(b => new { b.Id, customerName = b.ContactName ?? b.User!.FullName,
                    customerPhone = b.ContactName == null ? b.User!.PhoneNumber : b.ContactPhone, b.StartTime, b.DurationHours, b.Price,
                    b.Address, paymentMethod = b.Payment == null ? b.PaymentMethod : b.Payment.Method,
                    paymentStatus = b.Payment == null ? "Unpaid" : b.Payment.Status,
                    paidAt = b.Payment == null ? (DateTime?)null : b.Payment.PaidAt,
                    b.Notes, b.Status, b.StartedAt, b.CompletedAt, b.CustomerConfirmedAt, b.CustomerRating }).ToListAsync());
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
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockCleanerAsync(db, cleaner.Id);
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return Results.NotFound();
        if (booking.CleanerId != cleaner.Id) return Results.Forbid();
        if (booking.Status != expectedStatus)
            return Results.Conflict(new { message = "Trạng thái đơn không cho phép thao tác này. Vui lòng tải lại." });
        if (nextStatus == BookingStatuses.InProgress && await db.Bookings.AnyAsync(b =>
            b.CleanerId == cleaner.Id && b.Id != id && b.Status == BookingStatuses.InProgress))
            return Results.Conflict(new { message = "Bạn đang làm một ca khác. Hãy hoàn thành ca hiện tại trước khi bắt đầu ca mới." });

        var query = db.Bookings.Where(b => b.Id == id && b.CleanerId == cleaner.Id && b.Status == expectedStatus);
        var now = DateTime.UtcNow;
        // State, ownership and timestamp change in one conditional UPDATE. Replays cannot
        // advance the workflow twice or overwrite an already recorded timestamp.
        var count = nextStatus == BookingStatuses.InProgress
            ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Status, nextStatus)
                .SetProperty(b => b.StartedAt, (DateTime?)now))
            : await query.ExecuteUpdateAsync(setters => setters.SetProperty(b => b.Status, nextStatus)
                .SetProperty(b => b.CompletedAt, (DateTime?)now));
        if (count != 1) return Results.Conflict(new { message = "Trạng thái đơn đã thay đổi hoặc không cho phép thao tác này. Vui lòng tải lại." });
        await transaction.CommitAsync();
        return Results.Ok(new { id, status = nextStatus });
    }

    private static async Task<IResult> AcceptAsync(AppDbContext db, CurrentUserService users, ClaimsPrincipal principal, int id)
    {
        var cleaner = await users.GetCleanerProfileAsync(db, principal);
        if (cleaner is null) return Results.Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockCleanerAsync(db, cleaner.Id);
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        if (booking is null) return Results.NotFound();
        if (booking.Status != BookingStatuses.Pending || booking.CleanerId != null ||
            booking.UserId == cleaner.UserId || booking.StartTime <= DateTime.UtcNow)
            return Results.Conflict(new { message = "Đơn không còn khả dụng để nhận." });
        var end = booking.StartTime.AddHours(booking.DurationHours);
        if (await db.Bookings.AnyAsync(b => b.CleanerId == cleaner.Id &&
            (b.Status == BookingStatuses.Accepted || b.Status == BookingStatuses.InProgress) &&
            b.StartTime < end && b.StartTime.AddHours(b.DurationHours) > booking.StartTime))
            return Results.Conflict(new { message = "Ca này trùng lịch với ca bạn đã nhận. Vui lòng chọn khung giờ khác." });
        // The profile row lock serializes all assignments and starts for this cleaner.
        // The conditional booking update still arbitrates other cleaners and cancellation.
        var count = await db.Bookings.Where(b => b.Id == id && b.Status == BookingStatuses.Pending &&
                b.CleanerId == null && b.UserId != cleaner.UserId && b.StartTime > DateTime.UtcNow &&
                b.StartTime == booking.StartTime)
            .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.CleanerId, (int?)cleaner.Id)
                .SetProperty(b => b.Status, BookingStatuses.Accepted));
        if (count != 1) return Results.Conflict(new { message = "Đơn không còn khả dụng để nhận." });
        await transaction.CommitAsync();
        return Results.Ok(new { message = "Nhận việc thành công." });
    }

    private static async Task LockCleanerAsync(AppDbContext db, int cleanerId)
    {
        // Database locks cover multiple API processes/devices, unlike an in-memory lock.
        // Called inside an explicit transaction and retained until commit/rollback.
        if (db.Database.IsNpgsql())
            await db.CleanerProfiles.FromSqlInterpolated($"SELECT * FROM \"CleanerProfiles\" WHERE \"Id\" = {cleanerId} FOR UPDATE")
                .AsNoTracking().SingleAsync();
        else
            await db.CleanerProfiles.FromSqlInterpolated($"SELECT * FROM [CleanerProfiles] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {cleanerId}")
                .AsNoTracking().SingleAsync();
    }
}
