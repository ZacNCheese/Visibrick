using Visibrick.Pages;

namespace Visibrick;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// A catalog category, opened from the Catalog tab with GoToAsync("items?type=S") etc.
		Routing.RegisterRoute("items", typeof(CatalogItemsPage));

		// Email sign-in, opened from My Collection
		Routing.RegisterRoute("signin", typeof(SignInPage));

		// Profile settings, opened from the profile menu in the top-right corner
		Routing.RegisterRoute("profile", typeof(ProfilePage));

		// One catalog item (details + how many you own), opened with GoToAsync("item?type=S&number=75192-1")
		Routing.RegisterRoute("item", typeof(ItemDetailPage));
	}
}
