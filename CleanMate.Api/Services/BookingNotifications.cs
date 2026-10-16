using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;

namespace CleanMate.Api.Services;

public static class BookingNotifications
{
    // Added to the same context/transaction as the successful booking operation.
    public static void Add(AppDbContext db, int userId, int bookingId, string eventKey,
        string title, string message, bool forCleaner = false) => db.Notifications.Add(new Notification
        {
            UserId = userId, BookingId = bookingId, EventKey = eventKey,
            Title = title, Message = message, ForCleaner = forCleaner, CreatedAt = DateTime.UtcNow
        });
}
