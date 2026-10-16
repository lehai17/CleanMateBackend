using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanMate.Api.Domain.Entities;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using UserRole = CleanMate.Domain.Entities.UserRole;

namespace CleanMate.Tests;

public partial class ApiIntegrationTests
{
    private const string AccessReason = "Reviewed account access after an operational incident.";

    [Theory]
    [InlineData("Customer")][InlineData("Cleaner")][InlineData("Staff")]
    public async Task Account_moderation_is_admin_only(string role)
    {
        var actor = role == "Staff" ? await Staff() : await Register(role);
        var target = await Register();
        using var client = fixture.Client(actor.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/management/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/management/accounts/{target.Id}/access", new { isActive=false, expectedActive=true, reason=AccessReason })).StatusCode);
        using var owner = fixture.Client(target.Token);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Guest_cannot_list_or_moderate_accounts()
    {
        using var guest=fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync("/api/management/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.PutAsJsonAsync("/api/management/accounts/1/access",new{isActive=false,expectedActive=true,reason=AccessReason})).StatusCode);
    }

    [Fact]
    public async Task Account_list_paginates_filters_and_never_exposes_credentials_or_managers()
    {
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prefix="Moderation-"+Guid.NewGuid().ToString("N");
        var rows=Enumerable.Range(0,23).Select(i=>new User{FullName=$"{prefix} {i}",Email=$"{prefix}-{i}@example.test",Role=i==22?UserRole.Staff:i%2==0?UserRole.Customer:UserRole.Cleaner,IsActive=i%3!=0,PhoneNumber=$"090123{i:0000}"}).ToList();
        db.Users.AddRange(rows);await db.SaveChangesAsync();using var admin=fixture.Client(fixture.AdminToken);
        var response=await admin.GetAsync($"/api/management/accounts?search={prefix}");Assert.True(response.Headers.CacheControl?.NoStore);var first=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(22,first.GetProperty("total").GetInt32());Assert.Equal(20,first.GetProperty("items").GetArrayLength());
        var second=await admin.GetFromJsonAsync<JsonElement>($"/api/management/accounts?search={prefix}&page=2");Assert.Equal(2,second.GetProperty("items").GetArrayLength());
        Assert.Empty(first.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetInt32()).Intersect(second.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetInt32())));
        foreach(var item in first.GetProperty("items").EnumerateArray())foreach(var secret in new[]{"passwordHash","tokenVersion","token","address","emailVerifiedAt"})Assert.False(item.TryGetProperty(secret,out _));
        var filtered=await admin.GetFromJsonAsync<JsonElement>($"/api/management/accounts?search={prefix}&role=Cleaner&status=Locked");
        Assert.Equal(rows.Count(u=>u.Role==UserRole.Cleaner&&!u.IsActive),filtered.GetProperty("total").GetInt32());
        Assert.All(filtered.GetProperty("items").EnumerateArray(),item=>{Assert.Equal("Cleaner",item.GetProperty("role").GetString());Assert.False(item.GetProperty("isActive").GetBoolean());});
        var exact=await admin.GetFromJsonAsync<JsonElement>($"/api/management/accounts?search=%23{rows[1].Id}");Assert.Contains(exact.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetInt32()==rows[1].Id);
        foreach(var query in new[]{"page=0","page=100001","role=Admin","status=Inactive","search="+new string('a',101)})Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/management/accounts?"+query)).StatusCode);
    }

    [Fact]
    public async Task Account_list_reports_pending_and_assigned_work_without_counting_finished_jobs()
    {
        var customer=await Register();var worker=await Register("Cleaner");using var owner=fixture.Client(customer.Token);using var cleaner=fixture.Client(worker.Token);using var admin=fixture.Client(fixture.AdminToken);
        var job=await Create(owner);await CreateAt(owner,DateTimeOffset.UtcNow.AddDays(4));
        Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{job}/accept",null)).StatusCode);
        foreach(var id in new[]{customer.Id,worker.Id}){
            var list=await admin.GetFromJsonAsync<JsonElement>($"/api/management/accounts?search=%23{id}");var item=list.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==id);
            Assert.Equal(1,item.GetProperty("activeBookings").GetInt32());Assert.Equal(id==customer.Id?1:0,item.GetProperty("pendingBookings").GetInt32());
        }
        foreach(var action in new[]{"start","complete"})Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{job}/{action}",null)).StatusCode);
        var after=await admin.GetFromJsonAsync<JsonElement>($"/api/management/accounts?search=%23{worker.Id}");Assert.Equal(0,after.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==worker.Id).GetProperty("activeBookings").GetInt32());
    }

    [Theory]
    [InlineData("Customer")][InlineData("Cleaner")]
    public async Task Account_lock_unlock_revokes_sessions_but_preserves_job_and_cash_history(string role)
    {
        var customer=await Register();var worker=await Register("Cleaner");using var owner=fixture.Client(customer.Token);using var cleaner=fixture.Client(worker.Token);using var admin=fixture.Client(fixture.AdminToken);
        var job=await Create(owner);foreach(var action in new[]{"accept","start","complete","cash-payment"})Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{job}/{action}",null)).StatusCode);
        var target=role=="Customer"?customer:worker;using var old=fixture.Client(target.Token);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        async Task<string> Snapshot()=>JsonSerializer.Serialize(await db.Bookings.AsNoTracking().Where(b=>b.Id==job).Select(b=>new{b.Status,b.Price,b.UserId,b.CleanerId,b.StartTime,b.StartedAt,b.CompletedAt,b.ContactName,b.ContactPhone,b.PaymentMethod,paid=b.Payment!.Status,b.Payment.PaidAt}).SingleAsync());
        var before=await Snapshot();var path=$"/api/management/accounts/{target.Id}/access";
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{isActive=false,expectedActive=true,reason=AccessReason})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await old.GetAsync("/api/me")).StatusCode);
        using var guest=fixture.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await guest.PostAsJsonAsync("/api/auth/login",new{email=target.Email,password=Password})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{isActive=true,expectedActive=false,reason="Account access restored after review."})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await old.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await guest.PostAsJsonAsync("/api/auth/login",new{email=target.Email,password=Password})).StatusCode);
        Assert.Equal(before,await Snapshot());Assert.Equal(2,(await db.Users.FindAsync(target.Id))!.TokenVersion);
        Assert.Equal(2,await db.AdminAudits.CountAsync(a=>a.Action=="AccountAccess"&&a.Target==$"User:{target.Id}"));
        Assert.Contains(AccessReason,(await db.AdminAudits.FirstAsync(a=>a.Action=="AccountAccess"&&a.Target==$"User:{target.Id}")).Details);
    }

    [Fact]
    public async Task Account_access_validates_reason_rejects_manager_targets_and_has_noop_or_stale_safety()
    {
        var account=await Register();var staff=await Staff();using var admin=fixture.Client(fixture.AdminToken);var path=$"/api/management/accounts/{account.Id}/access";
        foreach(var reason in new string?[]{null,"short",new string('x',1001)})Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(path,new{isActive=false,expectedActive=true,reason})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{isActive=true,expectedActive=true,reason=AccessReason})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync(path,new{isActive=false,expectedActive=false,reason=AccessReason})).StatusCode);
        var me=await admin.GetFromJsonAsync<JsonElement>("/api/me");foreach(var id in new[]{staff.Id,me.GetProperty("id").GetInt32(),int.MaxValue})Assert.Equal(HttpStatusCode.NotFound,(await admin.PutAsJsonAsync($"/api/management/accounts/{id}/access",new{isActive=false,expectedActive=true,reason=AccessReason})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(0,(await db.Users.FindAsync(account.Id))!.TokenVersion);Assert.False(await db.AdminAudits.AnyAsync(a=>a.Action=="AccountAccess"&&a.Target==$"User:{account.Id}"));
    }

    [Fact]
    public async Task Concurrent_account_locks_commit_one_version_increment_and_one_reasoned_audit()
    {
        var account=await Register();using var admin=fixture.Client(fixture.AdminToken);var path=$"/api/management/accounts/{account.Id}/access";
        var results=await Task.WhenAll(admin.PutAsJsonAsync(path,new{isActive=false,expectedActive=true,reason=AccessReason}),admin.PutAsJsonAsync(path,new{isActive=false,expectedActive=true,reason=AccessReason}));
        Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var user=(await db.Users.FindAsync(account.Id))!;Assert.False(user.IsActive);Assert.Equal(1,user.TokenVersion);Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.Action=="AccountAccess"&&a.Target==$"User:{account.Id}"));
    }

    [Fact]
    public async Task Failed_account_access_audit_rolls_back_access_and_session_revocation()
    {
        var account=await Register();using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var trigger="RejectAccountAccess_"+Guid.NewGuid().ToString("N");
        var sql=$"CREATE TRIGGER [{trigger}] ON [AdminAudits] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Action='AccountAccess' AND Target='User:{account.Id}') THROW 51000, 'Test account audit failure', 1; END";await db.Database.ExecuteSqlRawAsync(sql);
        try{using var admin=fixture.Client(fixture.AdminToken);Assert.Equal(HttpStatusCode.InternalServerError,(await admin.PutAsJsonAsync($"/api/management/accounts/{account.Id}/access",new{isActive=false,expectedActive=true,reason=AccessReason})).StatusCode);}
        finally{var drop=$"DROP TRIGGER [{trigger}]";await db.Database.ExecuteSqlRawAsync(drop);}
        var user=(await db.Users.FindAsync(account.Id))!;Assert.True(user.IsActive);Assert.Equal(0,user.TokenVersion);using var owner=fixture.Client(account.Token);Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync("/api/me")).StatusCode);Assert.False(await db.AdminAudits.AnyAsync(a=>a.Action=="AccountAccess"&&a.Target==$"User:{account.Id}"));
    }

    [Theory]
    [InlineData("ResetPassword")][InlineData("VerifyEmail")]
    public async Task Moderation_invalidates_customer_email_codes_issued_before_locking(string purpose)
    {
        var account=await Register();using var owner=fixture.Client(account.Token);using var admin=fixture.Client(fixture.AdminToken);using var guest=fixture.Client();
        string code;if(purpose=="ResetPassword")code=await RequestReset(account.Email);else{Assert.Equal(HttpStatusCode.Accepted,(await owner.PostAsync("/api/auth/request-email-verification",null)).StatusCode);code=await WaitAccountCode(account.Email,purpose);}
        var path=$"/api/management/accounts/{account.Id}/access";
        foreach(var active in new[]{false,true})Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{isActive=active,expectedActive=!active,reason=AccessReason})).StatusCode);
        var result=purpose=="ResetPassword"?await guest.PostAsJsonAsync("/api/auth/reset-password",new{email=account.Email,code,newPassword="Blocked-recovery-password-456!"}):await guest.PostAsJsonAsync("/api/auth/verify-email",new{email=account.Email,code});Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);
    }
}
