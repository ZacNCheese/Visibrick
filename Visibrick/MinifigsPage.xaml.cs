using System.Collections.ObjectModel;
using Visibrick.Data;
using Visibrick.Core.Models;

namespace Visibrick;

public partial class MinifigsPage : ContentPage
{
	// Rows loaded from SQLite at a time; more load as you scroll
	private const int PageSize = 60;

	// Roughly how wide each card should be, used to pick the number of columns
	private const double CardWidth = 190;

	private readonly LegoMinifigRepository _repo;
	private readonly ObservableCollection<LegoMinifig> _minifigs = new();

	private CancellationTokenSource? _searchCts;
	private string? _search;
	private int _totalCount = -1;
	private bool _isLoading;
	private bool _hasMore;

	// Bumped on every reload so a page that finishes loading after a new search is thrown away
	private int _generation;

	public MinifigsPage(LegoMinifigRepository repo)
	{
		InitializeComponent();
		_repo = repo;
		MinifigList.ItemsSource = _minifigs;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		// Only reload if the data changed (e.g. after a sync on the Home tab), so the scroll position is kept
		var count = await _repo.CountAsync(_search);
		if (count != _totalCount)
			await ReloadAsync();
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);

		if (width > 0)
			MinifigGrid.Span = Math.Max(2, (int)(width / CardWidth));
	}

	private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		// Wait until typing pauses so we don't query on every keystroke
		_searchCts?.Cancel();
		_searchCts = new CancellationTokenSource();

		try
		{
			await Task.Delay(250, _searchCts.Token);
		}
		catch (TaskCanceledException)
		{
			return;
		}

		_search = e.NewTextValue;
		await ReloadAsync();
	}

	private async void OnRemainingItemsThresholdReached(object? sender, EventArgs e)
	{
		await LoadMoreAsync();
	}

	private async Task ReloadAsync()
	{
		_generation++;
		_isLoading = false;
		_hasMore = true;
		_minifigs.Clear();

		_totalCount = await _repo.CountAsync(_search);
		UpdateHeader();

		await LoadMoreAsync();
	}

	private async Task LoadMoreAsync()
	{
		if (_isLoading || !_hasMore)
			return;

		_isLoading = true;
		var generation = _generation;

		var page = await _repo.SearchAsync(_search, _minifigs.Count, PageSize);

		// A newer search started while this page was loading
		if (generation != _generation)
			return;

		foreach (var minifig in page)
			_minifigs.Add(minifig);

		_hasMore = page.Count == PageSize;
		_isLoading = false;
	}

	private void UpdateHeader()
	{
		var searching = !string.IsNullOrWhiteSpace(_search);

		CountLabel.Text = searching
			? $"{_totalCount:N0} matching minifigs"
			: $"{_totalCount:N0} minifigs";

		EmptyState.IsVisible = _totalCount == 0;
		MinifigList.IsVisible = _totalCount > 0;

		if (_totalCount == 0)
		{
			EmptyTitle.Text = searching ? "No matches" : "No minifigs yet";
			EmptyMessage.Text = searching
				? $"Nothing matches \"{_search!.Trim()}\". Try a different name or number."
				: "Go to the Home tab and tap Sync data to download every minifig from Rebrickable.";
		}
	}
}
