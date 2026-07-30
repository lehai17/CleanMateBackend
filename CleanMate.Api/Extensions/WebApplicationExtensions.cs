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
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }

    public static WebApplication ApplyCleanMateMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        try
        {
            var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

            if (env.IsProduction())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
            }
            else
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContextSqlServer>();
                db.Database.Migrate();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Migration skipped: " + ex.Message);
        }

        return app;
    }
}
