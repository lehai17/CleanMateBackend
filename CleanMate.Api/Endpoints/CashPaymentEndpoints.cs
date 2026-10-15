using System.Security.Claims;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Endpoints;

public static class CashPaymentEndpoints
{
    public static async Task<IResult> ConfirmAsync(int id, AppDbContext db,
        CurrentUserService users, ClaimsPrincipal principal)
    {
        var cleaner = await users.GetCleanerProfileAsync(db, principal);
        if (cleaner is null) return Results.Forbid();
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        if (booking is null) return Results.NotFound();
        if (booking.CleanerId != cleaner.Id) return Results.Forbid();
        if (booking.Status != BookingStatuses.Completed || booking.PaymentMethod != "cash")
            return Results.Conflict(new { message = "Chỉ xác nhận tiền mặt sau khi hoàn thành công việc." });

        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id);
        if (existing is not null) return Receipt(existing);
        var now = DateTime.UtcNow;
        var payment = new Payment { BookingId = id, Amount = booking.Price,
            CollectedByCleanerId = cleaner.Id, CreatedAt = now, PaidAt = now };
        db.Payments.Add(payment);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // A unique BookingId permits one receipt even when requests race.
            db.ChangeTracker.Clear();
            existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id);
            if (existing is null) throw;
            return Receipt(existing);
        }
        return Receipt(payment);
    }

    private static IResult Receipt(Payment payment) => Results.Ok(new {
        payment.Id, payment.BookingId, payment.Amount, payment.Method, payment.Status,
        payment.PaidAt, payment.CollectedByCleanerId });
}
