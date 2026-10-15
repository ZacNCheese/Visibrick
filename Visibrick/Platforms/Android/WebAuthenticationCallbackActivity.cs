using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Visibrick;

// Receives visibrick://auth-callback when Google/Apple sign-in finishes in the browser and hands it back
// to WebAuthenticator (see Services/Auth/OAuthBrowser.cs)
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
	[Intent.ActionView],
	Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
	DataScheme = "visibrick",
	DataHost = "auth-callback")]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
