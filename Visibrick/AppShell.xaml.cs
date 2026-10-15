using Visibrick.Pages;

namespace Visibrick;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// A catalog category, opened from the Catalog tab with GoToAsync("items?type=S") etc.
		Routing.RegisterRoute("items", typeof(CatalogItemsPage));
	}
}
