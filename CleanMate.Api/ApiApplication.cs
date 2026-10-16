using CleanMate.Api.Endpoints;
using CleanMate.Api.Extensions;

namespace CleanMate.Api;

public static class ApiApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        builder.Services.AddCleanMateApiServices(builder.Configuration);
        var app = builder.Build();
        app.UseCleanMateApi();
        app.MapPublicEndpoints();
        app.MapAuthEndpoints();
        app.MapProfileEndpoints();
        app.MapBookingEndpoints();
        app.MapCleanerJobEndpoints();
        app.MapPaymentEndpoints();
        app.MapNotificationEndpoints();
        app.MapManagementEndpoints();
        app.MapSupportEndpoints();
        return app;
    }
}
