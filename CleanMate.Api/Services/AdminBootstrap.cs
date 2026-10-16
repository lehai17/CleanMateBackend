using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Services;

public static class AdminBootstrap
{
    public static async Task CreateAsync(AppDbContext db, string email, string password, string name)
    {
        if (!System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var address) || address.Address != email.Trim() ||
            string.IsNullOrWhiteSpace(password) || password.Length < 12 || System.Text.Encoding.UTF8.GetByteCount(password) > 72 || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new InvalidOperationException("Provide a valid email, name and password (at least 12 characters, at most 72 UTF-8 bytes).");
        var normalized = address.Address.ToLowerInvariant();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized);
        if (existing is not null)
        {
            if (existing.Role == UserRole.Admin && existing.IsActive) return;
            throw new InvalidOperationException("Email already belongs to another account. No role was changed.");
        }
        var user = new User { FullName = name.Trim(), Email = normalized, Role = UserRole.Admin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Users.Add(user); await db.SaveChangesAsync();
        db.AdminAudits.Add(new AdminAudit { ActorId = user.Id, Action = "BootstrapAdmin", Target = $"User:{user.Id}",
            Details = "Administrator created by the local bootstrap command.", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
}
