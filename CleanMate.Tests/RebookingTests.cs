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
    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task Rebooking_template_preserves_contact_and_notes_without_copying_job_or_cash_state(string status)
    {
        var account=await Register();using var owner=fixture.Client(account.Token);using var cleaner=fixture.Client((await Register("Cleaner")).Token);
        var created=await owner.PostAsJsonAsync("/api/bookings",Booking(contactName:"Person at booking",contactPhone:"0907777777"));
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        if(status=="Cancelled")Assert.Equal(HttpStatusCode.NoContent,(await owner.PutAsync($"/api/orders/{id}/cancel",null)).StatusCode);
        else foreach(var action in new[]{"accept","start","complete","cash-payment"})Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}",null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await owner.PutAsJsonAsync("/api/profile",new{fullName="Changed profile",phoneNumber="0909999999",address="Different profile address"})).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notices=await db.Notifications.CountAsync();var payments=await db.Payments.CountAsync(p=>p.BookingId==id);
        for(var i=0;i<2;i++){
            var response=await owner.GetAsync($"/api/orders/{id}/rebooking");Assert.Equal(HttpStatusCode.OK,response.StatusCode);var data=await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(id,data.GetProperty("sourceId").GetInt32());Assert.Equal(account.Id,data.GetProperty("ownerId").GetInt32());Assert.Equal("cleaning",data.GetProperty("serviceKey").GetString());Assert.Equal(2,data.GetProperty("durationHours").GetInt32());
            Assert.Equal("Person at booking",data.GetProperty("contactName").GetString());Assert.Equal("0907777777",data.GetProperty("contactPhone").GetString());Assert.Equal("123 Test Street",data.GetProperty("address").GetString());Assert.Equal("Test booking",data.GetProperty("notes").GetString());
            foreach(var field in new[]{"price","startTime","date","cleaner","cleanerId","paymentStatus","paidAt","requestId","status"})Assert.False(data.TryGetProperty(field,out _));
        }
        Assert.Equal(status,(await db.Bookings.AsNoTracking().SingleAsync(b=>b.Id==id)).Status);Assert.Equal(notices,await db.Notifications.CountAsync());Assert.Equal(payments,await db.Payments.CountAsync(p=>p.BookingId==id));
    }

    [Fact]
    public async Task Rebooking_template_is_owner_only_even_for_assigned_cleaner_and_managers()
    {
        using var owner=fixture.Client((await Register()).Token);using var stranger=fixture.Client((await Register()).Token);using var cleaner=fixture.Client((await Register("Cleaner")).Token);using var admin=fixture.Client(fixture.AdminToken);using var guest=fixture.Client();
        var id=await Create(owner);foreach(var action in new[]{"accept","start","complete"})Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{id}/{action}",null)).StatusCode);
        var path=$"/api/orders/{id}/rebooking";Assert.Equal(HttpStatusCode.Unauthorized,(await guest.GetAsync(path)).StatusCode);
        foreach(var other in new[]{stranger,cleaner,admin})Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync("/api/orders/2147483647/rebooking")).StatusCode);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Accepted")]
    [InlineData("InProgress")]
    public async Task Active_orders_cannot_be_rebooked(string status)
    {
        using var owner=fixture.Client((await Register()).Token);using var cleaner=fixture.Client((await Register("Cleaner")).Token);var id=await Create(owner);
        if(status!="Pending")Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{id}/accept",null)).StatusCode);
        if(status=="InProgress")Assert.Equal(HttpStatusCode.OK,(await cleaner.PutAsync($"/api/cleaner/jobs/{id}/start",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await owner.GetAsync($"/api/orders/{id}/rebooking")).StatusCode);
    }

    [Theory]
    [InlineData("Legacy notes without a service marker",null,"Legacy notes without a service marker")]
    [InlineData("[cleaning] [cooking] Customer's own note","cleaning","[cooking] Customer's own note")]
    [InlineData("[not a key] Keep original text",null,"[not a key] Keep original text")]
    [InlineData("[Reminder] Keep original text",null,"[Reminder] Keep original text")]
    public async Task Legacy_service_markers_are_parsed_once_without_inventing_a_service(string stored,string? serviceKey,string notes)
    {
        using var owner=fixture.Client((await Register()).Token);var id=await Create(owner);Assert.Equal(HttpStatusCode.NoContent,(await owner.PutAsync($"/api/orders/{id}/cancel",null)).StatusCode);
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Bookings.Where(b=>b.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(b=>b.Notes,stored));
        var template=await owner.GetFromJsonAsync<JsonElement>($"/api/orders/{id}/rebooking");Assert.Equal(serviceKey,template.GetProperty("serviceKey").GetString());Assert.Equal(notes,template.GetProperty("notes").GetString());
    }
}
