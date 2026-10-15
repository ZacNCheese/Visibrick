using System.Diagnostics;
using Visibrick.Data;
using Visibrick.Services.Sync;

namespace Visibrick;

public partial class MainPage : ContentPage
{
	private readonly SyncOrchestrator _syncOrchestrator;
	private readonly LegoColorRepository _colorRepo;
	private readonly LegoMinifigRepository _minifigRepo;

	public MainPage(SyncOrchestrator syncOrchestrator, LegoColorRepository colorRepo, LegoMinifigRepository minifigRepo)
	{
		InitializeComponent();
		_syncOrchestrator = syncOrchestrator;
		_colorRepo = colorRepo;
		_minifigRepo = minifigRepo;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await RefreshCountsAsync();
	}

	public async void OnSyncClicked(object? sender, EventArgs e)
	{
		SyncBtn.IsEnabled = false;
		SyncSpinner.IsVisible = SyncSpinner.IsRunning = true;
		StatusLabel.Text = "Syncing…";

		try
		{
			// Run off the UI thread so the spinner keeps animating while the CSVs are parsed
			await Task.Run(_syncOrchestrator.FullSyncAsync);
			StatusLabel.Text = $"Synced at {DateTime.Now:t}";
			await RefreshCountsAsync();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("SYNC FAILED: " + ex);
			StatusLabel.Text = "Sync failed. Check your connection and try again.";
		}
		finally
		{
			SyncBtn.IsEnabled = true;
			SyncSpinner.IsVisible = SyncSpinner.IsRunning = false;
		}
	}

	private async Task RefreshCountsAsync()
	{
		MinifigCountLabel.Text = (await _minifigRepo.CountAsync()).ToString("N0");
		ColorCountLabel.Text = (await _colorRepo.CountAsync()).ToString("N0");
	}
}
