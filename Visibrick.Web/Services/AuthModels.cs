using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visibrick.Web.Services;

// A signed-in session as Supabase Auth returns it. Kept in the browser's local storage between visits.
public class AuthSession
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    // Unix time (seconds) when the access token stops working
    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public AuthUser User { get; set; } = new();

    // Within 3 minutes of expiring (AuthService schedules its refresh 2 minutes before, so that falls inside this)
    [JsonIgnore]
    public bool IsExpiringSoon => DateTimeOffset.UtcNow.ToUnixTimeSeconds() > ExpiresAt - 180;
}

public class AuthUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    // What the sign-in provider shared, e.g. full_name and avatar_url from Google
    [JsonPropertyName("user_metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    // Set by Supabase, e.g. providers: ["email"] or ["google"]
    [JsonPropertyName("app_metadata")]
    public Dictionary<string, JsonElement>? AppMetadata { get; set; }

    [JsonIgnore]
    public string DisplayName => ProviderName ?? Email ?? "your account";

    [JsonIgnore]
    public string? ProviderName => Text(Metadata, "full_name") ?? Text(Metadata, "name");

    // Profile picture from Google, if any (Apple doesn't share one)
    [JsonIgnore]
    public string? ProviderAvatarUrl => Text(Metadata, "avatar_url") ?? Text(Metadata, "picture");

    // How this account signs in: "email", "google", "apple"
    [JsonIgnore]
    public IReadOnlyList<string> Providers =>
        AppMetadata?.GetValueOrDefault("providers") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray().Select(p => p.GetString() ?? "").Where(p => p.Length > 0).ToList()
            : Text(AppMetadata, "provider") is { } single ? [single] : [];

    [JsonIgnore]
    public bool HasPassword => Providers.Contains("email");

    private static string? Text(Dictionary<string, JsonElement>? values, string key)
        => values?.GetValueOrDefault(key) is { ValueKind: JsonValueKind.String } value && value.GetString() is { Length: > 0 } text ? text : null;
}

// What the /auth/callback page found when Supabase sent the browser back
public enum AuthCallbackResult
{
    SignedIn,

    // Came from a "reset your password" email: show the page to choose a new one
    PasswordRecovery,

    Failed
}
