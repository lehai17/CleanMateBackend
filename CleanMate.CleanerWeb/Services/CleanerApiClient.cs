using System.Net.Http.Headers;
using CleanMate.CleanerWeb.Models;

namespace CleanMate.CleanerWeb.Services;

public class CleanerApiClient(HttpClient client, IHttpContextAccessor accessor)
{
    public async Task<ApiAuth?> AuthenticateAsync(string path, object payload)
    {
        using var response = await client.PostAsJsonAsync(path, payload);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ApiAuth>();
    }

    public Task<ApiUser> GetProfileAsync() => GetAsync<ApiUser>("api/me");
    public Task<List<BookingViewModel>> GetJobsAsync(string kind) =>
        GetAsync<List<BookingViewModel>>($"api/cleaner/jobs/{kind}");

    private async Task<T> GetAsync<T>(string path)
    {
        using var request = Authorized(HttpMethod.Get, path);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new HttpRequestException("API returned no data.");
    }

    public async Task<bool> AcceptAsync(int id)
    {
        using var request = Authorized(HttpMethod.Put, $"api/cleaner/jobs/{id}/accept");
        using var response = await client.SendAsync(request);
        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task UpdateProfileAsync(object payload)
    {
        using var request = Authorized(HttpMethod.Put, "api/cleaner/profile");
        request.Content = JsonContent.Create(payload);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            accessor.HttpContext?.Session.GetString("AccessToken"));
        return request;
    }
}