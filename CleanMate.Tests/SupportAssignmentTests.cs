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
    public async Task Staff_claim_release_and_queue_filters_preserve_shared_support_access()
    {
        var a=await Staff();var b=await Staff();using var first=fixture.Client(a.Token);using var second=fixture.Client(b.Token);
        using var owner=fixture.Client((await Register()).Token);var ticket=await Report(owner,await Create(owner));var path=$"/api/management/support/{ticket}/assignment";
        Assert.Equal(HttpStatusCode.OK,(await first.PutAsJsonAsync(path,new{assignedToId=a.Id,version=1})).StatusCode);
        var mine=await first.GetFromJsonAsync<JsonElement>("/api/management/support?assignment=Mine");var item=Assert.Single(mine.GetProperty("items").EnumerateArray(),t=>t.GetProperty("id").GetInt32()==ticket);
        Assert.Equal(a.Id,item.GetProperty("assignedToId").GetInt32());Assert.EndsWith("Z",item.GetProperty("assignedAt").GetString());
        Assert.DoesNotContain((await first.GetFromJsonAsync<JsonElement>("/api/management/support?assignment=Unassigned")).GetProperty("items").EnumerateArray(),t=>t.GetProperty("id").GetInt32()==ticket);
        Assert.DoesNotContain((await second.GetFromJsonAsync<JsonElement>("/api/management/support?assignment=Mine")).GetProperty("items").EnumerateArray(),t=>t.GetProperty("id").GetInt32()==ticket);
        Assert.Equal(HttpStatusCode.Forbidden,(await second.PutAsJsonAsync(path,new{assignedToId=b.Id,version=2})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await second.PutAsJsonAsync(path,new{assignedToId=(int?)null,version=2})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await first.PutAsJsonAsync(path,new{assignedToId=b.Id,version=2})).StatusCode);
        // A colleague can still assist; their message makes the assignment form's version stale.
        Assert.Equal(HttpStatusCode.OK,(await second.PostAsJsonAsync($"/api/management/support/{ticket}/messages",new{body="A colleague can assist with this report.",requestId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await first.PutAsJsonAsync(path,new{assignedToId=(int?)null,version=2})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await first.PutAsJsonAsync(path,new{assignedToId=(int?)null,version=3})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await second.PutAsJsonAsync(path,new{assignedToId=b.Id,version=4})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await first.GetAsync("/api/management/support?assignment=Unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await first.GetAsync("/api/management/support-assignees")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_claims_have_one_winner_and_one_audit()
    {
        var a=await Staff();var b=await Staff();using var first=fixture.Client(a.Token);using var second=fixture.Client(b.Token);using var owner=fixture.Client((await Register()).Token);
        var ticket=await Report(owner,await Create(owner));var path=$"/api/management/support/{ticket}/assignment";
        var results=await Task.WhenAll(first.PutAsJsonAsync(path,new{assignedToId=a.Id,version=1}),second.PutAsJsonAsync(path,new{assignedToId=b.Id,version=1}));
        Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(results,r=>r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2,(await db.SupportTickets.AsNoTracking().SingleAsync(t=>t.Id==ticket)).Version);
        Assert.Equal(1,await db.AdminAudits.CountAsync(a=>a.Target==$"Support:{ticket}"&&a.Action=="AssignSupport"));
    }

    [Fact]
    public async Task Admin_reassigns_and_unassigns_with_deduplicated_private_notification()
    {
        var a=await Staff();var b=await Staff();using var admin=fixture.Client(fixture.AdminToken);using var owner=fixture.Client((await Register()).Token);
        var ticket=await Report(owner,await Create(owner));var path=$"/api/management/support/{ticket}/assignment";
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{assignedToId=a.Id,version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{assignedToId=a.Id,version=2})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync(path,new{assignedToId=b.Id,version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync($"/api/management/staff/{a.Id}/access",new{isActive=false,expectedActive=true})).StatusCode);
        var candidates=await admin.GetFromJsonAsync<JsonElement>("/api/management/support-assignees");Assert.DoesNotContain(candidates.EnumerateArray(),u=>u.GetProperty("id").GetInt32()==a.Id);Assert.Contains(candidates.EnumerateArray(),u=>u.GetProperty("id").GetInt32()==b.Id);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{assignedToId=b.Id,version=2})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{assignedToId=(int?)null,version=3})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row=await db.SupportTickets.AsNoTracking().SingleAsync(t=>t.Id==ticket);Assert.Null(row.AssignedToId);Assert.Null(row.AssignedAt);Assert.Equal(4,row.Version);
        Assert.Equal(3,await db.AdminAudits.CountAsync(x=>x.Target==$"Support:{ticket}"&&x.Action=="AssignSupport"));
        var notices=await db.Notifications.Where(n=>n.EventKey.StartsWith($"SupportAssigned:{ticket}:")).ToListAsync();Assert.Equal(2,notices.Count);
        Assert.All(notices,n=>{Assert.Equal(ticket,n.TargetId);Assert.Equal("Support",n.Target);Assert.DoesNotContain(a.Email,n.Message);Assert.DoesNotContain(b.Email,n.Message);});
        var publicList=await owner.GetFromJsonAsync<JsonElement>("/api/support");Assert.False(publicList.GetProperty("items")[0].TryGetProperty("assignedToId",out _));
    }

    [Fact]
    public async Task Assignment_rejects_public_roles_inactive_targets_self_reports_and_invalid_versions()
    {
        var person=await Register();using var owner=fixture.Client(person.Token);using var admin=fixture.Client(fixture.AdminToken);using var guest=fixture.Client();
        var staff=await Staff();using var manager=fixture.Client(staff.Token);var ticket=await Report(owner,await Create(owner));var path=$"/api/management/support/{ticket}/assignment";
        Assert.Equal(HttpStatusCode.Unauthorized,(await guest.PutAsJsonAsync(path,new{assignedToId=staff.Id,version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.PutAsJsonAsync(path,new{assignedToId=staff.Id,version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync("/api/management/support-assignees")).StatusCode);
        foreach(var target in new[]{person.Id,-1,int.MaxValue})Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(path,new{assignedToId=target,version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(path,new{assignedToId=staff.Id,version=0})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.PutAsJsonAsync("/api/management/support/2147483647/assignment",new{assignedToId=staff.Id,version=1})).StatusCode);
        var ownTicket=await Report(manager,await Create(manager));Assert.Equal(HttpStatusCode.Forbidden,(await manager.PutAsJsonAsync($"/api/management/support/{ownTicket}/assignment",new{assignedToId=staff.Id,version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync($"/api/management/support/{ownTicket}/assignment",new{assignedToId=staff.Id,version=1})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync($"/api/management/staff/{staff.Id}/access",new{isActive=false,expectedActive=true})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PutAsJsonAsync(path,new{assignedToId=staff.Id,version=1})).StatusCode);
    }

    [Fact]
    public async Task Failed_assignment_notification_rolls_back_assignment_version_and_audit()
    {
        var staff=await Staff();using var admin=fixture.Client(fixture.AdminToken);using var owner=fixture.Client((await Register()).Token);var ticket=await Report(owner,await Create(owner));
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var name="RejectAssignment_"+Guid.NewGuid().ToString("N");
        var sql=$"ALTER TABLE [Notifications] WITH NOCHECK ADD CONSTRAINT [{name}] CHECK ([UserId] <> {staff.Id} OR [EventKey] NOT LIKE 'SupportAssigned:{ticket}:%')";await db.Database.ExecuteSqlRawAsync(sql);
        var path=$"/api/management/support/{ticket}/assignment";
        try{
            Assert.Equal(HttpStatusCode.InternalServerError,(await admin.PutAsJsonAsync(path,new{assignedToId=staff.Id,version=1})).StatusCode);
            var row=await db.SupportTickets.AsNoTracking().SingleAsync(t=>t.Id==ticket);Assert.Null(row.AssignedToId);Assert.Equal(1,row.Version);
            Assert.False(await db.AdminAudits.AnyAsync(a=>a.Target==$"Support:{ticket}"&&a.Action=="AssignSupport"));
        }finally{var drop=$"ALTER TABLE [Notifications] DROP CONSTRAINT [{name}]";await db.Database.ExecuteSqlRawAsync(drop);}
        Assert.Equal(HttpStatusCode.OK,(await admin.PutAsJsonAsync(path,new{assignedToId=staff.Id,version=1})).StatusCode);
    }
}
