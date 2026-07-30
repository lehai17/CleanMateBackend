using CleanMate.Api.Contracts;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;

namespace CleanMate.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (AppDbContext db, AuthService authService, RegisterDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            {
                return Results.BadRequest("Email/Password required");
            }

            var email = dto.Email.Trim().ToLowerInvariant();
            if (await authService.EmailExistsAsync(db, email))
            {
                return Results.BadRequest("Email already used");
            }

            var result = await authService.RegisterAsync(db, dto);
            return Results.Ok(result);
        });

        group.MapPost("/login", async (AppDbContext db, AuthService authService, LoginDto dto) =>
        {
            var result = await authService.LoginAsync(db, dto);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });

        group.MapPost("/google", async (AppDbContext db, AuthService authService, GoogleLoginDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.IdToken))
            {
                return Results.BadRequest("Missing idToken");
            }

            var result = await authService.LoginWithGoogleAsync(db, dto);
            return result is null
                ? Results.BadRequest("Invalid Google token")
                : Results.Ok(result);
        });

        return app;
    }
}
