using Microsoft.Extensions.DependencyInjection;

namespace PotLiteMaui;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_services.GetRequiredService<MainPage>())
		{
			Title = "划词翻译",
			Width = 940,
			Height = 760
		};
	}
}
