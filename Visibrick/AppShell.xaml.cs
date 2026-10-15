using Visibrick.Pages;

namespace Visibrick;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Catalog categories, opened from the Catalog tab with GoToAsync("minifigs")
		Routing.RegisterRoute("minifigs", typeof(MinifigsPage));
	}
}
