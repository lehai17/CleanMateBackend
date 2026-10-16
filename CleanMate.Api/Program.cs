using CleanMate.Api;
using CleanMate.Api.Extensions;

var app = ApiApplication.Build(args.Where(arg => arg != "--create-admin").ToArray());
app.ApplyCleanMateMigrations();
if (args.Contains("--create-admin"))
{
    using var scope = app.Services.CreateScope();
    await CleanMate.Api.Services.AdminBootstrap.CreateAsync(
        scope.ServiceProvider.GetRequiredService<CleanMate.Api.Infrastructure.AppDbContext>(),
        Environment.GetEnvironmentVariable("CLEANMATE_ADMIN_EMAIL") ?? "",
        Environment.GetEnvironmentVariable("CLEANMATE_ADMIN_PASSWORD") ?? "",
        Environment.GetEnvironmentVariable("CLEANMATE_ADMIN_NAME") ?? "Administrator");
    Console.WriteLine("Administrator is ready. No password is printed.");
    return;
}
app.Run();
