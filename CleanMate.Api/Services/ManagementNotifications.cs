using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Services;

public static class ManagementNotifications
{
    // Capture recipients before taking write locks; add their notifications in the
    // operation's transaction. This avoids scanning Users while a signup holds user locks.
    public static Task<List<int>> RecipientsAsync(AppDbContext db, int excludeUserId) =>
        db.Users.AsNoTracking().Where(u => u.IsActive && u.Id != excludeUserId &&
            (u.Role == UserRole.Admin || u.Role == UserRole.Staff)).Select(u => u.Id).ToListAsync();

    public static void Add(AppDbContext db, IEnumerable<int> recipients, string eventKey, string target, int targetId,
        string title, string message, int? bookingId = null)
    {
        var now = DateTime.UtcNow;
        db.Notifications.AddRange(recipients.Select(id => new Notification {
            UserId=id,EventKey=eventKey,Target=target,TargetId=targetId,BookingId=bookingId,
            Title=title,Message=message,CreatedAt=now
        }));
    }
}
