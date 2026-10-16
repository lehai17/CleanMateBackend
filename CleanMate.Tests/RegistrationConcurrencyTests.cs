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
    public async Task Concurrent_cleaner_signups_with_many_staff_keep_one_profile_and_each_notification()
    {
        var managers = new List<int>();
        for(var i=0;i<8;i++)managers.Add((await Staff()).Id);
        var emails=Enumerable.Range(0,8).Select(_=>Guid.NewGuid()+"@example.test").ToArray();
        var registrations=await Task.WhenAll(emails.Select(async email=>{
            using var client=fixture.Client();var response=await client.PostAsJsonAsync("/api/auth/register",new{fullName="Concurrent Applicant",email,password=Password,role="Cleaner"});
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadFromJsonAsync<JsonElement>();
        }));
        using var scope=fixture.Api.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach(var result in registrations){
            var user=result.GetProperty("user");var id=user.GetProperty("id").GetInt32();var profile=user.GetProperty("cleanerProfile").GetProperty("id").GetInt32();
            Assert.Equal(1,await db.Users.CountAsync(u=>u.Email==user.GetProperty("email").GetString()));Assert.Equal(1,await db.CleanerProfiles.CountAsync(p=>p.UserId==id));
            foreach(var manager in managers)Assert.Equal(1,await db.Notifications.CountAsync(n=>n.UserId==manager&&n.EventKey==$"ApplicationSubmitted:{profile}:initial"&&n.TargetId==profile));
        }
    }
}
