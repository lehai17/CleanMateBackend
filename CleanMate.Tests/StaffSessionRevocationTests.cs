using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanMate.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanMate.Tests;

public partial class ApiIntegrationTests
{
    [Fact]
    public async Task Staff_access_changes_revoke_old_sessions_permanently_even_after_unlock()
    {
        var account = await Staff();
        using var old = fixture.Client(account.Token);
        using var admin = fixture.Client(fixture.AdminToken);
        var path = $"/api/management/staff/{account.Id}/access";
        var changes = await Task.WhenAll(
            admin.PutAsJsonAsync(path, new { isActive = false, expectedActive = true }),
            admin.PutAsJsonAsync(path, new { isActive = false, expectedActive = true }));
        Assert.Single(changes, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(changes, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path, new { isActive = true, expectedActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/management/dashboard")).StatusCode);
        using var guest = fixture.Client();
        var login = await guest.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = "Staff-integration-password-123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
        using var fresh = fixture.Client(auth.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/management/dashboard")).StatusCode);
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, (await db.Users.FindAsync(account.Id))!.TokenVersion);
        Assert.Equal(2, await db.AdminAudits.CountAsync(a => a.Action == "StaffAccess" && a.Target == $"User:{account.Id}"));
    }

    [Fact]
    public async Task Staff_access_noop_preserves_sessions_and_does_not_create_an_audit()
    {
        var account = await Staff();
        using var admin = fixture.Client(fixture.AdminToken);
        using var staff = fixture.Client(account.Token);
        var path = $"/api/management/staff/{account.Id}/access";
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path, new { isActive = true, expectedActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, new { isActive = false, expectedActive = false })).StatusCode);
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, (await db.Users.FindAsync(account.Id))!.TokenVersion);
        Assert.False(await db.AdminAudits.AnyAsync(a => a.Action == "StaffAccess" && a.Target == $"User:{account.Id}"));
    }

    [Fact]
    public async Task Failed_staff_access_audit_rolls_back_the_lock_and_session_version()
    {
        var account = await Staff();
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trigger = "RejectStaffAccess_" + Guid.NewGuid().ToString("N");
        var sql = $"CREATE TRIGGER [{trigger}] ON [AdminAudits] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Action='StaffAccess' AND Target='User:{account.Id}') THROW 51000, 'Test staff audit failure', 1; END";
        await db.Database.ExecuteSqlRawAsync(sql);
        try
        {
            using var admin = fixture.Client(fixture.AdminToken);
            Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PutAsJsonAsync($"/api/management/staff/{account.Id}/access", new { isActive = false, expectedActive = true })).StatusCode);
        }
        finally { var dropSql = $"DROP TRIGGER [{trigger}]"; await db.Database.ExecuteSqlRawAsync(dropSql); }
        var user = (await db.Users.FindAsync(account.Id))!;
        Assert.True(user.IsActive);
        Assert.Equal(0, user.TokenVersion);
        using var staff = fixture.Client(account.Token);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/me")).StatusCode);
        Assert.False(await db.AdminAudits.AnyAsync(a => a.Action == "StaffAccess" && a.Target == $"User:{account.Id}"));
    }

    [Theory]
    [InlineData("ResetPassword")]
    [InlineData("VerifyEmail")]
    public async Task Staff_access_change_invalidates_prelock_email_codes(string purpose)
    {
        var account = await Staff();
        using var owner = fixture.Client(account.Token);
        using var admin = fixture.Client(fixture.AdminToken);
        using var guest = fixture.Client();
        string code;
        if (purpose == "ResetPassword") code = await RequestReset(account.Email);
        else
        {
            Assert.Equal(HttpStatusCode.Accepted, (await owner.PostAsync("/api/auth/request-email-verification", null)).StatusCode);
            code = await WaitAccountCode(account.Email, purpose);
        }
        var path = $"/api/management/staff/{account.Id}/access";
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path, new { isActive = false, expectedActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(path, new { isActive = true, expectedActive = false })).StatusCode);
        var result = purpose == "ResetPassword"
            ? await guest.PostAsJsonAsync("/api/auth/reset-password", new { email = account.Email, code, newPassword = "Blocked-recovery-password-456!" })
            : await guest.PostAsJsonAsync("/api/auth/verify-email", new { email = account.Email, code });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var scope = fixture.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = (await db.Users.FindAsync(account.Id))!;
        Assert.Null(user.EmailVerifiedAt);
        Assert.True(BCrypt.Net.BCrypt.Verify("Staff-integration-password-123!", user.PasswordHash));
    }
}
