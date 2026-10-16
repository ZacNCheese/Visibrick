using System.Diagnostics;
using Visibrick.Models;
using Visibrick.Services.Auth;
using Visibrick.Services.Collection;

namespace Visibrick.Controls;

public partial class ItemCardView : ContentView
{
	public ItemCardView()
	{
		InitializeComponent();
	}

	private ItemCardModel? Model => BindingContext as ItemCardModel;

	// Opens the item's own page (details, and how many you own)
	private async void OnCardTapped(object? sender, TappedEventArgs e)
	{
		if (Model is { } model)
			await OpenItemAsync(model);
	}

	private async void OnAddTapped(object? sender, TappedEventArgs e)
	{
		if (Model is not { } model)
			return;

		var collection = IPlatformApplication.Current!.Services.GetRequiredService<CollectionService>();
		var auth = IPlatformApplication.Current.Services.GetRequiredService<AuthService>();

		if (!auth.IsSignedIn)
		{
			await AskToSignInAsync();
			return;
		}

		try
		{
			await collection.AddOneAsync(model.Item.Type, model.Item.Number);
		}
		catch (AuthException ex)
		{
			await Shell.Current.DisplayAlert("Couldn't add it", ex.Message, "OK");
		}
	}

	// The number chip: open the item on BrickLink in the user's browser
	private async void OnNumberTapped(object? sender, TappedEventArgs e)
	{
		if (Model is not { } model)
			return;

		try
		{
			await Browser.Default.OpenAsync(model.Item.LinkUrl, BrowserLaunchMode.External);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Couldn't open " + model.Item.LinkUrl + ": " + ex.Message);
		}
	}

	public static Task OpenItemAsync(ItemCardModel model)
		=> Shell.Current.GoToAsync($"item?type={model.Item.Type}&number={Uri.EscapeDataString(model.Item.Number)}");

	public static async Task AskToSignInAsync()
	{
		var signIn = await Shell.Current.DisplayAlert(
			"Sign in to start your collection",
			"Your collection is saved to your account, so it's there on every device.",
			"Sign in",
			"Not now");

		if (signIn)
			await Shell.Current.GoToAsync("//collection");
	}
}
