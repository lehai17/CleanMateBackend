using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = "_myAllowSpecificOrigins";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: allowedOrigins,
        policy =>
        {
            policy.WithOrigins(
                "http://localhost:3000",   // React dev server
                "https://cleanmate-web.onrender.com" // nếu sau này bạn deploy React
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
        });
});

// ======================================================
// ============ 1. Add MVC Controllers + Views ===========
// ======================================================
builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient();


// ======================================================
// ============ 2. Database Configuration ===============
// ======================================================
var connectionString = builder.Configuration.GetConnectionString("Default");

if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
{
    // ✅ PostgreSQL (Render)
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString,
            npgsql => npgsql.MigrationsAssembly("CleanMate.Api")));
    Console.WriteLine("🟢 Using PostgreSQL database (Render)");
}
else
{
    // ✅ SQL Server (Local)
    builder.Services.AddDbContext<AppDbContextSqlServer>(options =>
        options.UseSqlServer(connectionString,
            sql => sql.MigrationsAssembly("CleanMate.Api")));

    builder.Services.AddScoped<AppDbContext, AppDbContextSqlServer>();

    Console.WriteLine("🟡 Using SQL Server database (Local)");
}

// ======================================================
// ============ 3. Session Configuration ================
// ======================================================
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ======================================================
// ============ 4. Build Application ====================
// ======================================================
var app = builder.Build();

app.UseCors("_myAllowSpecificOrigins");

// ======================================================
// ============ 5. Middleware Pipeline ==================
// ======================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// ======================================================
// ============ 6. Default Route ========================
// ======================================================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
