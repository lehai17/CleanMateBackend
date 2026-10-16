using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanMate.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseCleanMateApi(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRouting();
        app.UseCors(ServiceCollectionExtensions.CorsPolicy);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }

    public static WebApplication ApplyCleanMateMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        // Fail startup when migration fails, instead of serving requests against an incomplete schema.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        return app;
    }
}
