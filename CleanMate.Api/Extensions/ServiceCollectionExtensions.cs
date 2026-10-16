using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Security.Claims;

namespace CleanMate.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicy = "_myAllowSpecificOrigins";

    public static IServiceCollection AddCleanMateApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddCleanMateDb(configuration);
        services.AddCleanMateCors(configuration);
        services.AddCleanMateAuth(configuration);
        services.Configure<ForwardedHeadersOptions>(options => options.ConfigureProxies(configuration));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPublicAuthPolicies(configuration);
            options.AddPolicy("password-change", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
            options.OnRejected = async (context, cancellation) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter,out var retry) ? Math.Max(1,(int)Math.Ceiling(retry.TotalSeconds)) : 300;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                await context.HttpContext.Response.WriteAsJsonAsync(new { message = $"Thao tác quá nhiều lần. Vui lòng thử lại sau {seconds} giây.",retryAfterSeconds=seconds },cancellation);
            };
        });
        services.AddScoped<CurrentUserService>();
        services.AddScoped<AuthService>();
        services.AddSingleton<JwtTokenService>();
        services.AddControllers();

        return services;
    }

    public static IServiceCollection AddCleanMateDb(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing connection string 'Default'.");

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsAssembly("CleanMate.Api")
                                    .MigrationsHistoryTable("__EFMigrationsHistory", "public")));
        }
        else
        {
            services.AddDbContext<AppDbContextSqlServer>(options =>
                options.UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsAssembly("CleanMate.Api")
                              .MigrationsHistoryTable("__EFMigrationsHistory", "dbo")));

            services.AddScoped<AppDbContext, AppDbContextSqlServer>();
        }

        return services;
    }

    public static IServiceCollection AddCleanMateCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicy, policy =>
            {
                policy
                    .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                        ?? new[] { "http://localhost:3000" })
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }

    public static IServiceCollection AddCleanMateAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Missing Jwt:Key");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var users = context.HttpContext.RequestServices.GetRequiredService<CurrentUserService>();
                        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var id = users.GetCurrentUserId(context.Principal!);
                        var claim = context.Principal!.FindFirst("token_version")?.Value;
                        var version = 0;
                        if ((claim is not null && (!int.TryParse(claim,out version) || version < 0)) ||
                            id is null || !await db.Users.AnyAsync(u => u.Id == id && u.IsActive && u.TokenVersion == version))
                            context.Fail("Account is unavailable.");
                    }
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "CleanMate",
                    ValidateAudience = true,
                    ValidAudience = configuration["Jwt:Audience"] ?? "CleanMate",
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    IssuerSigningKey = signingKey
                };
            });

        services.AddAuthorization();

        return services;
    }
}
