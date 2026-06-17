using PotLiteMaui.Models;

namespace PotLiteMaui.Services.Platform;

public interface IHotkeyService
{
	event EventHandler? HotkeyPressed;
	bool IsRegistered { get; }
	string HotkeyText { get; }
	bool Register(string hotkey);
	void Unregister();
}

public interface ITextSelectionService
{
	Task<string?> CaptureSelectedTextAsync(int copyDelayMs, CancellationToken cancellationToken = default);
}

public interface ITrayService
{
	bool IsSupported { get; }
	void Initialize(Window mainWindow, Func<AppSettings> settingsProvider);
	void SetListening(bool isListening);
}

public interface IStartupService
{
	bool IsSupported { get; }
	bool IsEnabled();
	void SetEnabled(bool enabled);
}

public interface ISecureCredentialStore
{
	Task<string?> GetAsync(string key);
	Task SetAsync(string key, string value);
	Task DeleteAsync(string key);
}

public interface IPopupPlacementService
{
	Point GetPreferredPopupPosition(double width, double height);
}
