using CleanMate.Api;
using CleanMate.Api.Infrastructure;
using CleanMate.CleanerWeb;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

// This executable always creates its own database. It never reads application connection strings.
var databaseName = "CleanMateSystemTests_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLEANMATE_TEST_SQL")
    ?? "Server=localhost;Integrated Security=true;Encrypt=false;TrustServerCertificate=true")
{ InitialCatalog = databaseName };
var webRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("CLEANMATE_E2E_WEB_ROOT")
    ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", ".e2e-build"));
if (!File.Exists(Path.Combine(webRoot, "index.html")))
    throw new InvalidOperationException("Run npm run build:e2e in web before starting the system-test host.");

var api = ApiApplication.Build([], builder =>
{
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = connection.ConnectionString,
        ["Jwt:Key"] = "cleanmate-system-tests-only-signing-secret-at-least-32-bytes",
        ["Jwt:Issuer"] = "CleanMateSystemTests",
        ["Jwt:Audience"] = "CleanMateSystemTests",
        ["Cors:AllowedOrigins:0"] = "http://127.0.0.1:13000"
    });
    builder.WebHost.UseUrls("http://127.0.0.1:15238");
});
var portal = CleanerApplication.Build([], builder =>
{
    builder.Environment.ApplicationName = typeof(CleanerApplication).Assembly.GetName().Name!;
    var portalWebRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "CleanMate.CleanerWeb", "wwwroot"));
    builder.Environment.WebRootPath = portalWebRoot;
    builder.Environment.WebRootFileProvider = new PhysicalFileProvider(portalWebRoot);
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Api:BaseUrl"] = "http://127.0.0.1:15238" });
    builder.WebHost.UseUrls("http://127.0.0.1:15080");
});
var frontendBuilder = WebApplication.CreateBuilder();
frontendBuilder.WebHost.UseUrls("http://127.0.0.1:13000");
var frontend = frontendBuilder.Build();
using var files = new PhysicalFileProvider(webRoot);
frontend.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
frontend.UseStaticFiles(new StaticFileOptions { FileProvider = files });
frontend.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
frontend.MapPost("/__system-tests/shutdown", (HttpContext context) =>
{
    context.Response.OnCompleted(() =>
    {
        frontend.Lifetime.StopApplication();
        return Task.CompletedTask;
    });
    return Results.Ok();
});

try
{
    using (var scope = api.Services.CreateScope())
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await api.StartAsync();
    await portal.StartAsync();
    await frontend.StartAsync();
    Console.WriteLine("System tests ready: frontend 13000, API 15238, cleaner portal 15080.");
    await frontend.WaitForShutdownAsync();
}
finally
{
    await frontend.StopAsync();
    await portal.StopAsync();
    await api.StopAsync();
    using (var scope = api.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var target = new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
        if (target != databaseName || !target.StartsWith("CleanMateSystemTests_"))
            throw new InvalidOperationException("Unsafe system-test database cleanup target.");
        await db.Database.EnsureDeletedAsync();
    }
    await frontend.DisposeAsync();
    await portal.DisposeAsync();
    await api.DisposeAsync();
    var cleanupPath = Path.GetFullPath(Path.Combine(webRoot, "..", "test-results", "system-host-cleanup.json"));
    Directory.CreateDirectory(Path.GetDirectoryName(cleanupPath)!);
    await File.WriteAllTextAsync(cleanupPath,
        System.Text.Json.JsonSerializer.Serialize(new { database = databaseName, cleaned = true }));
}
