using Visibrick.Data;
using Visibrick.Services.Sync;

namespace Visibrick;

public partial class CatalogPage : ContentPage
{
	private readonly SyncOrchestrator _sync;
	private readonly LegoColorRepository _colorRepo;
	private readonly LegoMinifigRepository _minifigRepo;

	public CatalogPage(SyncOrchestrator sync, LegoColorRepository colorRepo, LegoMinifigRepository minifigRepo)
	{
		InitializeComponent();
		_sync = sync;
		_colorRepo = colorRepo;
		_minifigRepo = minifigRepo;

		// This page lives for the whole session, so it can stay subscribed
		_sync.StateChanged += (_, _) => UpdateStatus();
		_sync.DataChanged += async (_, _) => await RefreshCountsAsync();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		UpdateStatus();
		await RefreshCountsAsync();
	}

	private async void OnMinifigsTapped(object? sender, TappedEventArgs e)
		=> await Shell.Current.GoToAsync("minifigs");

	private async void OnColorsTapped(object? sender, TappedEventArgs e)
		=> await Shell.Current.GoToAsync("colors");

	private void UpdateStatus()
	{
		SyncSpinner.IsVisible = SyncSpinner.IsRunning = _sync.IsSyncing;

		StatusLabel.Text = _sync switch
		{
			{ IsSyncing: true } => "Updating catalog…",
			{ LastSyncFailed: true } => "Couldn't update the catalog. Will try again later.",
			{ LastChecked: { } last } => $"Catalog up to date · checked {last.ToLocalTime():t}",
			_ => ""
		};
	}

	private async Task RefreshCountsAsync()
	{
		MinifigCountLabel.Text = $"{await _minifigRepo.CountAsync():N0} minifigs";
		ColorCountLabel.Text = $"{await _colorRepo.CountAsync():N0} colors";
	}
}
