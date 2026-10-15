using Visibrick.Services.Sync;

namespace Visibrick;

public partial class App : Application
{
	private readonly SyncOrchestrator _sync;

	public App(SyncOrchestrator sync)
	{
		InitializeComponent();
		_sync = sync;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());

		// Check for new catalog data on launch and whenever the app comes back to the foreground.
		// SyncIfStaleAsync skips the check if it ran recently, and never throws.
		window.Created += (_, _) => _ = _sync.SyncIfStaleAsync();
		window.Resumed += (_, _) => _ = _sync.SyncIfStaleAsync();

		return window;
	}
}
