using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanMate.Api.Extensions;

public static class AuthRateLimits
{
    public static void AddPublicAuthPolicies(this RateLimiterOptions options, IConfiguration configuration)
    {
        Add("auth-login", "Login", 30, 300);
        Add("auth-register", "Registration", 10, 3600);
        Add("auth-google", "Google", 30, 300);
        Add("auth-recovery-request", "RecoveryRequest", 10, 600);
        Add("auth-recovery-consume", "RecoveryConsume", 30, 900);

        void Add(string policy, string section, int defaultLimit, int defaultWindow)
        {
            var limit = configuration.GetValue($"RateLimits:{section}:PermitLimit", defaultLimit);
            var seconds = configuration.GetValue($"RateLimits:{section}:WindowSeconds", defaultWindow);
            if (limit is < 1 or > 10000 || seconds is < 1 or > 86400)
                throw new InvalidOperationException($"Invalid RateLimits:{section} configuration.");
            options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit=limit, Window=TimeSpan.FromSeconds(seconds), QueueLimit=0 }));
        }
    }

    public static void ConfigureProxies(this ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        options.ForwardLimit = 1;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        var proxies = configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? ["127.0.0.1", "::1"];
        if (proxies.Length == 0) throw new InvalidOperationException("At least one explicit trusted proxy address is required.");
        foreach (var value in proxies)
        {
            if (!IPAddress.TryParse(value, out var address)) throw new InvalidOperationException("Proxy:KnownProxies must contain IP addresses.");
            options.KnownProxies.Add(address);
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) options.KnownProxies.Add(address.MapToIPv6());
        }
    }
}
