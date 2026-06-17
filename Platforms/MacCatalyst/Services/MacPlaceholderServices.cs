#if MACCATALYST
using PotLiteMaui.Models;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui.Platforms.MacCatalyst.Services;

public sealed class MacHotkeyService : IHotkeyService
{
	public event EventHandler? HotkeyPressed;
	public bool IsRegistered => false;
	public string HotkeyText { get; private set; } = string.Empty;
	public bool Register(string hotkey)
	{
		HotkeyText = hotkey;
		return false;
	}
	public void Unregister() { }
}

public sealed class MacTextSelectionService : ITextSelectionService
{
	public Task<string?> CaptureSelectedTextAsync(int copyDelayMs, CancellationToken cancellationToken = default)
	{
		throw new PlatformNotSupportedException("macOS 全局划词捕获适配器已预留，尚未实现");
	}
}

public sealed class MacTrayService : ITrayService
{
	public bool IsSupported => false;
	public void Initialize(Window mainWindow, Func<AppSettings> settingsProvider) { }
	public void SetListening(bool isListening) { }
}

public sealed class MacStartupService : IStartupService
{
	public bool IsSupported => false;
	public bool IsEnabled() => false;
	public void SetEnabled(bool enabled) { }
}
#endif
