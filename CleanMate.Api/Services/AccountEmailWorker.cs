using System.Security.Cryptography;
using System.Text;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Services;

public sealed class AccountEmailWorker(AccountEmailQueue queue, IServiceScopeFactory scopes, IAccountEmail sender, ILogger<AccountEmailWorker> logger) : BackgroundService
{
    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code.Trim().ToUpperInvariant())));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAll(stoppingToken))
        {
            try { await ProcessAsync(request, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Account email processing failed. No address, code or SMTP exception is logged."); }
        }
    }

    private async Task ProcessAsync(AccountEmailRequest request, CancellationToken cancellation)
    {
        if (!sender.IsConfigured) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == request.Email && u.IsActive, cancellation);
        if (user is null) return;
        if (request.Purpose == "PasswordChanged") { await sender.SendAsync(user.Email, request.Purpose, null, cancellation); return; }
        if (request.Purpose == "ResetPassword" && string.IsNullOrEmpty(user.PasswordHash) || request.Purpose == "VerifyEmail" && user.EmailVerifiedAt is not null) return;
        var now = DateTime.UtcNow;
        var window = new DateTimeOffset(now).ToUnixTimeSeconds() / 120;
        if (await db.AccountCodes.AnyAsync(c => c.UserId == user.Id && c.Purpose == request.Purpose && c.IssueWindow == window, cancellation)) return;
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var row = new AccountCode { UserId = user.Id, Purpose = request.Purpose, Hash = Hash(code), TokenVersion = user.TokenVersion,
            IssueWindow = window, CreatedAt = now, ExpiresAt = now.AddMinutes(30) };
        db.AccountCodes.Add(row);
        try { await db.SaveChangesAsync(cancellation); }
        catch (DbUpdateException) {
            db.ChangeTracker.Clear();
            if (await db.AccountCodes.AnyAsync(c => c.UserId == user.Id && c.Purpose == request.Purpose && c.IssueWindow == window, cancellation)) return;
            throw;
        }
        await sender.SendAsync(user.Email, request.Purpose, code, cancellation);
        await db.AccountCodes.Where(c => c.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.DeliveredAt, (DateTime?)DateTime.UtcNow), cancellation);
        await db.AccountCodes.Where(c => c.ExpiresAt < now.AddDays(-7)).ExecuteDeleteAsync(cancellation);
    }
}
