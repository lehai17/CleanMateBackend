using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanMate.Api;
using CleanMate.Api.Infrastructure;
using CleanMate.CleanerWeb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CleanMate.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string databaseName = "CleanMateTests_" + Guid.NewGuid().ToString("N");
    public WebApplication Api { get; private set; } = null!;
    public WebApplication Portal { get; private set; } = null!;
    public Uri ApiUrl => new(Api.Urls.Single());
    public Uri PortalUrl => new(Portal.Urls.Single());

    public async Task InitializeAsync()
    {
        // Never use the application's configured database. Override even a supplied test connection's database name.
        var sql = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLEANMATE_TEST_SQL")
            ?? "Server=localhost;Integrated Security=true;Encrypt=false;TrustServerCertificate=true");
        sql.InitialCatalog = databaseName;
        Api = ApiApplication.Build([], builder =>
        {
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = sql.ConnectionString,
                ["Jwt:Key"] = "cleanmate-integration-tests-only-secret-at-least-32-bytes",
                ["Jwt:Issuer"] = "CleanMateTests",
                ["Jwt:Audience"] = "CleanMateTests",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        });
        using (var scope = Api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        await Api.StartAsync();
        Portal = CleanerApplication.Build([], builder =>
        {
            builder.Environment.ApplicationName = typeof(CleanerApplication).Assembly.GetName().Name!;
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Api:BaseUrl"] = ApiUrl.ToString() });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        });
        await Portal.StartAsync();
    }

    public HttpClient Client(string? token = null)
    {
        var client = new HttpClient { BaseAddress = ApiUrl };
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task DisposeAsync()
    {
        if (Portal is not null) await Portal.DisposeAsync();
        if (Api is null) return;
        await Api.StopAsync();
        using (var scope = Api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var target = new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
            if (target != databaseName || !target.StartsWith("CleanMateTests_")) throw new InvalidOperationException("Unsafe test cleanup target.");
            await db.Database.EnsureDeletedAsync();
        }
        await Api.DisposeAsync();
    }
}

public class ApiIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Password = "CleanMate-test-123!";

    [Fact]
    public void Sql_model_matches_existing_migrations()
    {
        using var scope = fixture.Api.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.HasPendingModelChanges());
    }

    private async Task<(string Token, int Id, string Email)> Register(string role = "Customer")
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        using var client = fixture.Client();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { fullName = "Integration User", email, password = Password, role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (auth.GetProperty("token").GetString()!, auth.GetProperty("user").GetProperty("id").GetInt32(), email);
    }

    private static object Booking(object? startTime = null, string serviceKey = "cleaning", int durationHours = 2,
        string address = "123 Test Street", string paymentMethod = "cash") =>
        new { serviceKey, startTime = startTime ?? DateTimeOffset.UtcNow.AddDays(2), durationHours,
            address, notes = "Test booking", paymentMethod,
            price = 1, userId = -1, cleanerId = 999, status = "Completed" };

    private static async Task<int> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/bookings", Booking());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Customer_books_cleaner_claims_and_customer_sees_accepted_order()
    {
        var customer = await Register();
        var cleaner = await Register("Cleaner");
        using var customerClient = fixture.Client(customer.Token);
        using var cleanerClient = fixture.Client(cleaner.Token);
        var login = await customerClient.PostAsJsonAsync("/api/auth/login", new { email = customer.Email.ToUpperInvariant(), password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var id = await Create(customerClient);
        var orders = await customerClient.GetFromJsonAsync<JsonElement[]>("/api/orders");
        var order = Assert.Single(orders!, o => o.GetProperty("id").GetInt32() == id);
        Assert.Equal(140000m, order.GetProperty("price").GetDecimal());
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        var available = await cleanerClient.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available");
        Assert.Contains(available!, j => j.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.OK, (await cleanerClient.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        var mine = await cleanerClient.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine");
        Assert.Contains(mine!, j => j.GetProperty("id").GetInt32() == id);
        orders = await customerClient.GetFromJsonAsync<JsonElement[]>("/api/orders");
        Assert.Equal("Accepted", Assert.Single(orders!, o => o.GetProperty("id").GetInt32() == id).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await customerClient.PutAsync($"/api/orders/{id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_claims_allow_exactly_one_cleaner()
    {
        using var customer = fixture.Client((await Register()).Token);
        var id = await Create(customer);
        var cleaners = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => fixture.Client((await Register("Cleaner")).Token)));
        try
        {
            var replies = await Task.WhenAll(cleaners.Select(c => c.PutAsync($"/api/cleaner/jobs/{id}/accept", null)));
            Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Equal(3, replies.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            var mine = await Task.WhenAll(cleaners.Select(c => c.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine")));
            Assert.Equal(1, mine.Sum(list => list!.Count(j => j.GetProperty("id").GetInt32() == id)));
        }
        finally { foreach (var client in cleaners) client.Dispose(); }
    }

    [Fact]
    public async Task Cancellation_preserves_history_checks_owner_and_prevents_claim()
    {
        using var owner = fixture.Client((await Register()).Token);
        using var other = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsync($"/api/orders/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/api/orders/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        var orders = await owner.GetFromJsonAsync<JsonElement[]>("/api/orders");
        Assert.Equal("Cancelled", Assert.Single(orders!, o => o.GetProperty("id").GetInt32() == id).GetProperty("status").GetString());
        var others = await other.GetFromJsonAsync<JsonElement[]>("/api/orders");
        Assert.DoesNotContain(others!, o => o.GetProperty("id").GetInt32() == id);
    }

    [Theory]
    [InlineData("unknown", 2, "123 Test", "cash", 1)]
    [InlineData("cleaning", 0, "123 Test", "cash", 1)]
    [InlineData("cleaning", 9, "123 Test", "cash", 1)]
    [InlineData("cleaning", 2, " ", "cash", 1)]
    [InlineData("cleaning", 2, "123 Test", "online", 1)]
    [InlineData("cleaning", 2, "123 Test", "cash", -1)]
    [InlineData("cleaning", 2, "123 Test", "cash", 365)]
    public async Task Invalid_bookings_are_rejected(string service, int hours, string address, string payment, int days)
    {
        using var client = fixture.Client((await Register()).Token);
        var response = await client.PostAsJsonAsync("/api/bookings", Booking(DateTimeOffset.UtcNow.AddDays(days), service, hours, address, payment));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
    }

    [Theory]
    [InlineData("cleaning", 70000)]
    [InlineData("cooking", 90000)]
    [InlineData("laundry", 70000)]
    [InlineData("childcare", 100000)]
    public async Task Server_ignores_client_price_and_uses_service_rate(string service, int rate)
    {
        using var client = fixture.Client((await Register()).Token);
        var response = await client.PostAsJsonAsync("/api/bookings", Booking(serviceKey: service, durationHours: 3));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(rate * 3m, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Customer_cannot_claim_and_can_register_as_cleaner_using_existing_token()
    {
        var account = await Register();
        using var client = fixture.Client(account.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        var registration = await client.PostAsJsonAsync("/api/profile/register-cleaner", new { bio = "", city = "Ha Noi", addressText = "", hourlyRate = 120000 });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var user = await registration.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cleaner", user.GetProperty("role").GetString());
        Assert.True(user.GetProperty("isCleaner").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/profile/register-cleaner", new { bio = "", city = "", addressText = "", hourlyRate = 0 })).StatusCode);
    }

    [Fact]
    public async Task Authorization_registration_and_cors_are_restricted()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/bookings", Booking())).StatusCode);
        var admin = await Register("Admin");
        using var adminClient = fixture.Client(admin.Token);
        Assert.Equal("Customer", (await adminClient.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/google", new { idToken = "invalid" })).StatusCode);
        using var disallowed = new HttpRequestMessage(HttpMethod.Options, "/api/orders");
        disallowed.Headers.Add("Origin", "https://untrusted.example");
        disallowed.Headers.Add("Access-Control-Request-Method", "GET");
        Assert.False((await client.SendAsync(disallowed)).Headers.Contains("Access-Control-Allow-Origin"));
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/orders");
        allowed.Headers.Add("Origin", "http://localhost:3000");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        Assert.Equal("http://localhost:3000", Assert.Single((await client.SendAsync(allowed)).Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Claim_racing_cancellation_never_produces_cancelled_assigned_booking()
    {
        using var owner = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        for (var i = 0; i < 5; i++)
        {
            var id = await Create(owner);
            var responses = await Task.WhenAll(owner.PutAsync($"/api/orders/{id}/cancel", null), cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null));
            Assert.Single(responses, r => r.IsSuccessStatusCode);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            using var scope = fixture.Api.Services.CreateScope();
            var booking = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(b => b.Id == id);
            Assert.True((booking.Status == "Accepted" && booking.CleanerId != null) || (booking.Status == "Cancelled" && booking.CleanerId == null));
        }
    }

    [Fact]
    public async Task Cleaner_portal_registers_logs_in_and_claims_through_authenticated_api()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var portal = new HttpClient(handler) { BaseAddress = fixture.PortalUrl };
        Assert.Equal(HttpStatusCode.Redirect, (await portal.GetAsync("/Booking/List")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsync("/Booking/Accept", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1" }))).StatusCode);
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        async Task<HttpResponseMessage> Form(string path, Dictionary<string, string> data)
        {
            var html = await portal.GetStringAsync(path);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);
            data["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
            return await portal.PostAsync(path, new FormUrlEncodedContent(data));
        }
        var register = await Form("/Auth/Register", new() { ["FullName"] = "Portal Cleaner", ["Email"] = email, ["Password"] = Password, ["ConfirmPassword"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.Contains("Portal Cleaner", await portal.GetStringAsync("/Dashboard/Index"));
        await portal.GetAsync("/Auth/Logout");
        var login = await Form("/Auth/Login", new() { ["Email"] = email, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var customer = fixture.Client((await Register()).Token);
        var id = await Create(customer);
        var list = await portal.GetStringAsync("/Booking/List");
        Assert.Contains("CM" + id, list);
        var token = Regex.Match(list, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var accept = await portal.PostAsync("/Booking/Accept", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["id"] = id.ToString(), ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) }));
        Assert.Equal(HttpStatusCode.Redirect, accept.StatusCode);
        var orders = await customer.GetFromJsonAsync<JsonElement[]>("/api/orders");
        Assert.Equal("Accepted", Assert.Single(orders!, o => o.GetProperty("id").GetInt32() == id).GetProperty("status").GetString());
    }
}
