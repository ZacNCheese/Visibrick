using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Visibrick.Services.Sync;

namespace Visibrick.Services.Auth;

// The signed-in user's profile (display name, picture) from the Supabase "profiles" table, plus the
// profile picture in Supabase Storage. Requests use the user's own access token, so the database's
// Row Level Security only lets them read and change their own profile.
public class ProfileService
{
    // Profile pictures larger than this are refused (the "avatars" storage bucket enforces the same limit)
    public const long MaxPhotoBytes = 5 * 1024 * 1024;

    private const string AvatarBucket = "avatars";

    private readonly AuthService _auth;
    private readonly HttpClient _http;
    private Profile? _profile;

    public ProfileService(AuthService auth)
    {
        _auth = auth;
        _http = new HttpClient { BaseAddress = new Uri(SupabaseConfig.Url + "/") };
        _http.DefaultRequestHeaders.Add("apikey", SupabaseConfig.AnonKey);

        _auth.Changed += async (_, _) =>
        {
            if (_auth.IsSignedIn)
                await LoadAsync();
            else
                SetProfile(null);
        };

        // Created on first use, possibly after the user was already signed in: load for them now
        if (_auth.IsSignedIn)
            _ = LoadAsync();
    }

    // The profile changed (loaded, edited, or signed out). Raised on the main thread.
    public event EventHandler? Changed;

    // Display name: the one the user set, else the name from Google/Apple, else their email
    public string? DisplayName => _profile?.DisplayName is { Length: > 0 } name ? name : _auth.CurrentUser?.DisplayName;

    // Uploaded picture, else Google's picture, else null (show initials)
    public string? AvatarUrl => _profile?.AvatarUrl ?? (_profile is null ? _auth.CurrentUser?.ProviderAvatarUrl : null);

    // Whether the picture is one the user uploaded (so it can be removed)
    public bool HasUploadedAvatar => _profile?.AvatarUrl?.Contains($"/{AvatarBucket}/") == true;

    public string Initials => MakeInitials(DisplayName);

    public async Task LoadAsync()
    {
        try
        {
            var user = _auth.CurrentUser;
            if (user is null)
                return;

            var rows = await SendAsync<List<Profile>>(HttpMethod.Get, $"rest/v1/profiles?id=eq.{user.Id}&select=*");

            // Accounts created before the profiles table existed have no row yet: create one
            var profile = rows.FirstOrDefault() ?? (await SendAsync<List<Profile>>(HttpMethod.Post, "rest/v1/profiles", new Profile
            {
                Id = user.Id,
                DisplayName = user.ProviderName,
                AvatarUrl = user.ProviderAvatarUrl
            })).First();

            SetProfile(profile);
        }
        catch (Exception ex)
        {
            // No profile row yet or offline: fall back to what the sign-in provider shared
            Debug.WriteLine("PROFILE LOAD FAILED: " + ex.Message);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task UpdateDisplayNameAsync(string displayName)
    {
        var name = displayName.Trim();
        await UpdateAsync(new { display_name = name.Length == 0 ? null : name, updated_at = DateTimeOffset.UtcNow });
    }

    // Uploads a new profile picture to avatars/<user id>/avatar.<ext> and saves its address on the profile
    public async Task UploadAvatarAsync(FileResult photo)
    {
        var user = _auth.CurrentUser ?? throw new AuthException("Please sign in again.");

        var extension = Path.GetExtension(photo.FileName).ToLowerInvariant() switch
        {
            ".png" => "png",
            ".webp" => "webp",
            ".jpg" or ".jpeg" => "jpg",
            _ => throw new AuthException("Please choose a JPG, PNG or WebP picture.")
        };

        await using var stream = await photo.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        if (buffer.Length > MaxPhotoBytes)
            throw new AuthException("That picture is too large. Please choose one under 5 MB.");

        var path = $"{user.Id}/avatar.{extension}";
        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(extension == "jpg" ? "image/jpeg" : "image/" + extension);

        using var request = await CreateRequestAsync(HttpMethod.Post, $"storage/v1/object/{AvatarBucket}/{path}");
        request.Content = content;
        request.Headers.Add("x-upsert", "true");
        await SendAsync(request);

        // The ?v= part makes apps load the new picture instead of a cached copy of the old one
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
        if (!HasUploadedAvatar || _profile?.AvatarUrl is not { } url)
            return;

        var path = new Uri(url).AbsolutePath.Split($"/{AvatarBucket}/", 2)[1];
        using var request = await CreateRequestAsync(HttpMethod.Delete, $"storage/v1/object/{AvatarBucket}");
        request.Content = JsonContent.Create(new { prefixes = new[] { path } });

        try { await SendAsync(request); }
        catch (Exception ex) { Debug.WriteLine("AVATAR DELETE FAILED: " + ex.Message); }
    }

    private async Task UpdateAsync(object changes)
    {
        var user = _auth.CurrentUser ?? throw new AuthException("Please sign in again.");
        var rows = await SendAsync<List<Profile>>(HttpMethod.Patch, $"rest/v1/profiles?id=eq.{user.Id}", changes);

        if (rows.Count == 0)
            throw new AuthException("Your profile isn't set up yet. Please try again in a moment.");

        SetProfile(rows[0]);
    }

    private void SetProfile(Profile? profile)
    {
        _profile = profile;
        MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = await CreateRequestAsync(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        // Ask the database to send back the saved row(s)
        request.Headers.Add("Prefer", "return=representation");

        using var response = await SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<T>())!;
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
            throw new AuthException("Couldn't reach the server. Check your internet connection and try again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = await SupabaseAuthClient.ReadErrorAsync(response);
            response.Dispose();
            throw new AuthException(message);
        }

        return response;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path)
    {
        var token = await _auth.GetAccessTokenAsync() ?? throw new AuthException("Please sign in again.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string MakeInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        // An email address: use the first letter of the part before the @
        if (name.Contains('@'))
            return name[..1].ToUpperInvariant();

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    private class Profile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }
    }
}
