using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Visibrick.Services.Sync;

namespace Visibrick.Services.Auth;

// Talks to Supabase Auth's REST API (https://supabase.com/docs/reference/self-hosting-auth).
// Passwords go straight to Supabase over HTTPS; nothing here stores or logs them.
public class SupabaseAuthClient
{
    private readonly HttpClient _http;

    public SupabaseAuthClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(SupabaseConfig.Url + "/auth/v1/") };
        _http.DefaultRequestHeaders.Add("apikey", SupabaseConfig.AnonKey);
    }

    public Task<AuthSession> SignInWithPasswordAsync(string email, string password)
        => PostAsync<AuthSession>("token?grant_type=password", new { email, password });

    // Returns a session if Supabase signs the user straight in, or null if it sent a confirmation email instead
    public async Task<AuthSession?> SignUpAsync(string email, string password)
    {
        var json = await PostAsync<JsonElement>("signup", new { email, password });
        return json.TryGetProperty("access_token", out _) ? json.Deserialize<AuthSession>() : null;
    }

    public Task SendPasswordResetAsync(string email)
        => PostAsync<JsonElement>("recover", new { email });

    public Task<AuthSession> RefreshAsync(string refreshToken)
        => PostAsync<AuthSession>("token?grant_type=refresh_token", new { refresh_token = refreshToken });

    // Step 1 of signing in with Google/Apple: the page to open in the browser. Supabase sends the browser back
    // to redirectUrl with ?code=..., which ExchangeCodeAsync turns into a session.
    // PKCE: we keep a random secret (the verifier) and only send its hash, so an intercepted code is useless.
    public static (Uri Url, string Verifier) CreateAuthorizeUrl(string provider, string redirectUrl)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var url = $"{SupabaseConfig.Url}/auth/v1/authorize" +
            $"?provider={Uri.EscapeDataString(provider)}" +
            $"&redirect_to={Uri.EscapeDataString(redirectUrl)}" +
            $"&code_challenge={challenge}&code_challenge_method=s256";

        return (new Uri(url), verifier);
    }

    // Step 2 of signing in with Google/Apple
    public Task<AuthSession> ExchangeCodeAsync(string code, string verifier)
        => PostAsync<AuthSession>("token?grant_type=pkce", new { auth_code = code, code_verifier = verifier });

    // Ends the session on Supabase's side too, so the refresh token can't be used again
    public async Task SignOutAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request);
    }

    // The signed-in user's latest details from the server
    public async Task<AuthUser> GetUserAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new AuthException(await ReadErrorAsync(response));

        return (await response.Content.ReadFromJsonAsync<AuthUser>())!;
    }

    // Sets a new password for the signed-in user (email accounts)
    public Task<AuthUser> UpdatePasswordAsync(string accessToken, string password)
        => SendAsync<AuthUser>(HttpMethod.Put, "user", new { password }, accessToken);

    // Permanently deletes the signed-in user's account. Calls the delete_my_account database function
    // (supabase/migrations/002_profile_settings.sql), which can only ever delete the caller's own account.
    public Task DeleteAccountAsync(string accessToken)
        => SendAsync<JsonElement>(HttpMethod.Post, SupabaseConfig.Url + "/rest/v1/rpc/delete_my_account", new { }, accessToken);

    private Task<T> PostAsync<T>(string path, object body) => SendAsync<T>(HttpMethod.Post, path, body, null);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, string? accessToken)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            if (accessToken is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new AuthException("Couldn't reach the server. Check your internet connection and try again.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AuthException(await ReadErrorAsync(response));

            // Some endpoints (e.g. recover) answer with an empty body
            var text = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(text) ? "{}" : text)!;
        }
    }

    // Supabase errors look like {"error_code":"invalid_credentials","msg":"..."} (newer) or
    // {"error":"invalid_grant","error_description":"..."} (older). Turn the common ones into friendly text.
    internal static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        string? code = null, message = null;
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            code = Get(json, "error_code") ?? Get(json, "error");
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
            _ when (int)response.StatusCode == 429 => "Too many attempts. Please wait a minute and try again.",
            _ => message ?? "Something went wrong. Please try again."
        };

        static string? Get(JsonElement json, string name)
            => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
