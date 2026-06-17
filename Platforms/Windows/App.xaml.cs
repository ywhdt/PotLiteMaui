using Microsoft.UI.Xaml;
using System.Text;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PotLiteMaui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		UnhandledException += OnUnhandledException;
		InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		LogCrash("WinUI unhandled exception", e.Exception);
	}

	private static void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
	{
		LogCrash("AppDomain unhandled exception", e.ExceptionObject as Exception);
	}

	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		LogCrash("Unobserved task exception", e.Exception);
	}

	private static void LogCrash(string title, Exception? exception)
	{
		try
		{
			var directory = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"PotLiteMaui",
				"Logs");
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "crash.log");
			var builder = new StringBuilder()
				.AppendLine($"[{DateTimeOffset.Now:O}] {title}")
				.AppendLine(exception?.ToString() ?? "No exception object was provided.")
				.AppendLine();
			File.AppendAllText(path, builder.ToString());
		}
		catch
		{
			// Last-chance crash logging must never throw another startup exception.
		}
	}
}
