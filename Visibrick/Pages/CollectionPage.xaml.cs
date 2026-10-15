using Visibrick.Controls;
using Visibrick.Core.Models;
using Visibrick.Data;
using Visibrick.Models;
using Visibrick.Services.Auth;
using Visibrick.Services.Collection;

namespace Visibrick.Pages;

public partial class CollectionPage : ContentPage
{
	// Roughly how wide each card should be, used to pick the number of columns
	private const double CardWidth = 190;

	private readonly AuthService _auth;
	private readonly CollectionService _collection;
	private readonly CollectionRepository _repo;
	private CancellationTokenSource? _providerSignIn;
	private CancellationTokenSource? _searchCts;

	// Which type the filter chips show (null = All), and the search text
	private string? _filterType;
	private string? _search;

	// The owned items currently shown, and the card columns for the page width
	private List<ItemCardModel> _shown = [];
	private int _columns = 2;
	private bool _widthKnown;
	private IDispatcherTimer? _regroupTimer;

	// Set when the collection changes while this page is hidden, so it reloads when it reappears
	private bool _stale = true;

	public CollectionPage(AuthService auth, CollectionService collection, CollectionRepository repo)
	{
		InitializeComponent();
		_auth = auth;
		_collection = collection;
		_repo = repo;

		// This page lives for the whole session, so it can stay subscribed
		_auth.Changed += async (_, _) => await ReloadAsync();
		_collection.Changed += async (_, _) => await ReloadIfVisibleAsync();
		_collection.ItemChanged += async (_, _) => await ReloadIfVisibleAsync();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (_stale)
			await ReloadAsync();
	}

	private async Task ReloadIfVisibleAsync()
	{
		if (IsVisible && Window is not null && Shell.Current?.CurrentPage == this)
			await ReloadAsync();
		else
			_stale = true;
	}

	private async Task ReloadAsync()
	{
		_stale = false;
		var signedIn = _auth.IsSignedIn;

		SignInCard.IsVisible = !signedIn;
		if (!signedIn)
		{
			SubtitleLabel.Text = "Everything you own, down to the last brick.";
			ToolsPanel.IsVisible = OwnedList.IsVisible = EmptyCard.IsVisible = false;
			StatePanel.IsVisible = true;
			return;
		}

		var all = await _repo.GetOwnedItemsAsync(null, null);
		var owned = await _repo.GetOwnedItemsAsync(_filterType, _search);

		var pieces = all.Sum(i => i.Quantity);
		SubtitleLabel.Text = all.Count == 0
			? $"Signed in as {_auth.CurrentUser!.DisplayName}"
			: $"{pieces:N0} item{(pieces == 1 ? "" : "s")} · {all.Count:N0} different";

		BuildFilters(all);
		ToolsPanel.IsVisible = all.Count > 0;

		_shown = owned.Select(i => new ItemCardModel(i, i.Quantity)).ToList();
		ShowRows();

		var hasResults = _shown.Count > 0;
		OwnedList.IsVisible = hasResults;
		StatePanel.IsVisible = !hasResults;
		EmptyCard.IsVisible = !hasResults;

		if (!hasResults)
		{
			var filtering = all.Count > 0;
			EmptyTitle.Text = filtering ? "Nothing matches" : "Your collection is empty";
			EmptyMessage.Text = filtering
				? "Try a different search or filter."
				: "Find minifigs, sets, parts and more in the catalog, and tap + to add them here.";
			BrowseButton.IsVisible = !filtering;
		}
	}

	// All, then one chip per type the user owns something of, with counts
	private void BuildFilters(List<OwnedItem> all)
	{
		FilterBar.Children.Clear();
		AddFilter("All", null, all.Count);

		foreach (var type in CatalogType.All)
		{
			var count = all.Count(i => i.Type == type.Code);
			if (count > 0)
				AddFilter(type.Name, type.Code, count);
		}

		// The filtered type no longer has anything in it
		if (_filterType is not null && all.All(i => i.Type != _filterType))
			_filterType = null;
	}

	private void AddFilter(string name, string? type, int count)
	{
		var chip = new Button
		{
			Text = $"{name} · {count:N0}",
			Style = (Style)Application.Current!.Resources["ClayFilterChip"],
			Margin = new Thickness(0, 0, 8, 8)
		};

		if (type == _filterType)
		{
			chip.BackgroundColor = (Color)Application.Current.Resources["Primary"];
			chip.TextColor = Colors.White;
		}

		chip.Clicked += async (_, _) =>
		{
			_filterType = type;
			await ReloadAsync();
		};

		FilterBar.Children.Add(chip);
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

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);

		if (width <= 0)
			return;

		var columns = Math.Max(2, (int)(width / CardWidth));

		// First layout: show the cards straight away with the right column count
		if (!_widthKnown)
		{
			_widthKnown = true;
			_columns = columns;
			ShowRows();
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
		timer.Tick += (_, _) => ShowRows();
		return timer;
	}

	private void ShowRows()
	{
		if (_widthKnown)
			OwnedList.ItemsSource = CardRow.Chunk(_shown, _columns);
	}

	private async void OnAppleClicked(object? sender, EventArgs e) => await SignInWithProviderAsync("apple");

	private async void OnGoogleClicked(object? sender, EventArgs e) => await SignInWithProviderAsync("google");

	private async Task SignInWithProviderAsync(string provider)
	{
		ShowError(null);
		SignInButtons.IsVisible = false;
		WaitingPanel.IsVisible = true;
		_providerSignIn = new CancellationTokenSource();

		try
		{
			await _auth.SignInWithProviderAsync(provider, _providerSignIn.Token);
		}
		catch (OperationCanceledException)
		{
			// The user cancelled or closed the browser
		}
		catch (AuthException ex)
		{
			ShowError(ex.Message);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("PROVIDER SIGN-IN FAILED: " + ex);
			ShowError("Sign-in didn't work. Please try again.");
		}
		finally
		{
			_providerSignIn = null;
			SignInButtons.IsVisible = true;
			WaitingPanel.IsVisible = false;
		}
	}

	private void OnCancelClicked(object? sender, EventArgs e) => _providerSignIn?.Cancel();

	private async void OnEmailClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("signin");

	private async void OnBrowseCatalogClicked(object? sender, EventArgs e)
		=> await Shell.Current.GoToAsync("//catalog");

	private void ShowError(string? message)
	{
		ErrorLabel.Text = message;
		ErrorLabel.IsVisible = message is not null;
	}
}
