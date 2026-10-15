using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Visibrick.Web.Services;

// The signed-in user for the whole website. Talks to Supabase Auth, keeps the session in the browser's
// local storage, refreshes the short-lived access token before it expires, and tells pages when the user
// signs in or out. Passwords go straight to Supabase over HTTPS; nothing here stores or logs them.
public class AuthService
{
    private const string SessionKey = "visibrick.session";

    // The PKCE secret for a Google/Apple sign-in in progress (kept while the browser visits Google/Apple)
    private const string VerifierKey = "visibrick.pkce";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly IJSRuntime _js;
    private readonly NavigationManager _navigation;
    private readonly SupabaseRest _rest;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AuthSession? _session;
    private Timer? _refreshTimer;
    private Task? _initializing;

    public AuthService(IJSRuntime js, NavigationManager navigation, SupabaseRest rest)
    {
        _js = js;
        _navigation = navigation;
        _rest = rest;
    }

    public AuthUser? CurrentUser => _session?.User;

    public bool IsSignedIn => _session is not null;

    // Signed in, signed out, or the user's details changed
    public event Action? Changed;

    // Where Supabase sends the browser back to after Google/Apple sign-in and email links.
    // This address must be listed in Supabase: Authentication → URL Configuration → Redirect URLs.
    private string CallbackUrl => _navigation.BaseUri + "auth/callback";

    // Restores the saved session on page load. Safe to call from every page; it only runs once.
    public Task InitializeAsync() => _initializing ??= RestoreAsync();

