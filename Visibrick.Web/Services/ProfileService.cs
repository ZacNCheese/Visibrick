using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;

namespace Visibrick.Web.Services;

// The signed-in user's profile (display name, picture) from the Supabase "profiles" table, plus the picture
// in Supabase Storage. Requests use the user's own access token, so Row Level Security only lets them reach
// their own profile.
public class ProfileService
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;
    private const string AvatarBucket = "avatars";

    private readonly AuthService _auth;
    private readonly SupabaseRest _rest;
    private Profile? _profile;
    private string? _loadedUserId;

    public ProfileService(AuthService auth, SupabaseRest rest)
    {
        _auth = auth;
        _rest = rest;
        _auth.Changed += () => _ = LoadForCurrentUserAsync();
    }

    public event Action? Changed;

    // The name the user set, else the name from Google/Apple, else their email
    public string? DisplayName => _profile?.DisplayName is { Length: > 0 } name ? name : _auth.CurrentUser?.DisplayName;

    // The name the user set themselves (for the edit box)
    public string? CustomName => _profile?.DisplayName;

    // Uploaded picture, else Google's picture, else null (show initials)
    public string? AvatarUrl => _profile?.AvatarUrl ?? (_profile is null ? _auth.CurrentUser?.ProviderAvatarUrl : null);

    public string Initials
    {
        get
        {
            var name = DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                return "";

            if (name.Contains('@'))
                return name[..1].ToUpperInvariant();

            return string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    public Task EnsureLoadedAsync() => LoadForCurrentUserAsync();

    private async Task LoadForCurrentUserAsync()
    {
        var user = _auth.CurrentUser;
        if (user?.Id == _loadedUserId)
            return;

        _loadedUserId = user?.Id;
        _profile = null;

        if (user is not null)
        {
            try
            {
                var token = await _auth.GetAccessTokenAsync();
                var rows = await _rest.GetAsync<Profile>($"rest/v1/profiles?id=eq.{user.Id}&select=*", token);

                // Accounts created before the profiles table existed have no row yet: create one
                _profile = rows.FirstOrDefault() ?? await SendAsync(HttpMethod.Post, "rest/v1/profiles",
                    new { id = user.Id, display_name = user.ProviderName, avatar_url = user.ProviderAvatarUrl });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Loading the profile failed: " + ex.Message);
            }
        }

        Changed?.Invoke();
    }

    public async Task UpdateDisplayNameAsync(string displayName)
    {
        var name = displayName.Trim();
        await UpdateAsync(new { display_name = name.Length == 0 ? null : name, updated_at = DateTimeOffset.UtcNow });
    }

    // Uploads a new picture to avatars/<user id>/avatar.<ext> and saves its address on the profile
    public async Task UploadAvatarAsync(IBrowserFile file)
    {
        var user = _auth.CurrentUser ?? throw new AppException("Please sign in again.");

        var extension = file.ContentType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            "image/jpeg" => "jpg",
            _ => throw new AppException("Please choose a JPG, PNG or WebP picture.")
        };

        if (file.Size > MaxPhotoBytes)
            throw new AppException("That picture is too large. Please choose one under 5 MB.");

        using var buffer = new MemoryStream();
        await file.OpenReadStream(MaxPhotoBytes).CopyToAsync(buffer);

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

        var path = $"{user.Id}/avatar.{extension}";
        var token = await _auth.GetAccessTokenAsync();
        using var _ = await _rest.SendAsync(HttpMethod.Post, $"storage/v1/object/{AvatarBucket}/{path}", null, token,
            content: content, headers: new Dictionary<string, string> { ["x-upsert"] = "true" });

        // The ?v= part makes browsers load the new picture instead of a cached copy of the old one
        var url = $"{SupabaseConfig.Url}/storage/v1/object/public/{AvatarBucket}/{path}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        await UpdateAsync(new { avatar_url = url, updated_at = DateTimeOffset.UtcNow });
    }

    public async Task RemoveAvatarAsync()
    {
        await DeleteUploadedAvatarAsync();
        await UpdateAsync(new { avatar_url = (string?)null, updated_at = DateTimeOffset.UtcNow });
    }

    // Before deleting an account: remove its uploaded picture (storage files aren't removed by the database)
    public async Task DeleteUploadedAvatarAsync()
    {
        if (_profile?.AvatarUrl is not { } url || !url.Contains($"/{AvatarBucket}/"))
            return;

        var path = new Uri(url).AbsolutePath.Split($"/{AvatarBucket}/", 2)[1];
        try
        {
            var token = await _auth.GetAccessTokenAsync();
            using var _ = await _rest.SendAsync(HttpMethod.Delete, $"storage/v1/object/{AvatarBucket}", new { prefixes = new[] { path } }, token);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Deleting the profile picture failed: " + ex.Message);
        }
    }

    private async Task UpdateAsync(object changes)
    {
        var user = _auth.CurrentUser ?? throw new AppException("Please sign in again.");
        _profile = await SendAsync(HttpMethod.Patch, $"rest/v1/profiles?id=eq.{user.Id}", changes)
            ?? throw new AppException("Your profile isn't set up yet. Please try again in a moment.");
        Changed?.Invoke();
    }

    private async Task<Profile?> SendAsync(HttpMethod method, string path, object body)
    {
        var token = await _auth.GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");
        using var response = await _rest.SendAsync(method, path, body, token, prefer: "return=representation");
        return (await response.Content.ReadFromJsonAsync<List<Profile>>())?.FirstOrDefault();
    }

    private class Profile
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }
    }
}
