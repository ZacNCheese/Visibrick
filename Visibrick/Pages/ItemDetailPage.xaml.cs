using System.Globalization;
using Visibrick.Controls;
using Visibrick.Core.Models;
using Visibrick.Data;
using Visibrick.Services.Auth;
using Visibrick.Services.Collection;

namespace Visibrick.Pages;

public partial class ItemDetailPage : ContentPage, IQueryAttributable
{
	private readonly CatalogRepository _catalog;
	private readonly CollectionService _collection;
	private readonly AuthService _auth;

	private string _type = "";
	private string _number = "";
	private CatalogItem? _item;

	public ItemDetailPage(CatalogRepository catalog, CollectionService collection, AuthService auth)
	{
		InitializeComponent();
		_catalog = catalog;
		_collection = collection;
		_auth = auth;
	}

	// Shell passes ?type=...&number=... here before the page appears
	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		_type = query.TryGetValue("type", out var type) ? type as string ?? "" : "";
		_number = query.TryGetValue("number", out var number) ? Uri.UnescapeDataString(number as string ?? "") : "";
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		_collection.ItemChanged += OnItemChanged;
		_collection.Changed += OnCollectionChanged;
		_auth.Changed += OnCollectionChanged;

		_item ??= await _catalog.GetAsync(_type, _number);
		ShowItem();
		UpdateCollection();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_collection.ItemChanged -= OnItemChanged;
		_collection.Changed -= OnCollectionChanged;
		_auth.Changed -= OnCollectionChanged;
	}

	private void OnItemChanged(object? sender, ItemQuantityChangedEventArgs e)
	{
		if (e.Key == CatalogItem.MakeKey(_type, _number))
			UpdateCollection();
	}

	private void OnCollectionChanged(object? sender, EventArgs e) => UpdateCollection();

	private void ShowItem()
	{
		if (_item is null)
		{
			NameLabel.Text = "Item not found";
			TypeLabel.Text = _number;
			return;
		}

		var type = CatalogType.FromCode(_item.Type);
		TypeLabel.Text = type is null ? "" : SingularName(type);
		NameLabel.Text = _item.Name;
		NumberLabel.Text = _item.Number + " ↗";
		ItemImage.Source = ImageSource.FromUri(new Uri(_item.LargeImageUrl));

		DetailsGrid.Children.Clear();
		DetailsGrid.RowDefinitions.Clear();
		AddDetail("Category", _item.CategoryName);
		AddDetail("Released", _item.YearReleased?.ToString(CultureInfo.InvariantCulture));
		AddDetail("Weight", _item.WeightGrams is { } grams ? $"{grams.ToString("0.##", CultureInfo.CurrentCulture)} g" : null);
		AddDetail("Dimensions", _item.Dimensions is { } size ? size + " cm" : null);
		AddDetail("Also known as", _item.AlternateNumbers);

		if (DetailsGrid.Children.Count == 0)
			AddDetail("", "BrickLink doesn't list any other details for this item.");
	}

	private void AddDetail(string label, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return;

		var row = DetailsGrid.RowDefinitions.Count;
		DetailsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		DetailsGrid.Add(new Label { Text = label, Style = (Style)Application.Current!.Resources["ClayCaption"], VerticalOptions = LayoutOptions.Center }, 0, row);
		DetailsGrid.Add(new Label { Text = value, Style = (Style)Application.Current.Resources["ClayCardTitle"] }, 1, row);
	}

	private void UpdateCollection()
	{
		var signedIn = _auth.IsSignedIn;
		var quantity = _collection.QuantityOf(_type, _number);

		SignedOutPanel.IsVisible = !signedIn;
		AddButton.IsVisible = signedIn && quantity == 0 && _item is not null;
		OwnedPanel.IsVisible = signedIn && quantity > 0;
		QuantityLabel.Text = quantity.ToString(CultureInfo.CurrentCulture);
	}

	private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

	private async void OnSignInClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("//collection");

	private async void OnNumberTapped(object? sender, TappedEventArgs e)
	{
		if (_item is not null)
			await Browser.Default.OpenAsync(_item.BrickLinkUrl, BrowserLaunchMode.External);
	}

	private async void OnAddClicked(object? sender, EventArgs e) => await SetQuantityAsync(1);

	private async void OnPlusClicked(object? sender, EventArgs e) => await SetQuantityAsync(_collection.QuantityOf(_type, _number) + 1);

	// Going below one removes it, same as "Remove"
	private async void OnMinusClicked(object? sender, EventArgs e) => await SetQuantityAsync(_collection.QuantityOf(_type, _number) - 1);

	private async void OnRemoveClicked(object? sender, EventArgs e) => await SetQuantityAsync(0);

	private async Task SetQuantityAsync(int quantity)
	{
		CollectionMessage.IsVisible = false;
		try
		{
			await _collection.SetQuantityAsync(_type, _number, Math.Max(0, quantity));
		}
		catch (AuthException ex)
		{
			CollectionMessage.Text = ex.Message;
			CollectionMessage.IsVisible = true;
		}
	}

	private static string SingularName(CatalogType type) => type.Code switch
	{
		"M" => "Minifig",
		"S" => "Set",
		"P" => "Part",
		"G" => "Gear",
		"B" => "Book",
		_ => type.Name
	};
}
