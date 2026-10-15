using CleanMate.CleanerWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace CleanMate.CleanerWeb;

public static class CleanerApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        builder.Services.AddControllersWithViews(options =>
        {
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            options.Filters.Add<ApiExceptionFilter>();
        }).AddApplicationPart(typeof(CleanMate.CleanerWeb.Controllers.AuthController).Assembly);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient<CleanerApiClient>(client =>
        {
            var baseUrl = builder.Configuration["Api:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!builder.Environment.IsDevelopment())
                    throw new InvalidOperationException("Configure Api:BaseUrl for the cleaner portal.");
                baseUrl = "http://localhost:5238";
            }
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(60);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
        var app = builder.Build();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseSession();
        app.MapControllerRoute("default", "{controller=Auth}/{action=Login}/{id?}");
        return app;
    }
}
