using Microsoft.Extensions.DependencyInjection;

namespace LEGOOO;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		//test comment
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}