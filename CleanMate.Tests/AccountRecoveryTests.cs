using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanMate.Api;
using CleanMate.Api.Infrastructure;
using CleanMate.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanMate.Tests;

public partial class ApiIntegrationTests
{
    private async Task<string> WaitAccountCode(string email, string purpose)
    {
        for(var attempt=0;attempt<200;attempt++){
            var mail=fixture.Mail.Messages.LastOrDefault(m=>m.Email==email&&m.Purpose==purpose&&m.Code is not null);
            if(mail?.Code is string code){using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var hash=AccountEmailWorker.Hash(code);if(await db.AccountCodes.AnyAsync(c=>c.Hash==hash&&c.DeliveredAt!=null))return code;}
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Test mailbox did not deliver the account code.");
    }
    private async Task<string> RequestReset(string email)
    {
        using var guest=fixture.Client();var result=await guest.PostAsJsonAsync("/api/auth/forgot-password",new{email});Assert.Equal(HttpStatusCode.Accepted,result.StatusCode);
        Assert.False((await result.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code",out _));return await WaitAccountCode(email,"ResetPassword");
    }
    private async Task<(string Token,int Id,string Email)> RecoveryAccount(string role)
    {
        if(role=="Staff")return await Staff();
        if(role!="Admin")return await Register(role);
        var email=Guid.NewGuid()+"@example.test";using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await AdminBootstrap.CreateAsync(db,email,Password,"Recovery Administrator");
        using var guest=fixture.Client();var auth=await (await guest.PostAsJsonAsync("/api/auth/login",new{email,password=Password})).Content.ReadFromJsonAsync<JsonElement>();return(auth.GetProperty("token").GetString()!,auth.GetProperty("user").GetProperty("id").GetInt32(),email);
    }

    [Theory]
    [InlineData("Customer")][InlineData("Cleaner")][InlineData("Staff")][InlineData("Admin")]
    public async Task Email_password_recovery_revokes_sessions_and_uses_one_time_hashed_codes_for_all_roles(string role)
    {
        var account=await RecoveryAccount(role);using var old=fixture.Client(account.Token);using var guest=fixture.Client();var code=await RequestReset(account.Email);const string next="Recovered-password-456!";
        using(var scope=fixture.Api.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var row=await db.AccountCodes.SingleAsync(c=>c.Hash==AccountEmailWorker.Hash(code));Assert.NotEqual(code,row.Hash);Assert.Equal(64,code.Length);Assert.NotNull(row.DeliveredAt);Assert.Null(row.ConsumedAt);}
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email.ToUpperInvariant(),code=code.ToLowerInvariant(),newPassword=next})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await old.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email,code,newPassword="Another-password-789!"})).StatusCode);
        var original=role=="Staff"?"Staff-integration-password-123!":Password;
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.PostAsJsonAsync("/api/auth/login",new{email=account.Email,password=original})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/login",new{email=account.Email,password=next})).StatusCode);
        using var verify=fixture.Api.Services.CreateScope();var store=verify.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(1,await store.AdminAudits.CountAsync(a=>a.ActorId==account.Id&&a.Action=="ResetPassword"));Assert.Equal(1,await store.Notifications.CountAsync(n=>n.UserId==account.Id&&n.EventKey.StartsWith("ResetPassword:")));
    }

    [Theory]
    [InlineData("Customer")][InlineData("Cleaner")][InlineData("Staff")][InlineData("Admin")]
    public async Task Email_verification_is_bound_to_the_requesting_account_and_single_use(string role)
    {
        var account=await RecoveryAccount(role);using var owner=fixture.Client(account.Token);using var guest=fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.PostAsync("/api/auth/request-email-verification",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,(await owner.PostAsync("/api/auth/request-email-verification",null)).StatusCode);var code=await WaitAccountCode(account.Email,"VerifyEmail");
        Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/api/auth/verify-email",new{email="other@example.test",code})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email,code,newPassword="Different-password-123!"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/verify-email",new{email=account.Email,code})).StatusCode);
        var me=await owner.GetFromJsonAsync<JsonElement>("/api/me");Assert.EndsWith("Z",me.GetProperty("emailVerifiedAt").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/api/auth/verify-email",new{email=account.Email,code})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await owner.PostAsync("/api/auth/request-email-verification",null)).StatusCode);
    }

    [Theory]
    [InlineData("expired")][InlineData("consumed")][InlineData("undelivered")][InlineData("version")][InlineData("inactive")][InlineData("email")]
    public async Task Invalid_recovery_codes_do_not_change_credentials(string state)
    {
        var account=await Register();using var guest=fixture.Client();var code=await RequestReset(account.Email);using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var hash=AccountEmailWorker.Hash(code);var password=await db.Users.Where(u=>u.Id==account.Id).Select(u=>u.PasswordHash).SingleAsync();
        switch(state){
            case "expired":await db.AccountCodes.Where(c=>c.Hash==hash).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.ExpiresAt,DateTime.UtcNow.AddMinutes(-1)));break;
            case "consumed":await db.AccountCodes.Where(c=>c.Hash==hash).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.ConsumedAt,(DateTime?)DateTime.UtcNow));break;
            case "undelivered":await db.AccountCodes.Where(c=>c.Hash==hash).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.DeliveredAt,(DateTime?)null));break;
            case "version":await db.Users.Where(u=>u.Id==account.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.TokenVersion,u=>u.TokenVersion+1));break;
            case "inactive":await db.Users.Where(u=>u.Id==account.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.IsActive,false));break;
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=state=="email"?"wrong@example.test":account.Email,code,newPassword="Recovered-password-456!"})).StatusCode);
        Assert.Equal(password,await db.Users.Where(u=>u.Id==account.Id).Select(u=>u.PasswordHash).SingleAsync());Assert.False(await db.AdminAudits.AnyAsync(a=>a.ActorId==account.Id&&a.Action=="ResetPassword"));
    }

    [Fact]
    public async Task Concurrent_password_reset_has_one_winner_and_audit_without_duplicate_notifications()
    {
        var account=await Register();var code=await RequestReset(account.Email);using var guest=fixture.Client();var payload=new{email=account.Email,code,newPassword="Concurrent-recovery-123!"};
        var replies=await Task.WhenAll(guest.PostAsJsonAsync("/api/auth/reset-password",payload),guest.PostAsJsonAsync("/api/auth/reset-password",payload));Assert.Single(replies,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(replies,r=>r.StatusCode==HttpStatusCode.BadRequest);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.ActorId==account.Id&&a.Action=="ResetPassword"));Assert.Equal(1,await db.Notifications.CountAsync(n=>n.UserId==account.Id&&n.EventKey.StartsWith("ResetPassword:")));
    }

    [Fact]
    public async Task Failed_reset_notification_rolls_back_password_version_code_and_audit()
    {
        var account=await Register();var code=await RequestReset(account.Email);using var guest=fixture.Client();using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var original=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==account.Id);var name="RejectReset_"+Guid.NewGuid().ToString("N");
        var sql=$"ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [{name}] CHECK ([UserId] <> {account.Id} OR [EventKey] NOT LIKE 'ResetPassword:%')";await db.Database.ExecuteSqlRawAsync(sql);var payload=new{email=account.Email,code,newPassword="Recovered-password-456!"};
        try{Assert.Equal(HttpStatusCode.InternalServerError,(await guest.PostAsJsonAsync("/api/auth/reset-password",payload)).StatusCode);var current=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==account.Id);Assert.Equal(original.PasswordHash,current.PasswordHash);Assert.Equal(original.TokenVersion,current.TokenVersion);Assert.Null((await db.AccountCodes.SingleAsync(c=>c.Hash==AccountEmailWorker.Hash(code))).ConsumedAt);Assert.False(await db.AdminAudits.AnyAsync(a=>a.ActorId==account.Id&&a.Action=="ResetPassword"));}
        finally{var drop=$"ALTER TABLE [Notifications] DROP CONSTRAINT [{name}]";await db.Database.ExecuteSqlRawAsync(drop);}
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/reset-password",payload)).StatusCode);
    }

    [Fact]
    public async Task Recovery_password_validation_retains_the_code_until_a_valid_password_is_submitted()
    {
        var account=await Register();var code=await RequestReset(account.Email);using var guest=fixture.Client();
        foreach(var next in new[]{"short",new string('é',37),Password,"            "})Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email,code,newPassword=next})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email,code,newPassword="Valid-recovered-password-123!"})).StatusCode);
    }

    [Fact]
    public async Task Recovery_returns_the_same_accepted_message_for_unknown_disabled_and_google_only_accounts()
    {
        var disabled=await Register();var google=await Register();var marker=await Register();using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Users.Where(u=>u.Id==disabled.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.IsActive,false));await db.Users.Where(u=>u.Id==google.Id).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.PasswordHash,""));using var guest=fixture.Client();var emails=new[]{Guid.NewGuid()+"@example.test",disabled.Email,google.Email,marker.Email};string? first=null;
        foreach(var email in emails){var response=await guest.PostAsJsonAsync("/api/auth/forgot-password",new{email});Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);var text=await response.Content.ReadAsStringAsync();first??=text;Assert.Equal(first,text);Assert.Contains("no-store",response.Headers.CacheControl!.ToString());Assert.DoesNotContain(email,text);}
        await WaitAccountCode(marker.Email,"ResetPassword");foreach(var email in emails.Take(3))Assert.DoesNotContain(fixture.Mail.Messages,m=>m.Email==email);
    }

    [Fact]
    public async Task Disabled_email_delivery_is_explicit_and_never_reports_a_code_was_sent()
    {
        var account=await Register();using var guest=fixture.Client();fixture.Mail.IsConfigured=false;
        try{foreach(var email in new[]{account.Email,"unknown@example.test"})Assert.Equal(HttpStatusCode.ServiceUnavailable,(await guest.PostAsJsonAsync("/api/auth/forgot-password",new{email})).StatusCode);}
        finally{fixture.Mail.IsConfigured=true;}
        Assert.DoesNotContain(fixture.Mail.Messages,m=>m.Email==account.Email);
    }

    [Fact]
    public async Task Failed_delivery_never_activates_the_code_and_a_later_request_can_recover()
    {
        var account=await Register();fixture.Mail.FailNextDelivery(account.Email);using var guest=fixture.Client();Assert.Equal(HttpStatusCode.Accepted,(await guest.PostAsJsonAsync("/api/auth/forgot-password",new{email=account.Email})).StatusCode);
        string? attempted=null;for(var i=0;i<200&&attempted is null;i++){attempted=fixture.Mail.Attempts.LastOrDefault(m=>m.Email==account.Email)?.Code;if(attempted is null)await Task.Delay(25);}Assert.NotNull(attempted);
        Assert.Equal(HttpStatusCode.BadRequest,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email,code=attempted,newPassword="Recovered-after-failure-123!"})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var hash=AccountEmailWorker.Hash(attempted!);Assert.Null((await db.AccountCodes.SingleAsync(c=>c.Hash==hash)).DeliveredAt);
        // Advance only this generated test account's issuance bucket; avoid sleeping for two minutes.
        await db.AccountCodes.Where(c=>c.Hash==hash).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.IssueWindow,-1L));var delivered=await RequestReset(account.Email);Assert.NotEqual(attempted,delivered);Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email,code=delivered,newPassword="Recovered-after-failure-123!"})).StatusCode);
    }

    [Fact]
    public async Task Concurrent_email_verification_has_one_winner_and_does_not_grant_cleaner_approval()
    {
        using var guest=fixture.Client();var email=Guid.NewGuid()+"@example.test";var registered=await guest.PostAsJsonAsync("/api/auth/register",new{fullName="Pending applicant",email,password=Password,role="Cleaner"});var auth=await registered.Content.ReadFromJsonAsync<JsonElement>();var id=auth.GetProperty("user").GetProperty("id").GetInt32();using var owner=fixture.Client(auth.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.Accepted,(await owner.PostAsync("/api/auth/request-email-verification",null)).StatusCode);var code=await WaitAccountCode(email,"VerifyEmail");var payload=new{email,code};var replies=await Task.WhenAll(guest.PostAsJsonAsync("/api/auth/verify-email",payload),guest.PostAsJsonAsync("/api/auth/verify-email",payload));Assert.Single(replies,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(replies,r=>r.StatusCode==HttpStatusCode.BadRequest);Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync("/api/cleaner/jobs/available")).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.ActorId==id&&a.Action=="VerifyEmail"));Assert.Equal("Pending",await db.CleanerProfiles.Where(c=>c.UserId==id).Select(c=>c.ReviewStatus).SingleAsync());
    }

    [Theory]
    [InlineData("forgot-password",HttpStatusCode.ServiceUnavailable)]
    [InlineData("reset-password",HttpStatusCode.BadRequest)]
    [InlineData("verify-email",HttpStatusCode.BadRequest)]
    public async Task Recovery_attempts_are_rate_limited_per_ip_with_retry_instructions(string endpoint,HttpStatusCode initial)
    {
        await using var host=await LimitedAuthHost();using var client=new HttpClient{BaseAddress=new Uri(host.Urls.Single())};client.DefaultRequestHeaders.Add("X-Forwarded-For","198.51.100.155");var input=new{email="quota@example.test",code=new string('A',64),newPassword="Quota-recovery-password-123!"};for(var i=0;i<2;i++)Assert.Equal(initial,(await client.PostAsJsonAsync($"/api/auth/{endpoint}",input)).StatusCode);var limited=await client.PostAsJsonAsync($"/api/auth/{endpoint}",input);Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);Assert.True(limited.Headers.Contains("Retry-After"));client.DefaultRequestHeaders.Remove("X-Forwarded-For");client.DefaultRequestHeaders.Add("X-Forwarded-For","198.51.100.156");Assert.Equal(initial,(await client.PostAsJsonAsync($"/api/auth/{endpoint}",input)).StatusCode);
    }

    [Fact]
    public async Task Failed_verification_notice_rolls_back_email_status_code_and_audit()
    {
        var account=await Register();using var owner=fixture.Client(account.Token);using var guest=fixture.Client();Assert.Equal(HttpStatusCode.Accepted,(await owner.PostAsync("/api/auth/request-email-verification",null)).StatusCode);var code=await WaitAccountCode(account.Email,"VerifyEmail");using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var name="RejectVerification_"+Guid.NewGuid().ToString("N");var sql=$"ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [{name}] CHECK ([UserId] <> {account.Id} OR [EventKey] NOT LIKE 'VerifyEmail:%')";await db.Database.ExecuteSqlRawAsync(sql);
        try{Assert.Equal(HttpStatusCode.InternalServerError,(await guest.PostAsJsonAsync("/api/auth/verify-email",new{email=account.Email,code})).StatusCode);Assert.Null(await db.Users.Where(u=>u.Id==account.Id).Select(u=>u.EmailVerifiedAt).SingleAsync());Assert.Null((await db.AccountCodes.SingleAsync(c=>c.Hash==AccountEmailWorker.Hash(code))).ConsumedAt);Assert.False(await db.AdminAudits.AnyAsync(a=>a.ActorId==account.Id&&a.Action=="VerifyEmail"));}
        finally{var drop=$"ALTER TABLE [Notifications] DROP CONSTRAINT [{name}]";await db.Database.ExecuteSqlRawAsync(drop);}
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/verify-email",new{email=account.Email,code})).StatusCode);
    }
}
