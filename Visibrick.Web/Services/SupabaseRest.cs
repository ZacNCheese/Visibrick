using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Visibrick.Web.Services;

// Calls Supabase's REST APIs. Public data (the catalog) uses just the anon key; the user's own data adds their
// access token, so the database's Row Level Security only lets them reach their own rows.
public class SupabaseRest
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(SupabaseConfig.Url + "/") };

    public SupabaseRest()
    {
        _http.DefaultRequestHeaders.Add("apikey", SupabaseConfig.AnonKey);
    }

    // A page of rows, plus the total number of matching rows (from Supabase's Content-Range header)
    public async Task<(List<T> Rows, int Total)> GetPageAsync<T>(string path, string? accessToken = null)
    {
        using var request = Create(HttpMethod.Get, path, accessToken);
        request.Headers.Add("Prefer", "count=exact");

        using var response = await SendAsync(request);
        var rows = (await response.Content.ReadFromJsonAsync<List<T>>())!;

        // e.g. "0-59/19288"
        var range = response.Content.Headers.TryGetValues("Content-Range", out var values) ? values.First() : "";
        var total = int.TryParse(range.Split('/').LastOrDefault(), out var count) ? count : rows.Count;
        return (rows, total);
    }

    public async Task<T?> GetSingleAsync<T>(string path, string? accessToken = null)
    {
        using var request = Create(HttpMethod.Get, path, accessToken);
        using var response = await SendAsync(request);
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<List<T>> GetAsync<T>(string path, string? accessToken = null)
    {
        using var request = Create(HttpMethod.Get, path, accessToken);
        using var response = await SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<List<T>>())!;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, object? body, string? accessToken,
        string? prefer = null, HttpContent? content = null, IDictionary<string, string>? headers = null)
    {
        var request = Create(method, path, accessToken);
        if (prefer is not null)
            request.Headers.Add("Prefer", prefer);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            request.Headers.Add(name, value);
        if (content is not null)
            request.Content = content;
        else if (body is not null)
            request.Content = JsonContent.Create(body);

        return await SendAsync(request);
    }

    private static HttpRequestMessage Create(HttpMethod method, string path, string? accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new AppException("Couldn't reach the server. Check your internet connection and try again.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        var message = await ReadErrorAsync(response);
        response.Dispose();
        throw new AppException(message);
    }

    // Turns Supabase errors into friendly text. Auth errors look like {"error_code":"invalid_credentials","msg":"..."}
    // (or the older {"error":"...","error_description":"..."}); database errors look like {"code":"42P01","message":"..."}.
    public static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        string? code = null, message = null;
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            code = Get(json, "error_code") ?? Get(json, "error") ?? Get(json, "code");
            message = Get(json, "msg") ?? Get(json, "error_description") ?? Get(json, "message");
        }
        catch (JsonException)
        {
        }

        return code switch
        {
            "invalid_credentials" or "invalid_grant" => "That email and password don't match. Check them and try again.",
            "email_not_confirmed" => "Please confirm your email first. Check your inbox for the link we sent.",
            "user_already_exists" or "email_exists" => "There's already an account with that email. Try signing in instead.",
            "weak_password" => message ?? "Please choose a stronger password.",
            "same_password" => "That's already your password. Choose a different one.",
            "reauthentication_needed" => "For security, sign out and sign back in, then change your password.",
            "over_request_rate_limit" or "over_email_send_rate_limit" => "Too many attempts. Please wait a minute and try again.",
            "signup_disabled" => "New sign-ups are turned off right now.",
            "validation_failed" => message ?? "Please check the email address.",
            "42P01" or "PGRST205" => "Part of the website isn't set up on the server yet (run the scripts in supabase/migrations).",
            _ when (int)response.StatusCode == 429 => "Too many attempts. Please wait a minute and try again.",
            _ when (int)response.StatusCode is 401 or 403 => "Please sign in again.",
            _ => message ?? "Something went wrong. Please try again."
        };

        static string? Get(JsonElement json, string name)
            => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}

// A problem with a message that's safe to show to the user
public class AppException(string message) : Exception(message);
