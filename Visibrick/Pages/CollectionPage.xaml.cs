namespace Visibrick;

public partial class CollectionPage : ContentPage
{
	public CollectionPage()
	{
		InitializeComponent();
	}

	private async void OnBrowseCatalogClicked(object? sender, EventArgs e)
		=> await Shell.Current.GoToAsync("//catalog");
}
