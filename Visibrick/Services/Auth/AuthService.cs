using System.Diagnostics;
using System.Text.Json;

namespace Visibrick.Services.Auth;

// The signed-in user for the whole app. Keeps the session in SecureStorage (Keychain / Keystore /
// Windows encrypted storage), refreshes the short-lived access token before it expires, and tells
// pages when the user signs in or out.
public class AuthService
{
    private const string SessionKey = "auth.session";

    // Refresh this long before the access token expires (Supabase access tokens last an hour by default)
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly SupabaseAuthClient _client;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AuthSession? _session;
    private Timer? _refreshTimer;

    public AuthService(SupabaseAuthClient client)
    {
        _client = client;
    }

    public AuthUser? CurrentUser => _session?.User;

    public bool IsSignedIn => _session is not null;

    // Signed in or out. Raised on the main thread.
    public event EventHandler? Changed;

    // Called on launch: restores the saved session, if any, and makes sure it's still valid
    public async Task InitializeAsync()
    {
        try
        {
            var saved = await SecureStorage.Default.GetAsync(SessionKey);
            if (saved is null)
                return;

            _session = JsonSerializer.Deserialize<AuthSession>(saved);
            RaiseChanged();

            if (_session?.IsExpiringSoon == true)
                await RefreshAsync();
            else
                ScheduleRefresh();
        }
        catch (Exception ex)
        {
            // A damaged or unreadable saved session: start signed out rather than crash
            Debug.WriteLine("AUTH RESTORE FAILED: " + ex.Message);
            ClearSession();
            return;
        }

        await RefreshUserAsync();
    }

    // Updates the saved account details (name, email, how they sign in) from the server, in case they
    // changed since this device last saw them. Keeps the saved copy if the server can't be reached.
    private async Task RefreshUserAsync()
    {
        try
        {
            if (await GetAccessTokenAsync() is not { } token || _session is null)
                return;

            var user = await _client.GetUserAsync(token);
            await SetSessionAsync(new AuthSession
            {
                AccessToken = _session.AccessToken,
                RefreshToken = _session.RefreshToken,
                ExpiresAt = _session.ExpiresAt,
                User = user
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine("AUTH USER REFRESH FAILED: " + ex.Message);
        }
    }

    public async Task SignInWithEmailAsync(string email, string password)
        => await SetSessionAsync(await _client.SignInWithPasswordAsync(email.Trim(), password));

    public async Task<SignUpOutcome> SignUpWithEmailAsync(string email, string password)
    {
        var session = await _client.SignUpAsync(email.Trim(), password);
        if (session is null)
            return SignUpOutcome.ConfirmationEmailSent;

        await SetSessionAsync(session);
        return SignUpOutcome.SignedIn;
    }

    // Supabase emails a link to choose a new password. It replies the same way whether or not the email has an
    // account, so this can't be used to find out who has signed up.
    public Task SendPasswordResetAsync(string email) => _client.SendPasswordResetAsync(email.Trim());

    // provider: "google" or "apple". Throws OperationCanceledException if the user gives up in the browser.
    public async Task SignInWithProviderAsync(string provider, CancellationToken cancellationToken = default)
    {
        var (url, verifier) = SupabaseAuthClient.CreateAuthorizeUrl(provider, OAuthBrowser.RedirectUrl);
        var code = await OAuthBrowser.GetAuthorizationCodeAsync(url, cancellationToken);
        await SetSessionAsync(await _client.ExchangeCodeAsync(code, verifier));
    }

    public async Task SignOutAsync()
    {
        var accessToken = _session?.AccessToken;
        ClearSession();

        // Best effort: also end the session on the server so its refresh token can't be reused
        if (accessToken is not null)
        {
            try { await _client.SignOutAsync(accessToken); }
            catch (Exception ex) { Debug.WriteLine("SIGN OUT ON SERVER FAILED: " + ex.Message); }
        }
    }

    // Email accounts only (see AuthUser.HasPassword)
    public async Task ChangePasswordAsync(string newPassword)
    {
        var token = await GetAccessTokenAsync() ?? throw new AuthException("Please sign in again.");
        var user = await _client.UpdatePasswordAsync(token, newPassword);
        await SetSessionAsync(new AuthSession
        {
            AccessToken = _session!.AccessToken,
            RefreshToken = _session.RefreshToken,
            ExpiresAt = _session.ExpiresAt,
            User = user
        });
    }

    // Permanently deletes the account; its profile (and later its collection) go with it
    public async Task DeleteAccountAsync()
    {
        var token = await GetAccessTokenAsync() ?? throw new AuthException("Please sign in again.");
        await _client.DeleteAccountAsync(token);
        ClearSession();
    }

    // For calls to your own data (e.g. the user's collection): a valid access token, refreshed if needed
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

            await SetSessionAsync(await _client.RefreshAsync(current.RefreshToken));
        }
        catch (AuthException ex)
        {
            // The refresh token was revoked or expired (e.g. signed out everywhere): the user has to sign in again
            Debug.WriteLine("AUTH REFRESH REJECTED: " + ex.Message);
            ClearSession();
        }
        catch (Exception ex)
        {
            // Probably offline: keep the session and try again later
            Debug.WriteLine("AUTH REFRESH FAILED: " + ex.Message);
            _refreshTimer?.Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task SetSessionAsync(AuthSession session)
    {
        _session = session;
        await SecureStorage.Default.SetAsync(SessionKey, JsonSerializer.Serialize(session));
        ScheduleRefresh();
        RaiseChanged();
    }

    private void ClearSession()
    {
        _session = null;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        SecureStorage.Default.Remove(SessionKey);
        RaiseChanged();
    }

    private void ScheduleRefresh()
    {
        if (_session is null)
            return;

        var due = DateTimeOffset.FromUnixTimeSeconds(_session.ExpiresAt) - RefreshMargin - DateTimeOffset.UtcNow;
        if (due < TimeSpan.Zero)
            due = TimeSpan.Zero;

        _refreshTimer ??= new Timer(_ => _ = RefreshAsync());
        _refreshTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void RaiseChanged()
        => MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(this, EventArgs.Empty));
}
