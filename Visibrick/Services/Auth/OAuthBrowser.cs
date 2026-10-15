#if WINDOWS
using System.Net;
using System.Text;
using System.Web;
#endif

namespace Visibrick.Services.Auth;

// Opens Google/Apple sign-in in the system browser and waits for it to come back with an authorization code.
// The system browser (not an in-app web view) is the safe, recommended way: the app never sees the user's
// Google/Apple password, and Google refuses to sign in inside embedded web views anyway.
//
// Both redirect addresses must be listed in Supabase: Authentication → URL Configuration → Redirect URLs.
public static class OAuthBrowser
{
#if WINDOWS
    // MAUI's WebAuthenticator doesn't support Windows, so the app briefly listens on this local address
    // and the browser is sent back to it (the standard "loopback" pattern for desktop apps).
    private const int LoopbackPort = 52931;
    public static string RedirectUrl => $"http://localhost:{LoopbackPort}/auth/callback/";
#else
    // iOS, Android and Mac: the browser hands this link back to the app (registered in each platform's settings)
    public static string RedirectUrl => "visibrick://auth-callback";
#endif

    // How long to wait for the user to finish in the browser
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static async Task<string> GetAuthorizationCodeAsync(Uri authorizeUrl, CancellationToken cancellationToken)
    {
#if WINDOWS
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUrl);
        listener.Start();

        await Launcher.Default.OpenAsync(authorizeUrl);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        await using var stop = timeout.Token.Register(listener.Stop);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync();
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
        var code = query["code"];
        var error = query["error_description"] ?? query["error"];

        // A small page for the browser tab, so the user knows to go back to the app
        var html = Encoding.UTF8.GetBytes(code is not null
            ? "<html><body style=\"font-family:sans-serif;text-align:center;padding-top:15%\"><h2>You're signed in to Visibrick</h2><p>You can close this tab and go back to the app.</p></body></html>"
            : "<html><body style=\"font-family:sans-serif;text-align:center;padding-top:15%\"><h2>Sign-in didn't finish</h2><p>You can close this tab and try again in Visibrick.</p></body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(html, cancellationToken);
        context.Response.Close();

        return code ?? throw new AuthException(error ?? "Sign-in didn't finish. Please try again.");
#else
        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(authorizeUrl, new Uri(RedirectUrl));
        }
        catch (TaskCanceledException)
        {
            // The user closed the browser sheet
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.Properties.TryGetValue("code", out var code))
            return code;

        result.Properties.TryGetValue("error_description", out var error);
        throw new AuthException(error ?? "Sign-in didn't finish. Please try again.");
#endif
    }
}
