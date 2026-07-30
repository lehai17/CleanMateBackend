using CleanMate.Api.Endpoints;
using CleanMate.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCleanMateApiServices(builder.Configuration);

var app = builder.Build();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing connection string 'Default'.");
Console.WriteLine(connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
    ? "Using PostgreSQL (Render)"
    : "Using SQL Server (Local)");
Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"Connection: {connectionString}");

app.UseCleanMateApi();

app.MapPublicEndpoints();
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapBookingEndpoints();
app.MapCleanerJobEndpoints();
app.MapPaymentEndpoints();

app.ApplyCleanMateMigrations();
app.Run();
