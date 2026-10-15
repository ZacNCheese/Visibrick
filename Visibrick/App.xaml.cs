using Visibrick.Services.Auth;
using Visibrick.Services.Sync;

namespace Visibrick;

public partial class App : Application
{
	private readonly SyncOrchestrator _sync;
	private readonly AuthService _auth;

	public App(SyncOrchestrator sync, AuthService auth)
	{
		InitializeComponent();
		_sync = sync;
		_auth = auth;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());

		window.Created += (_, _) =>
		{
			// Restore the signed-in user from secure storage (refreshing the session if it has expired)
			_ = _auth.InitializeAsync();

			// Check for new catalog data. SyncIfStaleAsync skips the check if it ran recently, and never throws.
			_ = _sync.SyncIfStaleAsync();
		};

		window.Resumed += (_, _) =>
		{
			// The refresh timer may not have run while the app was in the background
			_ = _auth.GetAccessTokenAsync();
			_ = _sync.SyncIfStaleAsync();
		};

		return window;
	}
}
