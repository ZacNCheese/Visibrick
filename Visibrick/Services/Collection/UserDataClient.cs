using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Visibrick.Services.Auth;
using Visibrick.Services.Sync;

namespace Visibrick.Services.Collection;

// Reads and writes the signed-in user's own rows in Supabase (e.g. their collection). Every request carries
// the user's access token, so the database's Row Level Security only lets them touch their own data.
public class UserDataClient
{
    private readonly AuthService _auth;
    private readonly HttpClient _http;

    public UserDataClient(AuthService auth)
    {
        _auth = auth;
        _http = new HttpClient { BaseAddress = new Uri(SupabaseConfig.Url + "/rest/v1/") };
        _http.DefaultRequestHeaders.Add("apikey", SupabaseConfig.AnonKey);
    }

    public async Task<List<T>> GetAsync<T>(string pathAndQuery)
    {
        using var response = await SendAsync(HttpMethod.Get, pathAndQuery, body: null);
        return (await response.Content.ReadFromJsonAsync<List<T>>())!;
    }

    // Insert, or update the existing row with the same values in the onConflict columns
    public async Task UpsertAsync(string table, object row, string onConflict)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{table}?on_conflict={onConflict}", row,
            prefer: "resolution=merge-duplicates,return=minimal");
    }

    public async Task DeleteAsync(string pathAndQuery)
    {
        using var response = await SendAsync(HttpMethod.Delete, pathAndQuery, body: null);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? prefer = null)
    {
        var token = await _auth.GetAccessTokenAsync() ?? throw new AuthException("Please sign in again.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (prefer is not null)
            request.Headers.Add("Prefer", prefer);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new AuthException("Couldn't reach the server. Check your internet connection and try again.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
            throw new AuthException(await ReadErrorAsync(response));
    }

    // Database errors look like {"code":"42P01","message":"relation ... does not exist"}
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var code = json.TryGetProperty("code", out var c) ? c.ToString() : null;

            // The table doesn't exist yet: the matching supabase/migrations script hasn't been run
            if (code is "42P01" or "PGRST205")
                return "Collections aren't set up on the server yet (run supabase/migrations/003_collection.sql).";
        }
        catch (JsonException)
        {
        }

        return (int)response.StatusCode is 401 or 403
            ? "Please sign in again."
            : "Something went wrong saving your collection. Please try again.";
    }
}
