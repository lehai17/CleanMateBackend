using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        services.AddScoped<CurrentUserService>();
        services.AddScoped<AuthService>();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<MomoPaymentService>();
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
