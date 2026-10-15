using System.Text.Json.Serialization;

namespace Visibrick.Services.Auth;

// A signed-in session as Supabase Auth returns it. Stored in SecureStorage between launches.
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
    public Dictionary<string, object?>? Metadata { get; set; }

    // Set by Supabase, e.g. providers: ["email"] or ["google"]
    [JsonPropertyName("app_metadata")]
    public Dictionary<string, object?>? AppMetadata { get; set; }

    // Name from Google/Apple if they provided one, otherwise the email address
    [JsonIgnore]
    public string DisplayName => ProviderName ?? Email ?? "your account";

    [JsonIgnore]
    public string? ProviderName => Text(Metadata, "full_name") ?? Text(Metadata, "name");

    // Profile picture from Google, if any (Apple doesn't share one)
    [JsonIgnore]
    public string? ProviderAvatarUrl => Text(Metadata, "avatar_url") ?? Text(Metadata, "picture");

    // How this account signs in: "email", "google", "apple" (an account can have more than one)
    [JsonIgnore]
    public IReadOnlyList<string> Providers =>
        AppMetadata?.GetValueOrDefault("providers") is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } list
            ? list.EnumerateArray().Select(p => p.GetString() ?? "").Where(p => p.Length > 0).ToList()
            : Text(AppMetadata, "provider") is { } single ? [single] : [];

    [JsonIgnore]
    public bool HasPassword => Providers.Contains("email");

    private static string? Text(Dictionary<string, object?>? values, string key)
        => values?.GetValueOrDefault(key)?.ToString() is { Length: > 0 } text ? text : null;
}

// What happened after "Create account"
public enum SignUpOutcome
{
    // Signed straight in (email confirmation is switched off in Supabase)
    SignedIn,

    // A confirmation email was sent; the user signs in after clicking the link
    ConfirmationEmailSent
}

// A sign-in problem with a message that's safe to show to the user
public class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
}
