using Visibrick.Data;
using Visibrick.Services.Sync;

namespace Visibrick.Pages;

public partial class CatalogPage : ContentPage
{
	private readonly SyncOrchestrator _sync;
	private readonly LegoMinifigRepository _minifigRepo;

	public CatalogPage(SyncOrchestrator sync, LegoMinifigRepository minifigRepo)
	{
		InitializeComponent();
		_sync = sync;
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

	private void UpdateStatus()
	{
		SyncSpinner.IsVisible = SyncSpinner.IsRunning = _sync.IsSyncing;

		StatusLabel.Text = _sync switch
		{
			{ IsConfigured: false } => "Catalog server not set up yet (add the Supabase key in SupabaseConfig.cs)",
			{ IsSyncing: true } => "Updating catalog…",
			{ LastSyncFailed: true } => "Couldn't update the catalog. Will try again later.",
			{ LastChecked: { } last } => $"Catalog up to date · checked {last.ToLocalTime():t}",
			_ => ""
		};
	}

	private async Task RefreshCountsAsync()
		=> MinifigCountLabel.Text = $"{await _minifigRepo.CountAsync():N0} minifigs";
}
