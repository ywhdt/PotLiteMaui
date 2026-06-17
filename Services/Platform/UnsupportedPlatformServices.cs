namespace PotLiteMaui.Services.Platform;

public sealed class UnsupportedHotkeyService : IHotkeyService
{
	public event EventHandler? HotkeyPressed;
	public bool IsRegistered => false;
	public string HotkeyText { get; private set; } = string.Empty;

	public bool Register(string hotkey)
	{
		HotkeyText = hotkey;
		return false;
	}

	public void Unregister()
	{
	}
}

public sealed class UnsupportedTextSelectionService : ITextSelectionService
{
	public Task<string?> CaptureSelectedTextAsync(int copyDelayMs, CancellationToken cancellationToken = default)
	{
		throw new PlatformNotSupportedException("当前平台暂不支持全局划词捕获");
	}
}

public sealed class UnsupportedTrayService : ITrayService
{
	public bool IsSupported => false;
	public void Initialize(Window mainWindow, Func<Models.AppSettings> settingsProvider) { }
	public void SetListening(bool isListening) { }
}

public sealed class UnsupportedStartupService : IStartupService
{
	public bool IsSupported => false;
	public bool IsEnabled() => false;
	public void SetEnabled(bool enabled) { }
}

public sealed class MauiSecureCredentialStore : ISecureCredentialStore
{
	public async Task<string?> GetAsync(string key)
	{
		try
		{
			return await SecureStorage.Default.GetAsync(key);
		}
		catch
		{
			return null;
		}
	}

	public async Task SetAsync(string key, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			SecureStorage.Default.Remove(key);
			return;
		}

		await SecureStorage.Default.SetAsync(key, value);
	}

	public Task DeleteAsync(string key)
	{
		SecureStorage.Default.Remove(key);
		return Task.CompletedTask;
	}
}

public sealed class DefaultResultPopupService : IResultPopupService
{
	public void Show(Models.TranslationBatchResult result, int autoHideSeconds)
	{
	}
}