    private async Task RestoreAsync()
    {
        try
        {
            var saved = await _js.InvokeAsync<string?>("localStorage.getItem", SessionKey);
            if (saved is null)
                return;

            _session = JsonSerializer.Deserialize<AuthSession>(saved);
            if (_session?.IsExpiringSoon == true)
                await RefreshAsync();
            else
                ScheduleRefresh();

            // Pick up changes made elsewhere (name, email, how they sign in)
            if (_session is not null && await GetAccessTokenAsync() is { } token)
            {
                var user = await GetUserAsync(token);
                await SetSessionAsync(new AuthSession { AccessToken = _session.AccessToken, RefreshToken = _session.RefreshToken, ExpiresAt = _session.ExpiresAt, User = user });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Restoring the session failed: " + ex.Message);
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    public async Task SignInWithEmailAsync(string email, string password)
        => await SetSessionAsync(await PostAsync<AuthSession>("auth/v1/token?grant_type=password", new { email = email.Trim(), password }));

    // Returns true if signed straight in, false if a confirmation email was sent instead
    public async Task<bool> SignUpWithEmailAsync(string email, string password)
    {
        var json = await PostAsync<JsonElement>($"auth/v1/signup?redirect_to={Uri.EscapeDataString(CallbackUrl)}", new { email = email.Trim(), password });
        if (!json.TryGetProperty("access_token", out _))
            return false;

        await SetSessionAsync(json.Deserialize<AuthSession>()!);
        return true;
    }

    // Supabase replies the same way whether or not the email has an account, so this can't be used to find
    // out who has signed up. The emailed link comes back to /auth/callback, which opens the new-password page.
    public Task SendPasswordResetAsync(string email)
        => PostAsync<JsonElement>($"auth/v1/recover?redirect_to={Uri.EscapeDataString(CallbackUrl)}", new { email = email.Trim() });

    // provider: "google" or "apple". Leaves the website; Supabase brings the browser back to /auth/callback.
    // PKCE: we keep a random secret (the verifier) and only send its hash, so an intercepted code is useless.
    public async Task StartProviderSignInAsync(string provider)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        await _js.InvokeVoidAsync("localStorage.setItem", VerifierKey, verifier);

        _navigation.NavigateTo(
            $"{SupabaseConfig.Url}/auth/v1/authorize?provider={Uri.EscapeDataString(provider)}" +
            $"&redirect_to={Uri.EscapeDataString(CallbackUrl)}&code_challenge={challenge}&code_challenge_method=s256",
            forceLoad: true);
    }

    // Called by the /auth/callback page with the address Supabase sent the browser to:
    //   ?code=...               after Google/Apple (PKCE)
    //   #access_token=...&type=  after clicking a confirmation or password-reset email
    //   ?error_description=...   if something went wrong
    public async Task<(AuthCallbackResult Result, string? Error)> HandleCallbackAsync(string url)
    {
        var uri = new Uri(url);
        var query = QueryString.Parse(uri.Query);
        var fragment = QueryString.Parse(uri.Fragment);

        var error = query.GetValueOrDefault("error_description") ?? fragment.GetValueOrDefault("error_description");
        if (error is not null)
            return (AuthCallbackResult.Failed, error);

        try
        {
            if (query.TryGetValue("code", out var code))
            {
                var verifier = await _js.InvokeAsync<string?>("localStorage.getItem", VerifierKey);
                await _js.InvokeVoidAsync("localStorage.removeItem", VerifierKey);
                if (verifier is null)
                    return (AuthCallbackResult.Failed, "That sign-in link has already been used. Please try again.");

                await SetSessionAsync(await PostAsync<AuthSession>("auth/v1/token?grant_type=pkce", new { auth_code = code, code_verifier = verifier }));
                return (AuthCallbackResult.SignedIn, null);
            }

            if (fragment.TryGetValue("access_token", out var accessToken) && fragment.TryGetValue("refresh_token", out var refreshToken))
            {
                var expiresAt = long.TryParse(fragment.GetValueOrDefault("expires_at"), out var at)
                    ? at
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long.TryParse(fragment.GetValueOrDefault("expires_in"), out var seconds) ? seconds : 3600);

                var user = await GetUserAsync(accessToken);
                await SetSessionAsync(new AuthSession { AccessToken = accessToken, RefreshToken = refreshToken, ExpiresAt = expiresAt, User = user });

                return fragment.GetValueOrDefault("type") == "recovery"
                    ? (AuthCallbackResult.PasswordRecovery, null)
                    : (AuthCallbackResult.SignedIn, null);
            }

            return (AuthCallbackResult.Failed, "That link didn't work. It may have expired; please try again.");
        }
        catch (AppException ex)
        {
            return (AuthCallbackResult.Failed, ex.Message);
        }
    }

    // Email accounts, and after a password-reset email
    public async Task ChangePasswordAsync(string newPassword)
    {
        var token = await GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");
        using var response = await _rest.SendAsync(HttpMethod.Put, "auth/v1/user", new { password = newPassword }, token);
        var user = (await response.Content.ReadFromJsonAsync<AuthUser>())!;
        await SetSessionAsync(new AuthSession { AccessToken = _session!.AccessToken, RefreshToken = _session.RefreshToken, ExpiresAt = _session.ExpiresAt, User = user });
    }

    public async Task SignOutAsync()
    {
        var accessToken = _session?.AccessToken;
        await ClearSessionAsync();

        // Best effort: also end the session on the server so its refresh token can't be reused
        if (accessToken is not null)
        {
            try { using var _ = await _rest.SendAsync(HttpMethod.Post, "auth/v1/logout", null, accessToken); }
            catch (Exception ex) { Console.WriteLine("Signing out on the server failed: " + ex.Message); }
        }
    }

    // Permanently deletes the account (the delete_my_account database function only ever deletes the caller)
    public async Task DeleteAccountAsync()
    {
        var token = await GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");
        using var _ = await _rest.SendAsync(HttpMethod.Post, "rest/v1/rpc/delete_my_account", new { }, token);
        await ClearSessionAsync();
    }

    // For requests to the user's own data: a valid access token, refreshed if needed
    public async Task<string?> GetAccessTokenAsync()
    {
        if (_session?.IsExpiringSoon == true)
            await RefreshAsync();

        return _session?.AccessToken;
    }

    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            if (_session is not { IsExpiringSoon: true } current)
                return;

            await SetSessionAsync(await PostAsync<AuthSession>("auth/v1/token?grant_type=refresh_token", new { refresh_token = current.RefreshToken }));
        }
        catch (AppException ex) when (ex.Message != "Couldn't reach the server. Check your internet connection and try again.")
        {
            // The refresh token was revoked or expired: the user has to sign in again
            Console.WriteLine("Session refresh rejected: " + ex.Message);
            await ClearSessionAsync();
        }
        catch (Exception ex)
        {
            // Probably offline: keep the session and try again in a minute
            Console.WriteLine("Session refresh failed: " + ex.Message);
            _refreshTimer?.Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<AuthUser> GetUserAsync(string accessToken)
        => (await _rest.GetSingleAsync<AuthUser>("auth/v1/user", accessToken))!;

    private async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await _rest.SendAsync(HttpMethod.Post, path, body, accessToken: null);
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(text) ? "{}" : text)!;
    }

    private async Task SetSessionAsync(AuthSession session)
    {
        _session = session;
        await _js.InvokeVoidAsync("localStorage.setItem", SessionKey, JsonSerializer.Serialize(session));
        ScheduleRefresh();
        Changed?.Invoke();
    }

    private async Task ClearSessionAsync()
    {
        _session = null;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        await _js.InvokeVoidAsync("localStorage.removeItem", SessionKey);
        Changed?.Invoke();
    }

    private void ScheduleRefresh()
    {
        if (_session is null)
            return;

        var due = DateTimeOffset.FromUnixTimeSeconds(_session.ExpiresAt) - RefreshMargin - DateTimeOffset.UtcNow;
        _refreshTimer ??= new Timer(_ => _ = RefreshAsync());
        _refreshTimer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
