using System.Collections.ObjectModel;
using Visibrick.Controls;
using Visibrick.Core.Models;
using Visibrick.Data;
using Visibrick.Services.Sync;

namespace Visibrick.Pages;

public partial class MinifigsPage : ContentPage
{
	// Minifigs are loaded from SQLite in batches of this many rows as you scroll.
	// Batches are always whole rows, so rows already on screen never have to change.
	private const int RowsPerBatch = 10;

	// Roughly how wide each card should be, used to pick the number of columns
	private const double CardWidth = 190;

	private readonly LegoMinifigRepository _repo;
	private readonly SyncOrchestrator _sync;

	// Every minifig loaded so far, and the same minifigs grouped into rows of cards for the list
	private readonly List<LegoMinifig> _minifigs = new();
	private ObservableCollection<CardRow> _rows = new();

	private CancellationTokenSource? _searchCts;
	private string? _search;
	private int _totalCount = -1;
	private bool _isLoading;
	private bool _hasMore;

	// Cards per row. Nothing loads until the first layout tells us the page width,
	// so the first rows already have the right number of columns.
	private int _columns = 2;
	private readonly TaskCompletionSource _widthKnown = new();
	private IDispatcherTimer? _regroupTimer;

	// The catalog version this page last loaded (see SyncOrchestrator.DataVersion)
	private int _loadedVersion = -1;

	// Bumped on every reload so a batch that finishes loading after a new search is thrown away
	private int _generation;

	public MinifigsPage(LegoMinifigRepository repo, SyncOrchestrator sync)
	{
		InitializeComponent();
		_repo = repo;
		_sync = sync;
		MinifigList.ItemsSource = _rows;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		_sync.DataChanged += OnDataChanged;
		_sync.StateChanged += OnSyncStateChanged;

		// First visit, or the catalog changed while this page was hidden
		if (_loadedVersion != _sync.DataVersion)
			await ReloadAsync();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();

		// This page is created each time it's opened, so let go of the long-lived sync service
		_sync.DataChanged -= OnDataChanged;
		_sync.StateChanged -= OnSyncStateChanged;
	}

	private async void OnDataChanged(object? sender, EventArgs e) => await ReloadAsync();

	private void OnSyncStateChanged(object? sender, EventArgs e) => UpdateHeader();

	private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

	// The number chip on a card: open that minifig on BrickLink in the user's browser
	private async void OnNumberTapped(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is not LegoMinifig { BrickLinkUrl: { } url })
			return;

		try
		{
			await Browser.Default.OpenAsync(url, BrowserLaunchMode.External);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("Couldn't open " + url + ": " + ex.Message);
		}
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);

		if (width <= 0)
			return;

		var columns = Math.Max(2, (int)(width / CardWidth));

		// First layout: start with the right column count, then let loading begin
		if (!_widthKnown.Task.IsCompleted)
		{
			_columns = columns;
			_widthKnown.SetResult();
			return;
		}

		if (columns == _columns)
			return;

		_columns = columns;

		// Dragging a window edge fires many size changes a second, so regroup once it settles
		_regroupTimer ??= CreateRegroupTimer();
		_regroupTimer.Stop();
		_regroupTimer.Start();
	}

	private IDispatcherTimer CreateRegroupTimer()
	{
		var timer = Dispatcher.CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(250);
		timer.IsRepeating = false;
		timer.Tick += (_, _) =>
		{
			// Don't regroup in the middle of loading a batch; try again shortly
			if (_isLoading)
				timer.Start();
			else
				RegroupRows();
		};
		return timer;
	}

	private void RegroupRows()
	{
		// Keep every row full while there's more to load: drop the leftover minifigs from the end,
		// and the next batch loads them again as part of a full row
		if (_hasMore)
		{
			var leftover = _minifigs.Count % _columns;
			_minifigs.RemoveRange(_minifigs.Count - leftover, leftover);
		}

		_rows = new ObservableCollection<CardRow>(CardRow.Chunk(_minifigs, _columns));
		MinifigList.ItemsSource = _rows;

		// More columns means fewer rows. If that leaves too few to scroll, the "load more when you
		// scroll near the end" trigger never fires, so top up straight away.
		if (_hasMore && _rows.Count < RowsPerBatch)
			_ = LoadMoreAsync();
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
		_loadedVersion = _sync.DataVersion;
		_generation++;
		_isLoading = false;
		_hasMore = true;
		_minifigs.Clear();
		_rows.Clear();

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

		await _widthKnown.Task;

		var columns = _columns;
		var batchSize = columns * RowsPerBatch;
		var batch = await _repo.SearchAsync(_search, _minifigs.Count, batchSize);

		// A newer search started while this batch was loading
		if (generation != _generation)
			return;

		_minifigs.AddRange(batch);
		foreach (var row in CardRow.Chunk(batch, columns))
			_rows.Add(row);

		_hasMore = batch.Count == batchSize;
		_isLoading = false;
	}

	private void UpdateHeader()
	{
		// Not loaded yet
		if (_totalCount < 0)
			return;

		var searching = !string.IsNullOrWhiteSpace(_search);

		CountLabel.Text = searching
			? $"{_totalCount:N0} matching minifigs"
			: $"{_totalCount:N0} minifigs";

		EmptyState.IsVisible = _totalCount == 0;
		MinifigList.IsVisible = _totalCount > 0;

		if (_totalCount == 0)
		{
			(EmptyTitle.Text, EmptyMessage.Text) = (searching, _sync.IsSyncing) switch
			{
				(true, _) => ("No matches", $"Nothing matches \"{_search!.Trim()}\". Try a different name or number."),
				(false, true) => ("Downloading the catalog…", "Every minifig from BrickLink will appear here in a moment."),
				_ => ("No minifigs yet", "Couldn't download the catalog. Check your internet connection; it will try again automatically.")
			};
		}
	}
}
