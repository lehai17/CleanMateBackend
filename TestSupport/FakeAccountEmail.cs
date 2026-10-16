using System.Collections.Concurrent;
using CleanMate.Api.Services;

namespace CleanMate.TestSupport;

public record CapturedAccountMail(string Email, string Purpose, string? Code);

// This source is compiled only into the test projects, never into CleanMate.Api.
public sealed class FakeAccountEmail : IAccountEmail
{
    public bool IsConfigured { get; set; } = true;
    public ConcurrentQueue<CapturedAccountMail> Messages { get; } = new();
    public ConcurrentQueue<CapturedAccountMail> Attempts { get; } = new();
    private readonly ConcurrentDictionary<string, byte> failures = new();
    public void FailNextDelivery(string email) => failures[email] = 1;
    public Task SendAsync(string email, string purpose, string? code, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Attempts.Enqueue(new(email, purpose, code));
        if (failures.TryRemove(email, out _)) throw new InvalidOperationException("Deliberate test mail failure.");
        Messages.Enqueue(new(email, purpose, code));
        return Task.CompletedTask;
    }
}
