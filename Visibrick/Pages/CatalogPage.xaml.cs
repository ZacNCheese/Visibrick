using Visibrick.Core.Models;
using Visibrick.Data;
using Visibrick.Services.Sync;

namespace Visibrick.Pages;

public partial class CatalogPage : ContentPage
{
	private readonly SyncOrchestrator _sync;
	private readonly CatalogRepository _repo;

	// The category cards, in the order they appear
	private readonly List<CatalogCategory> _categories =
	[
		new(CatalogType.Minifigs, "icon_minifigs.png"),
		new(CatalogType.Sets, "icon_sets.png"),
		new(CatalogType.Parts, "icon_parts.png"),
		new(CatalogType.Gear, "icon_gear.png"),
		new(CatalogType.Books, "icon_books.png")
	];

	public CatalogPage(SyncOrchestrator sync, CatalogRepository repo)
	{
		InitializeComponent();
		_sync = sync;
		_repo = repo;

		BindableLayout.SetItemsSource(CategoryList, _categories);

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

	private async void OnCategoryTapped(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is CatalogCategory category)
			await Shell.Current.GoToAsync($"items?type={category.Type.Code}");
	}

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
	{
		foreach (var category in _categories)
			category.CountText = $"{await _repo.CountAsync(category.Type.Code):N0} {category.Type.CountNoun}";
	}
}
