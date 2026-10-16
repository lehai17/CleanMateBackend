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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public string AdminToken { get; private set; } = "";

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
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                ["RateLimits:Login:PermitLimit"] = "10000",
                ["RateLimits:Registration:PermitLimit"] = "10000",
                // Stale deployment settings must never reactivate online payments.
                ["Payments:Momo:Enabled"] = "true", ["Payments:PayOs:Enabled"] = "true"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        });
        using (var scope = Api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            await CleanMate.Api.Services.AdminBootstrap.CreateAsync(db, "admin@integration.test", "CleanMate-admin-test-123!", "Test Administrator");
        }
        await Api.StartAsync();
        using (var client = Client())
            AdminToken = (await (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@integration.test", password = "CleanMate-admin-test-123!" }))
                .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
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

    private async Task<WebApplication> LimitedAuthHost(int limit=2,int seconds=300,bool trustLoopback=true)
    {
        var host=ApiApplication.Build([],builder=>{
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(fixture.Api.Configuration.AsEnumerable());
            var settings=new Dictionary<string,string?>();
            foreach(var name in new[]{"Login","Registration","Google"}){
                settings[$"RateLimits:{name}:PermitLimit"]=limit.ToString();settings[$"RateLimits:{name}:WindowSeconds"]=seconds.ToString();
            }
            if(!trustLoopback)settings["Proxy:KnownProxies:0"]="192.0.2.200";
            builder.Configuration.AddInMemoryCollection(settings);builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        });
        await host.StartAsync();return host;
    }

    [Theory]
    [InlineData("login",HttpStatusCode.Unauthorized)]
    [InlineData("register",HttpStatusCode.BadRequest)]
    [InlineData("google",HttpStatusCode.BadRequest)]
    public async Task Public_auth_quotas_are_independent_per_endpoint_and_client_ip(string endpoint,HttpStatusCode initial)
    {
        await using var host=await LimitedAuthHost();using var client=new HttpClient{BaseAddress=new Uri(host.Urls.Single())};
        client.DefaultRequestHeaders.Add("X-Forwarded-For","198.51.100.21");
        object Input()=>endpoint switch {"register"=>new{fullName="",email=Guid.NewGuid()+"@example.test",password=Password},"google"=>new{idToken=""},_=>new{email=Guid.NewGuid()+"@example.test",password=Password}};
        for(var i=0;i<2;i++)Assert.Equal(initial,(await client.PostAsJsonAsync($"/api/auth/{endpoint}",Input())).StatusCode);
        var limited=await client.PostAsJsonAsync($"/api/auth/{endpoint}",Input());Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);
        var body=await limited.Content.ReadFromJsonAsync<JsonElement>();var retry=body.GetProperty("retryAfterSeconds").GetInt32();Assert.InRange(retry,1,300);Assert.Equal(retry.ToString(),limited.Headers.GetValues("Retry-After").Single());Assert.DoesNotContain(Password,body.ToString());
        var otherEndpoint=endpoint=="login"?"register":"login";
        Assert.NotEqual(HttpStatusCode.TooManyRequests,(await client.PostAsJsonAsync($"/api/auth/{otherEndpoint}",new{fullName="",email="",password=""})).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");client.DefaultRequestHeaders.Add("X-Forwarded-For","198.51.100.22");
        Assert.Equal(initial,(await client.PostAsJsonAsync($"/api/auth/{endpoint}",Input())).StatusCode);
        using var authorized=new HttpClient{BaseAddress=client.BaseAddress};authorized.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK,(await authorized.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Untrusted_forwarding_headers_cannot_bypass_auth_limits()
    {
        await using var host=await LimitedAuthHost(trustLoopback:false);using var client=new HttpClient{BaseAddress=new Uri(host.Urls.Single())};
        for(var i=0;i<3;i++){
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");client.DefaultRequestHeaders.Add("X-Forwarded-For",$"198.51.100.{i+1}");
            var response=await client.PostAsJsonAsync("/api/auth/login",new{email="not-an-account@example.test",password=Password});
            Assert.Equal(i<2?HttpStatusCode.Unauthorized:HttpStatusCode.TooManyRequests,response.StatusCode);
        }
    }

    [Fact]
    public async Task Auth_quota_expires_without_locking_or_changing_the_account()
    {
        var account=await Register();await using var host=await LimitedAuthHost(1,1);using var client=new HttpClient{BaseAddress=new Uri(host.Urls.Single())};
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/auth/login",new{email=account.Email,password="incorrect password"})).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await client.PostAsJsonAsync("/api/auth/login",new{email=account.Email,password=Password})).StatusCode);
        await Task.Delay(2200);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/login",new{email=account.Email,password=Password})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var user=(await db.Users.FindAsync(account.Id))!;Assert.True(user.IsActive);Assert.Equal(0,user.TokenVersion);
    }

    [Fact]
    public async Task Registration_rejects_overlong_passwords_and_accepts_72_utf8_bytes()
    {
        using var client=fixture.Client();
        foreach(var password in new[]{new string('x',73),new string('ệ',25)}){
            var email=Guid.NewGuid()+"@example.test";
            var result=await client.PostAsJsonAsync("/api/auth/register",new{fullName="Password Test",email,password});Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);
            using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.False(await db.Users.AnyAsync(u=>u.Email==email));
        }
        var validEmail=Guid.NewGuid()+"@example.test";var valid=new string('ệ',24);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/register",new{fullName="Password Test",email=validEmail,password=valid})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/login",new{email=validEmail,password=valid})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/auth/login",new{email=validEmail,password=new string('x',129)})).StatusCode);
    }

    [Fact]
    public async Task Staff_and_admin_creation_reject_unrepresentable_bcrypt_passwords()
    {
        using var admin=fixture.Client(fixture.AdminToken);
        foreach(var password in new[]{new string('x',73),new string('ệ',25)}){
            var email=Guid.NewGuid()+"@example.test";
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync("/api/management/staff",new{fullName="Password Staff",email,password})).StatusCode);
            using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>CleanMate.Api.Services.AdminBootstrap.CreateAsync(db,email,password,"Password Admin"));
            Assert.False(await db.Users.AnyAsync(u=>u.Email==email));
        }
    }

    [Fact]
    public async Task Logout_all_revokes_only_own_sessions_without_changing_password()
    {
        var account=await Register();var other=await Register();using var client=fixture.Client(account.Token);using var outsider=fixture.Client(other.Token);using var guest=fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.PostAsync("/api/auth/logout-all",null)).StatusCode);
        var login=await (await guest.PostAsJsonAsync("/api/auth/login",new {email=account.Email,password=Password})).Content.ReadFromJsonAsync<JsonElement>();using var second=fixture.Client(login.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/logout-all",new {userId=other.Id})).StatusCode);
        foreach(var session in new[]{client,second})Assert.Equal(HttpStatusCode.Unauthorized,(await session.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await outsider.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/login",new {email=account.Email,password=Password})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user=(await db.Users.FindAsync(account.Id))!;Assert.Equal(1,user.TokenVersion);Assert.True(BCrypt.Net.BCrypt.Verify(Password,user.PasswordHash));
        Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.ActorId==account.Id&&a.Action=="LogoutAll"));Assert.Equal(1,await db.Notifications.CountAsync(n=>n.UserId==account.Id&&n.EventKey==$"LogoutAll:{account.Id}:1"));
    }

    [Fact]
    public async Task Concurrent_logout_all_requests_revoke_once()
    {
        var account=await Register();using var client=fixture.Client(account.Token);
        var responses=await Task.WhenAll(client.PostAsync("/api/auth/logout-all",null),client.PostAsync("/api/auth/logout-all",null));
        Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(responses,r=>r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Conflict);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1,(await db.Users.FindAsync(account.Id))!.TokenVersion);Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.ActorId==account.Id&&a.Action=="LogoutAll"));
    }

    [Fact]
    public async Task Google_only_account_can_revoke_sessions_without_a_local_password()
    {
        var account=await Register();using var client=fixture.Client(account.Token);
        using(var scope=fixture.Api.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var user=(await db.Users.FindAsync(account.Id))!;user.PasswordHash="";await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsync("/api/auth/logout-all",null)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/me")).StatusCode);
        using var verification=fixture.Api.Services.CreateScope();var database=verification.ServiceProvider.GetRequiredService<AppDbContext>();var updated=(await database.Users.FindAsync(account.Id))!;
        Assert.Equal("",updated.PasswordHash);Assert.Equal(1,updated.TokenVersion);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Cleaner")]
    [InlineData("Staff")]
    [InlineData("Admin")]
    public async Task Password_change_revokes_every_old_session_and_requires_new_password(string role)
    {
        (string Token,int Id,string Email) account;
        var oldPassword=role=="Staff"?"Staff-integration-password-123!":Password;
        if(role=="Admin")
        {
            var email=Guid.NewGuid().ToString("N")+"@example.test";
            using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CleanMate.Api.Services.AdminBootstrap.CreateAsync(db,email,Password,"Password Admin");
            using var guest=fixture.Client();var auth=await (await guest.PostAsJsonAsync("/api/auth/login",new {email,password=Password})).Content.ReadFromJsonAsync<JsonElement>();
            account=(auth.GetProperty("token").GetString()!,auth.GetProperty("user").GetProperty("id").GetInt32(),email);
        }
        else account=role=="Staff"?await Staff():await Register(role);
        using var client=fixture.Client(account.Token);using var visitor=fixture.Client();
        Assert.Equal(role,(await client.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("role").GetString());
        var auth2=await (await visitor.PostAsJsonAsync("/api/auth/login",new {email=account.Email,password=oldPassword})).Content.ReadFromJsonAsync<JsonElement>();
        var token2=auth2.GetProperty("token").GetString()!;Assert.NotEqual(account.Token,token2);using var other=fixture.Client(token2);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword="incorrect password",newPassword="CleanMate-new-password-456!"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=oldPassword,newPassword="CleanMate-new-password-456!"})).StatusCode);
        foreach(var session in new[]{client,other})Assert.Equal(HttpStatusCode.Unauthorized,(await session.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await visitor.PostAsJsonAsync("/api/auth/login",new {email=account.Email,password=oldPassword})).StatusCode);
        var fresh=await visitor.PostAsJsonAsync("/api/auth/login",new {email=account.Email,password="CleanMate-new-password-456!"});Assert.Equal(HttpStatusCode.OK,fresh.StatusCode);
        using var scope2=fixture.Api.Services.CreateScope();var database=scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1,(await database.Users.FindAsync(account.Id))!.TokenVersion);
        var audit=await database.AdminAudits.SingleAsync(a=>a.ActorId==account.Id&&a.Action=="ChangePassword");Assert.DoesNotContain(oldPassword,audit.Details);Assert.DoesNotContain("CleanMate-new-password-456!",audit.Details);
        Assert.Equal(1,await database.Notifications.CountAsync(n=>n.UserId==account.Id&&n.EventKey==$"PasswordChanged:{account.Id}:1"));
    }

    [Fact]
    public async Task Password_validation_and_google_only_accounts_do_not_mutate_credentials()
    {
        var account=await Register();using var client=fixture.Client(account.Token);using var guest=fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="CleanMate-new-password-456!"})).StatusCode);
        foreach(var next in new[]{Password,"short",new string('x',73),new string('ệ',25),new string(' ',12)})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword=next})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user=(await db.Users.FindAsync(account.Id))!;Assert.Equal(0,user.TokenVersion);user.PasswordHash="";await db.SaveChangesAsync();
        var response=await client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="CleanMate-new-password-456!"});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Contains("Google",await response.Content.ReadAsStringAsync());
        Assert.False(await db.AdminAudits.AnyAsync(a=>a.ActorId==account.Id&&a.Action=="ChangePassword"));
    }

    [Fact]
    public async Task Concurrent_password_changes_have_one_winner_and_one_audit()
    {
        var account=await Register();using var client=fixture.Client(account.Token);
        var responses=await Task.WhenAll(client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="New-password-first-123!"}),client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="New-password-second-123!"}));
        Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(responses,r=>r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Conflict);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1,(await db.Users.FindAsync(account.Id))!.TokenVersion);Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.ActorId==account.Id&&a.Action=="ChangePassword"));
    }

    [Fact]
    public async Task Password_changes_limit_attempts_per_account()
    {
        var account=await Register();using var client=fixture.Client(account.Token);
        for(var i=0;i<8;i++)Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="short"})).StatusCode);
        var limited=await client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="New-password-test-123!"});Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.TooManyRequests,(await client.PostAsync("/api/auth/logout-all",null)).StatusCode);
        using var other=fixture.Client((await Register()).Token);Assert.Equal(HttpStatusCode.BadRequest,(await other.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="short"})).StatusCode);
    }

    [Fact]
    public async Task Legacy_tokens_work_only_before_first_password_change_and_malformed_versions_fail()
    {
        var account=await Register();
        string Token(string? version)
        {
            var claims=new List<System.Security.Claims.Claim>{new("sub",account.Id.ToString())};
            if(version is not null)claims.Add(new("token_version",version));
            var jwt=new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("CleanMateTests","CleanMateTests",claims,expires:DateTime.UtcNow.AddHours(1),signingCredentials:new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("cleanmate-integration-tests-only-secret-at-least-32-bytes")),Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
            return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
        }
        using var legacy=fixture.Client(Token(null));Assert.Equal(HttpStatusCode.OK,(await legacy.GetAsync("/api/me")).StatusCode);
        using var malformed=fixture.Client(Token("bad"));Assert.Equal(HttpStatusCode.Unauthorized,(await malformed.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await legacy.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword="New-legacy-password-123!"})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await legacy.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Password_change_accepts_full_72_byte_values_without_truncating_unicode()
    {
        var account=await Register();using var client=fixture.Client(account.Token);using var guest=fixture.Client();var next=new string('x',72);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=Password,newPassword=next})).StatusCode);
        var auth=await (await guest.PostAsJsonAsync("/api/auth/login",new {email=account.Email,password=next})).Content.ReadFromJsonAsync<JsonElement>();using var fresh=fixture.Client(auth.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK,(await fresh.PostAsJsonAsync("/api/auth/change-password",new {currentPassword=next,newPassword=new string('ệ',24)})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/login",new {email=account.Email,password=new string('ệ',24)})).StatusCode);
    }

    [Fact]
    public async Task Support_only_booking_parties_can_report_and_reports_are_private()
    {
        using var owner = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        using var outsider = fixture.Client((await Register()).Token);
        using var guest = fixture.Client();
        var id = await Create(owner);
        object Report() => new { bookingId=id,category="Scheduling",description="Không thể liên hệ vào giờ đã hẹn.",requestId=Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync("/api/support")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await outsider.PostAsJsonAsync("/api/support",Report())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await cleaner.PostAsJsonAsync("/api/support",Report())).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept",null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await owner.PostAsJsonAsync("/api/support",Report())).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await cleaner.PostAsJsonAsync("/api/support",Report())).StatusCode);
        foreach(var client in new[]{owner,cleaner})
        {
            var result=await client.GetFromJsonAsync<JsonElement>("/api/support");
            Assert.Equal(1,result.GetProperty("total").GetInt32());
            Assert.Equal(id,result.GetProperty("items")[0].GetProperty("bookingId").GetInt32());
        }
        Assert.Equal(0,(await outsider.GetFromJsonAsync<JsonElement>("/api/support")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync("/api/management/support")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.GetAsync("/api/support?page=0")).StatusCode);
    }

    [Fact]
    public async Task Support_concurrent_retries_create_one_ticket_and_one_notification()
    {
        var account=await Register();using var client=fixture.Client(account.Token);var id=await Create(client);var key=Guid.NewGuid();
        var payload=new {bookingId=id,category="CashPayment",description="Cần kiểm tra khoản tiền mặt sau ca.",requestId=key};
        var responses=await Task.WhenAll(client.PostAsJsonAsync("/api/support",payload),client.PostAsJsonAsync("/api/support",payload));
        Assert.All(responses,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode));
        var first=(await responses[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(first,(await responses[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/support",new {bookingId=id,category="Other",description="Nội dung thay đổi của cùng một mã.",requestId=key})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1,await db.SupportTickets.CountAsync(t=>t.ReporterId==account.Id));
        Assert.Equal(1,await db.Notifications.CountAsync(n=>n.UserId==account.Id&&n.EventKey==$"SupportCreated:{first}"));
    }

    [Fact]
    public async Task Support_staff_resolution_is_versioned_audited_and_does_not_change_cash_or_booking()
    {
        var account=await Register();using var owner=fixture.Client(account.Token);using var staff=fixture.Client((await Staff()).Token);
        var bookingId=await Create(owner);
        var create=await owner.PostAsJsonAsync("/api/support",new {bookingId,category="CashPayment",description="Cần được giải thích về tiền mặt.",requestId=Guid.NewGuid()});
        var id=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();var path=$"/api/management/support/{id}";
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.PutAsJsonAsync(path,new {status="Resolved",response="Fake resolution",version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await staff.PutAsJsonAsync(path,new {status="Resolved",response="",version=1})).StatusCode);
        var response=new string('ệ',2000);
        var attempts=await Task.WhenAll(staff.PutAsJsonAsync(path,new {status="Resolved",response,version=1}),staff.PutAsJsonAsync(path,new {status="InProgress",response,version=1}));
        Assert.Single(attempts,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(attempts,r=>r.StatusCode==HttpStatusCode.Conflict);
        var result=(await owner.GetFromJsonAsync<JsonElement>("/api/support")).GetProperty("items")[0];Assert.Equal(response,result.GetProperty("response").GetString());
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.Target==$"Support:{id}"));
        Assert.Equal(1,await db.Notifications.CountAsync(n=>n.UserId==account.Id&&n.EventKey==$"SupportUpdated:{id}:2"));
        Assert.Equal("Pending",(await db.Bookings.FindAsync(bookingId))!.Status);Assert.False(await db.Payments.AnyAsync(p=>p.BookingId==bookingId));
        Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync("/api/management/support?status=Unknown")).StatusCode);
    }

    [Fact]
    public async Task Support_rejects_invalid_payloads_before_creating_any_report()
    {
        using var client=fixture.Client((await Register()).Token);var bookingId=await Create(client);
        foreach(var description in new[]{"short",new string('x',2001)})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/support",new {bookingId,category="Other",description,requestId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/support",new {bookingId,category="Other",description="A valid description",requestId=Guid.Empty})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/support",new {bookingId,category="Fake",description="A valid description",requestId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>("/api/support")).GetProperty("total").GetInt32());
    }

    [Fact]
    public void Sql_model_matches_existing_migrations()
    {
        using var scope = fixture.Api.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.HasPendingModelChanges());
    }

    [Fact]
    public void Postgres_model_matches_migrations_and_generates_native_schema_script()
    {
        using var db = new PostgresDesignTimeFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("ADD \"Address\" text", script);
        Assert.Contains("ADD \"PhoneNumber\" text", script);
        Assert.Contains("ADD \"Status\" text", script);
        Assert.DoesNotContain("nvarchar", script);
        Assert.DoesNotContain("ALTER COLUMN \"Price\"", script);
    }

    private async Task<(string Token, int Id, string Email)> Register(string role = "Customer")
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        using var client = fixture.Client();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { fullName = "Integration User", email, password = Password, role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (role == "Cleaner")
        {
            using var admin = fixture.Client(fixture.AdminToken);
            var profile = auth.GetProperty("user").GetProperty("cleanerProfile").GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/management/applications/{profile}/review", new { status = "Approved" })).StatusCode);
        }
        return (auth.GetProperty("token").GetString()!, auth.GetProperty("user").GetProperty("id").GetInt32(), email);
    }

    private static object Booking(object? startTime = null, string serviceKey = "cleaning", int durationHours = 2,
        string address = "123 Test Street", string paymentMethod = "cash", string? contactName = null, string? contactPhone = null,
        Guid? requestId = null) =>
        new { serviceKey, startTime = startTime ?? DateTimeOffset.UtcNow.AddDays(2), durationHours,
            address, notes = "Test booking", paymentMethod, contactName, contactPhone, requestId,
            price = 1, userId = -1, cleanerId = 999, status = "Completed",
            startedAt = "2000-01-01T00:00:00Z", completedAt = "2000-01-01T00:00:00Z" };

    private static async Task<int> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/bookings", Booking());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateAt(HttpClient client, DateTimeOffset start, int hours = 2)
    {
        var response = await client.PostAsJsonAsync("/api/bookings", Booking(startTime: start, durationHours: hours));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task<(string Token, int Id, string Email)> Staff()
    {
        using var admin = fixture.Client(fixture.AdminToken);
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var created = await admin.PostAsJsonAsync("/api/management/staff", new { fullName = "Integration Staff", email, password = "Staff-integration-password-123!" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using var guest = fixture.Client();
        var login = await guest.PostAsJsonAsync("/api/auth/login", new { email, password = "Staff-integration-password-123!" });
        return ((await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!, id, email);
    }

    [Fact]
    public async Task Public_registration_never_grants_staff_and_management_uses_server_permissions()
    {
        using var guest = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/management/dashboard")).StatusCode);
        using var customer = fixture.Client((await Register("Staff")).Token);
        Assert.Equal("Customer", (await customer.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("role").GetString());
        foreach (var path in new[] { "dashboard", "applications", "orders", "services", "staff", "audit" })
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/management/" + path)).StatusCode);
        using var staff = fixture.Client((await Staff()).Token);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/management/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/management/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/management/staff", new { fullName = "Fake", email = "fake@example.test", password = "Fake-password-123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/management/services/cleaning", new
        { name = "Cleaning", hourlyRate = 99000, minHours = 1, maxHours = 8, isActive = true, version = 1 })).StatusCode);
    }

    [Fact]
    public async Task Staff_requests_more_information_and_approval_is_atomic_with_notification_and_audit()
    {
        using var guest = fixture.Client();
        var response = await guest.PostAsJsonAsync("/api/auth/register", new { fullName = "Pending Applicant", email = Guid.NewGuid().ToString("N") + "@example.test", password = Password, role = "Cleaner" });
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        var profileId = auth.GetProperty("user").GetProperty("cleanerProfile").GetProperty("id").GetInt32();
        using var applicant = fixture.Client(auth.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.PutAsJsonAsync("/api/cleaner/profile", new { fullName = "Bypass", phoneNumber = "0901234567", city = "Ha Noi", bio = "", addressText = "", hourlyRate = 1 })).StatusCode);
        using var staff = fixture.Client((await Staff()).Token);
        var path = $"/api/management/applications/{profileId}/review";
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync(path, new { status = "NeedsInfo" })).StatusCode);
        var longReason = new string('ệ', 1000);
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync(path, new { status = "NeedsInfo", reason = longReason })).StatusCode);
        var pendingMe = await applicant.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(longReason, pendingMe.GetProperty("cleanerProfile").GetProperty("reviewReason").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        var resubmitted = await applicant.PutAsJsonAsync("/api/profile/cleaner-application", new { city = "Ha Noi", bio = "Updated experience", addressText = "Contact address", hourlyRate = 120000 });
        Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
        var reviews = await Task.WhenAll(staff.PutAsJsonAsync(path, new { status = "Approved" }), staff.PutAsJsonAsync(path, new { status = "Approved" }));
        Assert.Single(reviews, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(reviews, r => r.StatusCode == HttpStatusCode.Conflict);
        var me = await applicant.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("Cleaner", me.GetProperty("role").GetString());
        Assert.True(me.GetProperty("isCleaner").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await applicant.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        var notices = await applicant.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(2, notices.GetProperty("items").GetArrayLength());
        Assert.All(notices.GetProperty("items").EnumerateArray(), n => Assert.Equal("Profile", n.GetProperty("target").GetString()));
        var audit = await staff.GetFromJsonAsync<JsonElement[]>("/api/management/audit");
        Assert.Equal(2, audit!.Count(a => a.GetProperty("target").GetString() == $"Cleaner:{profileId}"));
        var needsInfoAudit = audit!.Single(a => a.GetProperty("target").GetString() == $"Cleaner:{profileId}" &&
            a.GetProperty("details").GetString()!.Contains("NeedsInfo"));
        using var details = JsonDocument.Parse(needsInfoAudit.GetProperty("details").GetString()!);
        Assert.Equal(longReason, details.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Locked_staff_sessions_are_rejected_immediately_and_audit_does_not_contain_passwords()
    {
        var account = await Staff();
        using var staff = fixture.Client(account.Token);
        using var admin = fixture.Client(fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/management/staff/{account.Id}/access", new { isActive = false, expectedActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/management/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/me")).StatusCode);
        var audit = await admin.GetStringAsync("/api/management/audit");
        Assert.DoesNotContain("Staff-integration-password-123!", audit);
        Assert.DoesNotContain("PasswordHash", audit);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/management/staff/{account.Id}/access", new { isActive = true, expectedActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/management/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/management/staff/{account.Id}/access", new { isActive = false, expectedActive = false })).StatusCode);
    }

    [Fact]
    public async Task Management_pricing_uses_version_checks_and_preserves_existing_booking_quotes()
    {
        using var admin = fixture.Client(fixture.AdminToken);
        using var customer = fixture.Client((await Register()).Token);
        var original = (await admin.GetFromJsonAsync<JsonElement[]>("/api/management/services"))!.Single(s => s.GetProperty("key").GetString() == "cooking");
        var before = await customer.PostAsJsonAsync("/api/bookings", Booking(serviceKey: "cooking"));
        var id = (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var version = original.GetProperty("version").GetInt32();
        try
        {
            var change = new { name = "Nấu ăn cập nhật", hourlyRate = 98000, minHours = 2, maxHours = 4, isActive = true, version };
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/management/services/cooking", change)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/management/services/cooking", change)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/bookings", Booking(serviceKey: "cooking", durationHours: 1))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/bookings", Booking(serviceKey: "cooking", durationHours: 5))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync("/api/bookings", new { serviceKey = "cooking", startTime = DateTimeOffset.UtcNow.AddDays(2), durationHours = 2, address = "Test", paymentMethod = "cash", expectedPrice = 180000 })).StatusCode);
            var after = await customer.PostAsJsonAsync("/api/bookings", Booking(serviceKey: "cooking"));
            Assert.Equal(196000, (await after.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("price").GetDecimal());
            Assert.Equal(180000, (await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!.Single(b => b.GetProperty("id").GetInt32() == id).GetProperty("price").GetDecimal());
            var disabled = new { name = "Nấu ăn cập nhật", hourlyRate = 98000, minHours = 2, maxHours = 4, isActive = false, version = version + 1 };
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/management/services/cooking", disabled)).StatusCode);
            Assert.DoesNotContain((await customer.GetFromJsonAsync<JsonElement[]>("/api/services"))!, s => s.GetProperty("key").GetString() == "cooking");
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/bookings", Booking(serviceKey: "cooking"))).StatusCode);
        }
        finally
        {
            var current = (await admin.GetFromJsonAsync<JsonElement[]>("/api/management/services"))!.Single(s => s.GetProperty("key").GetString() == "cooking");
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/management/services/cooking", new {
                name = original.GetProperty("name").GetString(), hourlyRate = original.GetProperty("hourlyRate").GetDecimal(),
                minHours = original.GetProperty("minHours").GetInt32(), maxHours = original.GetProperty("maxHours").GetInt32(),
                isActive = original.GetProperty("isActive").GetBoolean(), version = current.GetProperty("version").GetInt32()
            })).StatusCode);
        }
    }

    [Fact]
    public async Task Staff_can_find_real_orders_and_dashboard_reports_service_value_not_platform_revenue()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var staff = fixture.Client((await Staff()).Token);
        var id = await Create(customer);
        var result = await staff.GetFromJsonAsync<JsonElement>($"/api/management/orders?search=CM{id}");
        var row = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(id, row.GetProperty("id").GetInt32());
        var dashboard = await staff.GetFromJsonAsync<JsonElement>("/api/management/dashboard");
        Assert.True(dashboard.GetProperty("totalOrders").GetInt32() > 0);
        Assert.True(dashboard.TryGetProperty("completedServiceValue", out _));
        Assert.False(dashboard.TryGetProperty("revenue", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/management/orders?page=0")).StatusCode);
    }

    [Fact]
    public async Task Bootstrap_refuses_to_promote_a_public_account_and_does_not_reset_existing_admins()
    {
        var customer = await Register();
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CleanMate.Api.Services.AdminBootstrap.CreateAsync(db, customer.Email, "Bootstrap-password-123!", "Administrator"));
        await CleanMate.Api.Services.AdminBootstrap.CreateAsync(db, "admin@integration.test", "Another-password-123!", "Administrator");
        using var admin = fixture.Client(fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/management/dashboard")).StatusCode);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "admin@integration.test"));
    }

    [Fact]
    public async Task Management_migration_preserves_operating_legacy_cleaners_without_auto_approving_new_applications()
    {
        using var scope = fixture.Api.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = new SqlConnectionStringBuilder(source.Database.GetConnectionString()) { InitialCatalog = "CleanMateTestsLegacy_" + Guid.NewGuid().ToString("N") };
        await using var db = new AppDbContextSqlServer(new DbContextOptionsBuilder<AppDbContextSqlServer>().UseSqlServer(connection.ConnectionString).Options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261016030621_AddNotifications");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO [Users] ([FullName],[Email],[PasswordHash],[Role]) VALUES ('Legacy Cleaner','legacy@example.test','test-only-unusable-hash',1); DECLARE @id int = SCOPE_IDENTITY(); INSERT INTO [CleanerProfiles] ([UserId],[Bio],[HourlyRate],[AvgRating],[Latitude],[Longitude],[City],[AddressText]) VALUES (@id,'Legacy',120000,0,0,0,'Ha Noi','Test address');");
            await db.Database.MigrateAsync();
            Assert.Equal("Approved", (await db.CleanerProfiles.SingleAsync()).ReviewStatus);
            Assert.True((await db.Users.SingleAsync()).IsActive);
            var user = new CleanMate.Api.Domain.Entities.User { FullName = "New Applicant", Email = "new@applicant.test" };
            db.Users.Add(user); await db.SaveChangesAsync();
            db.CleanerProfiles.Add(new CleanMate.Api.Domain.Entities.CleanerProfile { UserId = user.Id, SubmittedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            Assert.Equal("Pending", (await db.CleanerProfiles.AsNoTracking().SingleAsync(c => c.UserId == user.Id)).ReviewStatus);
        }
        finally
        {
            Assert.StartsWith("CleanMateTestsLegacy_", new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Notifications_follow_booking_lifecycle_and_cash_feedback_retries_do_not_duplicate_them()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        foreach (var action in new[] { "accept", "start", "complete" })
            Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        var cash = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => cleaner.PutAsync($"/api/cleaner/jobs/{id}/cash-payment", null)));
        Assert.All(cash, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync($"/api/orders/{id}/confirm-completion", new { rating = 5, review = "Good" })).StatusCode);
        var inbox = await customer.GetFromJsonAsync<JsonElement>("/api/notifications");
        var items = inbox.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(5, items.Length);
        Assert.Equal(5, inbox.GetProperty("unreadCount").GetInt32());
        Assert.Contains(items, n => n.GetProperty("title").GetString() == "Cleaner xác nhận đã nhận tiền mặt");
        Assert.All(items, n => {
            Assert.Equal(id, n.GetProperty("bookingId").GetInt32());
            Assert.Equal("Orders", n.GetProperty("target").GetString());
            Assert.EndsWith("Z", n.GetProperty("createdAt").GetString());
            Assert.Equal(JsonValueKind.Null, n.GetProperty("readAt").ValueKind);
        });
        var cleanerInbox = await cleaner.GetFromJsonAsync<JsonElement>("/api/notifications");
        var cleanerItems = cleanerInbox.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, cleanerItems.Length);
        Assert.Equal(2, cleanerItems.Count(n => n.GetProperty("target").GetString() == "CleanerJobs"));
        Assert.Contains(cleanerItems, n => n.GetProperty("title").GetString() == "Khách đã đánh giá ca làm");
        Assert.Equal(5, (await customer.GetFromJsonAsync<JsonElement>("/api/notifications/unread-count")).GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Notification_reads_are_owner_scoped_and_preserve_the_original_read_timestamp()
    {
        using var owner = fixture.Client((await Register()).Token);
        using var stranger = fixture.Client((await Register()).Token);
        using var guest = fixture.Client();
        var booking = await Create(owner);
        await owner.PutAsync($"/api/orders/{booking}/cancel", null);
        await Create(stranger);
        var inbox = await owner.GetFromJsonAsync<JsonElement>("/api/notifications");
        var id = inbox.GetProperty("items")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsync($"/api/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsync($"/api/notifications/{id}/read", null)).StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => owner.PutAsync($"/api/notifications/{id}/read", null)));
        var timestamps = new List<string>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
            timestamps.Add(saved.GetProperty("readAt").GetString()!);
            Assert.Equal(1, saved.GetProperty("unreadCount").GetInt32());
        }
        Assert.Single(timestamps.Distinct());
        Assert.EndsWith("Z", timestamps[0]);
        var otherInbox = await stranger.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(1, otherInbox.GetProperty("unreadCount").GetInt32());
        Assert.DoesNotContain(otherInbox.GetProperty("items").EnumerateArray(), n => n.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Notification_pagination_and_bulk_read_do_not_skip_older_items_or_mark_new_arrivals()
    {
        using var owner = fixture.Client((await Register()).Token);
        using var stranger = fixture.Client((await Register()).Token);
        for (var i = 0; i < 23; i++) await Create(owner);
        await Create(stranger);
        var first = await owner.GetFromJsonAsync<JsonElement>("/api/notifications");
        var rows = first.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(20, rows.Length);
        var throughId = rows[0].GetProperty("id").GetInt32();
        var cursor = first.GetProperty("nextBeforeId").GetInt32();
        await Create(owner);
        var second = await owner.GetFromJsonAsync<JsonElement>($"/api/notifications?beforeId={cursor}");
        var tail = second.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, tail.Length);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBeforeId").ValueKind);
        Assert.Equal(23, rows.Concat(tail).Select(n => n.GetProperty("id").GetInt32()).Distinct().Count());
        var response = await owner.PutAsJsonAsync("/api/notifications/read-all", new { throughId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unreadCount").GetInt32());
        var refreshed = await owner.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(JsonValueKind.Null, refreshed.GetProperty("items")[0].GetProperty("readAt").ValueKind);
        Assert.Equal(1, (await stranger.GetFromJsonAsync<JsonElement>("/api/notifications/unread-count")).GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Fresh_inbox_is_empty_and_rejects_invalid_cursors_and_read_boundaries()
    {
        using var customer = fixture.Client((await Register()).Token);
        var inbox = await customer.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Empty(inbox.GetProperty("items").EnumerateArray());
        Assert.Equal(0, inbox.GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.GetAsync("/api/notifications?beforeId=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PutAsJsonAsync("/api/notifications/read-all", new { throughId = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PutAsync("/api/notifications/2147483647/read", null)).StatusCode);
    }

    [Fact]
    public async Task Creation_rescheduling_and_cancellation_replays_do_not_duplicate_notifications()
    {
        using var customer = fixture.Client((await Register()).Token);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var payload = Booking(start, requestId: Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => customer.PostAsJsonAsync("/api/bookings", payload)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var id = (await responses[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync($"/api/orders/{id}/reschedule", new { startTime = start, expectedStartTime = start })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync($"/api/orders/{id}/reschedule", new { startTime = start.AddDays(1), expectedStartTime = start })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PutAsJsonAsync($"/api/orders/{id}/reschedule", new { startTime = start.AddDays(1), expectedStartTime = start })).StatusCode);
        for (var i = 0; i < 2; i++) await customer.PutAsync($"/api/orders/{id}/cancel", null);
        var inbox = await customer.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(3, inbox.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Notification_insert_failure_rolls_back_the_booking_transition_and_other_recipient_notification()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Fault injection only in this fixture's generated database, removed even if assertions fail.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [CK_Test_NotificationFailure] CHECK ([EventKey] NOT LIKE 'Accepted:%')");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
            Assert.Equal("Pending", Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!).GetProperty("status").GetString());
            Assert.DoesNotContain((await cleaner.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray(),
                n => n.GetProperty("bookingId").ValueKind != JsonValueKind.Null && n.GetProperty("bookingId").GetInt32() == id);
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Notifications] DROP CONSTRAINT [CK_Test_NotificationFailure]"); }
    }

    [Fact]
    public async Task Concurrent_booking_retries_create_one_order_with_the_same_receipt()
    {
        using var customer = fixture.Client((await Register()).Token);
        var payload = Booking(requestId: Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => customer.PostAsJsonAsync("/api/bookings", payload)));
        var ids = new List<int>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
            ids.Add(receipt.GetProperty("id").GetInt32());
            Assert.Equal(140000, receipt.GetProperty("price").GetDecimal());
        }
        Assert.Single(ids.Distinct());
        Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
    }

    [Fact]
    public async Task Owner_can_reschedule_pending_order_preserving_quote_contact_and_creation_request()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var payload = Booking(start, contactName: "Booking Contact", contactPhone: "0901234567", requestId: Guid.NewGuid());
        var created = await customer.PostAsJsonAsync("/api/bookings", payload);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var target = start.AddDays(1).ToOffset(TimeSpan.FromHours(7));
        var response = await customer.PutAsJsonAsync($"/api/orders/{id}/reschedule", new
        { startTime = target, expectedStartTime = start, price = 1, cleanerId = 999, durationHours = 8, status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(target.UtcDateTime, order.GetProperty("date").GetDateTime());
        Assert.EndsWith("Z", order.GetProperty("date").GetString());
        Assert.Equal(140000, order.GetProperty("price").GetDecimal());
        Assert.Equal("Booking Contact", order.GetProperty("contactName").GetString());
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("cleaner").ValueKind);
        var job = (await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available"))!.Single(b => b.GetProperty("id").GetInt32() == id);
        Assert.Equal(2, job.GetProperty("durationHours").GetInt32());
        Assert.Equal(target.UtcDateTime, job.GetProperty("startTime").GetDateTime());
        var replay = await customer.PostAsJsonAsync("/api/bookings", payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
    }

    [Fact]
    public async Task Rescheduling_requires_owner_and_an_existing_booking()
    {
        using var owner = fixture.Client((await Register()).Token);
        using var stranger = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        using var guest = fixture.Client();
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var id = await CreateAt(owner, start);
        var dto = new { startTime = start.AddDays(1), expectedStartTime = start };
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsJsonAsync($"/api/orders/{id}/reschedule", dto)).StatusCode);
        foreach (var client in new[] { stranger, cleaner })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/orders/{id}/reschedule", dto)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync("/api/orders/2147483647/reschedule", dto)).StatusCode);
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("InProgress")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task Rescheduling_is_rejected_after_assignment_or_cancellation(string status)
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var id = await CreateAt(customer, start);
        if (status == "Cancelled") await customer.PutAsync($"/api/orders/{id}/cancel", null);
        else
        {
            await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null);
            if (status is "InProgress" or "Completed") await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null);
            if (status == "Completed") await cleaner.PutAsync($"/api/cleaner/jobs/{id}/complete", null);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PutAsJsonAsync($"/api/orders/{id}/reschedule",
            new { startTime = start.AddDays(1), expectedStartTime = start })).StatusCode);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(start.UtcDateTime, order.GetProperty("date").GetDateTime());
        Assert.Equal(status, order.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(200)]
    public async Task Rescheduling_rejects_past_or_excessively_distant_dates(int days)
    {
        using var customer = fixture.Client((await Register()).Token);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var id = await CreateAt(customer, start);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PutAsJsonAsync($"/api/orders/{id}/reschedule",
            new { startTime = DateTimeOffset.UtcNow.AddDays(days), expectedStartTime = start })).StatusCode);
        Assert.Equal(start.UtcDateTime, Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!)
            .GetProperty("date").GetDateTime());
    }

    [Fact]
    public async Task Expired_pending_order_cannot_be_revived_by_rescheduling()
    {
        using var customer = fixture.Client((await Register()).Token);
        var id = await Create(customer);
        var expired = DateTime.UtcNow.AddHours(-1);
        using (var scope = fixture.Api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.Where(b => b.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.StartTime, expired));
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PutAsJsonAsync($"/api/orders/{id}/reschedule",
            new { startTime = DateTimeOffset.UtcNow.AddDays(1), expectedStartTime = expired })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_schedule_edits_from_the_same_old_date_allow_one_winner()
    {
        using var customer = fixture.Client((await Register()).Token);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var id = await CreateAt(customer, start);
        var responses = await Task.WhenAll(Enumerable.Range(1, 6).Select(offset => customer.PutAsJsonAsync(
            $"/api/orders/{id}/reschedule", new { startTime = start.AddDays(offset), expectedStartTime = start })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var winner = await responses.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>();
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(winner.GetProperty("date").GetDateTime(), order.GetProperty("date").GetDateTime());
    }

    [Fact]
    public async Task Cancellation_racing_rescheduling_never_resurrects_an_order()
    {
        using var customer = fixture.Client((await Register()).Token);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var id = await CreateAt(customer, start);
        var cancel = customer.PutAsync($"/api/orders/{id}/cancel", null);
        var edit = customer.PutAsJsonAsync($"/api/orders/{id}/reschedule", new { startTime = start.AddDays(1), expectedStartTime = start });
        await Task.WhenAll(cancel, edit);
        Assert.Equal(HttpStatusCode.NoContent, cancel.Result.StatusCode);
        Assert.Contains(edit.Result.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.Equal("Cancelled", Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Claim_racing_schedule_edit_cannot_assign_a_new_overlapping_interval()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var reserved = await CreateAt(customer, start);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{reserved}/accept", null)).StatusCode);
        for (var i = 0; i < 6; i++)
        {
            var original = start.AddHours(4 + i * 3);
            var id = await CreateAt(customer, original);
            var accept = cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null);
            var edit = customer.PutAsJsonAsync($"/api/orders/{id}/reschedule",
                new { startTime = start.AddHours(1), expectedStartTime = original });
            await Task.WhenAll(accept, edit);
            Assert.Contains(accept.Result.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            Assert.Contains(edit.Result.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            Assert.False(accept.Result.IsSuccessStatusCode && edit.Result.IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task Reusing_booking_request_with_changed_details_returns_conflict_without_modification()
    {
        using var customer = fixture.Client((await Register()).Token);
        var key = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow.AddDays(2);
        Assert.Equal(HttpStatusCode.OK, (await customer.PostAsJsonAsync("/api/bookings", Booking(start, requestId: key))).StatusCode);
        foreach (var changed in new[] { Booking(start, address: "Another address", requestId: key),
            Booking(start, durationHours: 3, requestId: key), Booking(start, contactName: "Another contact", requestId: key),
            Booking(start, serviceKey: "cooking", requestId: key), Booking(start.AddHours(1), requestId: key) })
            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync("/api/bookings", changed)).StatusCode);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("123 Test Street", order.GetProperty("address").GetString());
        Assert.Equal(140000, order.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Request_keys_are_scoped_to_owner_and_new_keys_allow_intentional_new_bookings()
    {
        using var first = fixture.Client((await Register()).Token);
        using var second = fixture.Client((await Register()).Token);
        var key = Guid.NewGuid();
        var payload = Booking(requestId: key);
        var a = await first.PostAsJsonAsync("/api/bookings", payload);
        var b = await second.PostAsJsonAsync("/api/bookings", payload);
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        Assert.NotEqual((await a.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32(),
            (await b.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/bookings", Booking(requestId: Guid.NewGuid()))).StatusCode);
        Assert.Equal(2, (await first.GetFromJsonAsync<JsonElement[]>("/api/orders"))!.Length);
        Assert.Single((await second.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
    }

    [Fact]
    public async Task Booking_retry_after_profile_change_and_cancellation_returns_existing_order_without_resurrection()
    {
        using var customer = fixture.Client((await Register()).Token);
        var payload = Booking(requestId: Guid.NewGuid());
        var first = await customer.PostAsJsonAsync("/api/bookings", payload);
        var receipt = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = receipt.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync("/api/profile", new
        { fullName = "Changed profile", phoneNumber = "0909999999" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await customer.PutAsync($"/api/orders/{id}/cancel", null)).StatusCode);
        var retry = await customer.PostAsJsonAsync("/api/bookings", payload);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var saved = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, saved.GetProperty("id").GetInt32());
        Assert.Equal("Cancelled", saved.GetProperty("status").GetString());
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("Integration User", order.GetProperty("contactName").GetString());
    }

    [Fact]
    public async Task Failed_booking_validation_does_not_consume_request_key_and_empty_key_is_rejected()
    {
        using var customer = fixture.Client((await Register()).Token);
        var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/bookings", Booking(requestId: Guid.Empty))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/bookings", Booking(durationHours: 0, requestId: key))).StatusCode);
        Assert.Empty((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(HttpStatusCode.OK, (await customer.PostAsJsonAsync("/api/bookings", Booking(requestId: key))).StatusCode);
        Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
    }

    [Fact]
    public async Task Older_clients_can_create_multiple_orders_without_request_keys()
    {
        using var customer = fixture.Client((await Register()).Token);
        var payload = Booking();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await customer.PostAsJsonAsync("/api/bookings", payload)).StatusCode);
        Assert.Equal(2, (await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!.Length);
    }

    [Fact]
    public async Task Booking_contact_is_preserved_independently_of_account_profile()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var response = await customer.PostAsJsonAsync("/api/bookings", Booking(
            contactName: "  Person at the house  ", contactPhone: "  +84 902 345 678  "));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync("/api/profile", new
        { fullName = "Changed account name", phoneNumber = "0909999999" })).StatusCode);
        var available = (await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available"))!
            .Single(job => job.GetProperty("id").GetInt32() == id);
        Assert.Equal("Person at the house", available.GetProperty("customerName").GetString());
        Assert.Equal("+84 902 345 678", available.GetProperty("customerPhone").GetString());
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        var mine = Assert.Single((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!);
        Assert.Equal("Person at the house", mine.GetProperty("customerName").GetString());
        Assert.Equal("+84 902 345 678", mine.GetProperty("customerPhone").GetString());
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("Person at the house", order.GetProperty("contactName").GetString());
        Assert.Equal("+84 902 345 678", order.GetProperty("contactPhone").GetString());
    }

    [Fact]
    public async Task Older_clients_snapshot_profile_contact_including_an_absent_phone()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync("/api/profile", new
        { fullName = "New name", phoneNumber = "0909999999" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        var mine = Assert.Single((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!);
        Assert.Equal("Integration User", mine.GetProperty("customerName").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("customerPhone").ValueKind);
    }

    [Fact]
    public async Task Legacy_bookings_without_contact_fields_keep_profile_fallback()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        using (var scope = fixture.Api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.Where(b => b.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.ContactName, (string?)null)
                    .SetProperty(b => b.ContactPhone, (string?)null));
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync("/api/profile", new
        { fullName = "Legacy contact", phoneNumber = "0901234567" })).StatusCode);
        var job = (await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available"))!
            .Single(b => b.GetProperty("id").GetInt32() == id);
        Assert.Equal("Legacy contact", job.GetProperty("customerName").GetString());
        Assert.Equal("0901234567", job.GetProperty("customerPhone").GetString());
    }

    [Fact]
    public async Task Booking_contact_length_limits_reject_oversized_input_and_accept_boundaries()
    {
        using var customer = fixture.Client((await Register()).Token);
        foreach (var input in new[] { Booking(contactName: new string('x', 201), contactPhone: "0901234567"),
            Booking(contactName: "Contact", contactPhone: new string(' ', 31) + "0901234567") })
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/bookings", input)).StatusCode);
        Assert.Empty((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(HttpStatusCode.OK, (await customer.PostAsJsonAsync("/api/bookings",
            Booking(contactName: new string('x', 200), contactPhone: "+123456789012345"))).StatusCode);
    }

    [Theory]
    [InlineData("", "0901234567")]
    [InlineData("   ", "0901234567")]
    [InlineData("Contact", "")]
    [InlineData("Contact", "*123#")]
    [InlineData("Contact", "12345")]
    [InlineData("Contact", "1234567890123456")]
    public async Task Booking_rejects_invalid_explicit_contact_without_creating_order(string name, string phone)
    {
        using var customer = fixture.Client((await Register()).Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/bookings",
            Booking(contactName: name, contactPhone: phone))).StatusCode);
        Assert.Empty((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
    }

    [Theory]
    [InlineData(-1, 2, true)]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, true)]
    [InlineData(0, 1, true)]
    [InlineData(-1, 4, true)]
    [InlineData(-2, 2, false)]
    [InlineData(2, 2, false)]
    public async Task Cleaner_schedule_rejects_overlap_but_allows_touching_boundaries(int offsetHours, int hours, bool overlap)
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        using var anotherCleaner = fixture.Client((await Register("Cleaner")).Token);
        // Cross midnight and submit the target using another offset to verify UTC comparisons.
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(3).AddHours(23), TimeSpan.Zero);
        var first = await CreateAt(customer, start);
        var second = await CreateAt(customer, start.AddHours(offsetHours).ToOffset(TimeSpan.FromHours(7)), hours);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{first}/accept", null)).StatusCode);
        var available = (await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available"))!;
        Assert.Equal(!overlap, available.Any(job => job.GetProperty("id").GetInt32() == second));
        var response = await cleaner.PutAsync($"/api/cleaner/jobs/{second}/accept", null);
        Assert.Equal(overlap ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
        if (overlap)
        {
            Assert.Contains("trùng lịch", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await anotherCleaner.PutAsync($"/api/cleaner/jobs/{second}/accept", null)).StatusCode);
        }
    }

    [Fact]
    public async Task Owner_orders_show_only_the_assigned_cleaner_contact_after_acceptance()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var stranger = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        using var guest = fixture.Client();
        var id = await Create(customer);
        var otherId = await Create(stranger);
        var pending = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("cleaner").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsJsonAsync("/api/profile", new
        { fullName = "Assigned Cleaner", phoneNumber = "+84 901 234 567", address = "Private home address" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        var accepted = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        var contact = accepted.GetProperty("cleaner");
        Assert.Equal("Assigned Cleaner", contact.GetProperty("name").GetString());
        Assert.Equal("+84 901 234 567", contact.GetProperty("phoneNumber").GetString());
        Assert.Equal(2, contact.EnumerateObject().Count());
        Assert.False(contact.TryGetProperty("email", out _));
        Assert.False(contact.TryGetProperty("address", out _));
        var otherOrder = Assert.Single((await stranger.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(otherId, otherOrder.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, otherOrder.GetProperty("cleaner").ValueKind);
        Assert.Empty((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/orders")).StatusCode);
        foreach (var action in new[] { "start", "complete" })
            Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        var completed = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("Assigned Cleaner", completed.GetProperty("cleaner").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Assigned_cleaner_without_phone_is_returned_and_profile_updates_are_reflected()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(JsonValueKind.Null, order.GetProperty("cleaner").GetProperty("phoneNumber").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsJsonAsync("/api/profile", new
        { fullName = "Updated Cleaner", phoneNumber = "0909999999" })).StatusCode);
        order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("Updated Cleaner", order.GetProperty("cleaner").GetProperty("name").GetString());
        Assert.Equal("0909999999", order.GetProperty("cleaner").GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task Concurrent_overlapping_claims_by_one_cleaner_accept_exactly_one_job()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var start = DateTimeOffset.UtcNow.AddDays(3);
        var ids = new List<int>();
        for (var i = 0; i < 6; i++) ids.Add(await CreateAt(customer, start));
        var responses = await Task.WhenAll(ids.Select(id => cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var mine = Assert.Single((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!);
        Assert.Equal("Accepted", mine.GetProperty("status").GetString());
        var available = (await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available"))!;
        Assert.DoesNotContain(available, job => ids.Contains(job.GetProperty("id").GetInt32()));
        var orders = (await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!;
        Assert.Equal(5, orders.Count(order => order.GetProperty("status").GetString() == "Pending"));
    }

    [Fact]
    public async Task Active_job_reserves_its_slot_until_completion_and_old_accept_alias_cannot_bypass_it()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var start = DateTimeOffset.UtcNow.AddDays(3);
        var first = await CreateAt(customer, start);
        var overlap = await CreateAt(customer, start.AddHours(1));
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/bookings/{first}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{first}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync($"/api/bookings/{overlap}/accept", null)).StatusCode);
        Assert.DoesNotContain((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available"))!, job => job.GetProperty("id").GetInt32() == overlap);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{first}/complete", null)).StatusCode);
        Assert.Contains((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/available"))!, job => job.GetProperty("id").GetInt32() == overlap);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/bookings/{overlap}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_starts_of_separate_accepted_jobs_allow_one_active_job_at_a_time()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var start = DateTimeOffset.UtcNow.AddDays(3);
        var first = await CreateAt(customer, start);
        var second = await CreateAt(customer, start.AddHours(2));
        foreach (var id in new[] { first, second })
            Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        var responses = await Task.WhenAll(new[] { first, second }.Select(id => cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var jobs = (await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!;
        var active = Assert.Single(jobs, job => job.GetProperty("status").GetString() == "InProgress");
        var waiting = Assert.Single(jobs, job => job.GetProperty("status").GetString() == "Accepted");
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("startedAt").ValueKind);
        var waitingId = waiting.GetProperty("id").GetInt32();
        var blocked = await cleaner.PutAsync($"/api/cleaner/jobs/{waitingId}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("Bạn đang làm một ca khác", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{active.GetProperty("id").GetInt32()}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{waitingId}/start", null)).StatusCode);
    }

    [Fact]
    public async Task Direct_cash_mode_retires_online_routes_without_creating_a_payment()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var anonymous = fixture.Client();
        var id = await Create(customer);
        var options = await customer.GetFromJsonAsync<JsonElement>("/api/payment/options");
        Assert.True(options.GetProperty("cash").GetBoolean());
        Assert.False(options.GetProperty("momo").GetBoolean());
        Assert.False(options.GetProperty("payos").GetBoolean());
        Assert.Equal("DirectCash", options.GetProperty("model").GetString());
        foreach (var route in new[] { "/api/payment/momo", "/api/payment/payos", $"/api/payment/{id}/reconcile" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(route, new { bookingId = id })).StatusCode);
            Assert.Equal(HttpStatusCode.Gone, (await customer.PostAsJsonAsync(route, new { bookingId = id, amount = 1 })).StatusCode);
        }
        foreach (var route in new[] { "/api/payment/momo-callback", "/api/payment/payos/webhook" })
            Assert.Equal(HttpStatusCode.Gone, (await anonymous.PostAsJsonAsync(route, new { success = true, amount = 140000 })).StatusCode);
        using var scope = fixture.Api.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Payments.AnyAsync(p => p.BookingId == id));
    }

    [Fact]
    public async Task Customer_feedback_requires_completion_is_owned_and_never_marks_cash_received()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var other = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        using var anonymous = fixture.Client();
        var id = await Create(customer);
        var path = $"/api/orders/{id}/confirm-completion";
        var feedback = new { rating = 4, review = "  Good work  ", paidAt = "2000-01-01", paymentStatus = "Paid" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(path, feedback)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(path, feedback)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cleaner.PutAsJsonAsync(path, feedback)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PutAsJsonAsync(path, feedback)).StatusCode);
        foreach (var action in new[] { "accept", "start", "complete" })
            Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        var response = await customer.PutAsJsonAsync(path, feedback);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var confirmation = await response.Content.ReadFromJsonAsync<JsonElement>();
        var time = confirmation.GetProperty("customerConfirmedAt").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, time.Kind);
        Assert.InRange(time, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(4, order.GetProperty("customerRating").GetInt32());
        Assert.Equal("Good work", order.GetProperty("customerReview").GetString());
        Assert.Equal("Unpaid", order.GetProperty("paymentStatus").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("paidAt").ValueKind);
        var repeat = await (await customer.PutAsJsonAsync(path, feedback)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(time, repeat.GetProperty("customerConfirmedAt").GetDateTime());
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PutAsJsonAsync(path, new { rating = 1, review = "Changed" })).StatusCode);
        var mine = Assert.Single((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!);
        Assert.Equal(4, mine.GetProperty("customerRating").GetInt32());
        var profileId = (await cleaner.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("cleanerProfile").GetProperty("id").GetInt32();
        var cleaners = (await customer.GetFromJsonAsync<JsonElement[]>("/api/cleaners"))!;
        Assert.Equal(4, Assert.Single(cleaners, row => row.GetProperty("id").GetInt32() == profileId).GetProperty("avgRating").GetDouble());
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PutAsJsonAsync("/api/orders/2147483647/confirm-completion", feedback)).StatusCode);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]
    [InlineData(5, 1001)]
    public async Task Invalid_customer_rating_or_review_is_rejected(int rating, int length)
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        foreach (var action in new[] { "accept", "start", "complete" }) await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await customer.PutAsJsonAsync($"/api/orders/{id}/confirm-completion", new { rating, review = new string('x', length) })).StatusCode);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal(JsonValueKind.Null, order.GetProperty("customerConfirmedAt").ValueKind);
    }

    [Fact]
    public async Task Concurrent_customer_confirmations_preserve_one_rating_and_timestamp()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        foreach (var action in new[] { "accept", "start", "complete" }) await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => customer.PutAsJsonAsync(
            $"/api/orders/{id}/confirm-completion", new { rating = 5, review = "Nice" })));
        var times = new List<DateTime>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            times.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerConfirmedAt").GetDateTime());
        }
        Assert.Single(times.Distinct());
    }

    [Fact]
    public async Task Legacy_online_history_is_retained_and_cannot_be_relabelled_as_cash()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        foreach (var action in new[] { "accept", "start", "complete" }) await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null);
        using (var scope = fixture.Api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Payments.Add(new CleanMate.Api.Domain.Entities.Payment { BookingId = id, Method = "momo", Status = "Pending", Amount = 140000, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/cash-payment", null)).StatusCode);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("momo", order.GetProperty("paymentMethod").GetString());
        Assert.Equal("Pending", order.GetProperty("paymentStatus").GetString());
    }

    [Fact]
    public async Task Cash_receipt_requires_completed_assigned_job_and_ignores_client_amount()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        using var otherCleaner = fixture.Client((await Register("Cleaner")).Token);
        using var anonymous = fixture.Client();
        var id = await Create(customer);
        var path = $"/api/cleaner/jobs/{id}/cash-payment";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cleaner.PutAsync(path, null)).StatusCode);
        await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null);
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync(path, null)).StatusCode);
        await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null);
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync(path, null)).StatusCode);
        await cleaner.PutAsync($"/api/cleaner/jobs/{id}/complete", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherCleaner.PutAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsync(path, null)).StatusCode);
        var unpaid = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("Unpaid", unpaid.GetProperty("paymentStatus").GetString());
        Assert.Equal(JsonValueKind.Null, unpaid.GetProperty("paidAt").ValueKind);
        var response = await cleaner.PutAsJsonAsync(path, new { amount = 1, paidAt = "2000-01-01", collectedByCleanerId = -1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(140000m, receipt.GetProperty("amount").GetDecimal());
        Assert.Equal("cash", receipt.GetProperty("method").GetString());
        Assert.Equal("Paid", receipt.GetProperty("status").GetString());
        Assert.True(receipt.GetProperty("collectedByCleanerId").GetInt32() > 0);
        var paidAt = receipt.GetProperty("paidAt").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, paidAt.Kind);
        Assert.InRange(paidAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        var repeat = await (await cleaner.PutAsync(path, null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(receipt.GetProperty("id").GetGuid(), repeat.GetProperty("id").GetGuid());
        Assert.Equal(paidAt, repeat.GetProperty("paidAt").GetDateTime());
        var paid = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("Completed", paid.GetProperty("status").GetString());
        Assert.Equal("Paid", paid.GetProperty("paymentStatus").GetString());
        Assert.Equal(paidAt, paid.GetProperty("paidAt").GetDateTime());
        Assert.Equal(HttpStatusCode.NotFound, (await cleaner.PutAsync("/api/cleaner/jobs/2147483647/cash-payment", null)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_cash_confirmations_return_the_same_single_receipt()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        foreach (var action in new[] { "accept", "start", "complete" })
            Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => cleaner.PutAsync($"/api/cleaner/jobs/{id}/cash-payment", null)));
        var receipts = new List<Guid>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            receipts.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        Assert.Single(receipts.Distinct());
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = Assert.Single(await db.Payments.Where(p => p.BookingId == id).ToListAsync());
        Assert.Equal(140000m, saved.Amount);
    }

    [Fact]
    public async Task Assigned_cleaner_starts_and_completes_once_with_utc_timestamps_visible_to_customer()
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null)).StatusCode);
        async Task<JsonElement> Order() => Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!,
            order => order.GetProperty("id").GetInt32() == id);
        var accepted = await Order();
        Assert.Equal(JsonValueKind.Null, accepted.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, accepted.GetProperty("completedAt").ValueKind);
        Assert.Equal(DateTimeKind.Utc, accepted.GetProperty("date").GetDateTime().Kind);
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null)).StatusCode);
        var started = await Order();
        Assert.Equal("InProgress", started.GetProperty("status").GetString());
        var startedAt = started.GetProperty("startedAt").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, startedAt.Kind);
        Assert.InRange(startedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.Equal(JsonValueKind.Null, started.GetProperty("completedAt").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PutAsync($"/api/orders/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null)).StatusCode);
        Assert.Equal(startedAt, (await Order()).GetProperty("startedAt").GetDateTime());
        Assert.Equal(HttpStatusCode.OK, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/complete", null)).StatusCode);
        var completed = await Order();
        var completedAt = completed.GetProperty("completedAt").GetDateTime();
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal(DateTimeKind.Utc, completedAt.Kind);
        Assert.True(completedAt >= startedAt);
        Assert.Equal(startedAt, completed.GetProperty("startedAt").GetDateTime());
        Assert.Equal(140000m, completed.GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null)).StatusCode);
        Assert.Equal(completedAt, (await Order()).GetProperty("completedAt").GetDateTime());
        var mine = Assert.Single((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!,
            job => job.GetProperty("id").GetInt32() == id);
        Assert.Equal(completedAt, mine.GetProperty("completedAt").GetDateTime());
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    public async Task Job_progress_requires_assigned_cleaner_and_checks_missing_bookings(string action)
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        using var otherCleaner = fixture.Client((await Register("Cleaner")).Token);
        using var anonymous = fixture.Client();
        var id = await Create(customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null);
        if (action == "complete") await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherCleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cleaner.PutAsync($"/api/cleaner/jobs/{int.MaxValue}/{action}", null)).StatusCode);
        var mine = Assert.Single((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!);
        Assert.Equal(action == "start" ? "Accepted" : "InProgress", mine.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("completedAt").ValueKind);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    public async Task Concurrent_progress_requests_change_state_exactly_once(string action)
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept", null);
        if (action == "complete") await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start", null);
        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)));
        Assert.Single(replies, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, replies.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var job = Assert.Single((await cleaner.GetFromJsonAsync<JsonElement[]>("/api/cleaner/jobs/mine"))!);
        Assert.Equal(action == "start" ? "InProgress" : "Completed", job.GetProperty("status").GetString());
        Assert.Equal(DateTimeKind.Utc, job.GetProperty(action == "start" ? "startedAt" : "completedAt").GetDateTime().Kind);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("complete")]
    public async Task Cancelled_booking_cannot_be_started_or_completed(string action)
    {
        using var customer = fixture.Client((await Register()).Token);
        using var cleaner = fixture.Client((await Register("Cleaner")).Token);
        var id = await Create(customer);
        await customer.PutAsync($"/api/orders/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}", null)).StatusCode);
        var order = Assert.Single((await customer.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
        Assert.Equal("Cancelled", order.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, order.GetProperty("completedAt").ValueKind);
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
    public async Task Customer_application_requires_approval_before_existing_token_can_claim()
    {
        var account = await Register();
        using var client = fixture.Client(account.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        var registration = await client.PostAsJsonAsync("/api/profile/register-cleaner", new { bio = "", city = "Ha Noi", addressText = "", hourlyRate = 120000 });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var user = await registration.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Customer", user.GetProperty("role").GetString());
        Assert.False(user.GetProperty("isCleaner").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        using var admin = fixture.Client(fixture.AdminToken);
        var profileId = user.GetProperty("cleanerProfile").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/management/applications/{profileId}/review", new { status = "Approved" })).StatusCode);
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
        foreach (var action in new[] { "Accept", "Start", "Complete" })
            Assert.Equal(HttpStatusCode.BadRequest, (await portal.PostAsync($"/Booking/{action}", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "1" }))).StatusCode);
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
        var applications = await fixture.Client(fixture.AdminToken).GetFromJsonAsync<JsonElement>($"/api/management/applications?search={email}");
        var profileId = applications.GetProperty("items")[0].GetProperty("id").GetInt32();
        using (var admin = fixture.Client(fixture.AdminToken))
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/management/applications/{profileId}/review", new { status = "Approved" })).StatusCode);
        await portal.GetAsync("/Auth/Logout");
        var login = await Form("/Auth/Login", new() { ["Email"] = email, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains("Portal Cleaner", await portal.GetStringAsync("/Dashboard/Index"));
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
