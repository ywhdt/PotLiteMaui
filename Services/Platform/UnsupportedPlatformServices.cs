using Microsoft.Maui.ApplicationModel;

namespace PotLiteMaui.Services.Platform;

public sealed class UnsupportedHotkeyService : IHotkeyService
{
	public event EventHandler? HotkeyPressed
	{
		add { }
		remove { }
	}

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

public sealed class LauncherAudioPlaybackService : IAudioPlaybackService
{
	public async Task PlayAsync(string audioUrl, CancellationToken cancellationToken = default)
	{
		if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
		{
			throw new InvalidOperationException("发音地址无效");
		}

		await Launcher.Default.OpenAsync(uri);
	}
}

public sealed class DefaultResultPopupService : IResultPopupService
{
	public IResultPopupSession? ShowLoading(string sourceText, string message, double resultFontSize)
	{
		return null;
	}

	public void UpdateLoading(IResultPopupSession? session, string sourceText, string message, double resultFontSize)
	{
	}

	public void Update(IResultPopupSession? session, Models.TranslationBatchResult result, int autoHideSeconds, double resultFontSize)
	{
	}

	public void Show(Models.TranslationBatchResult result, int autoHideSeconds, double resultFontSize)
	{
	}
}
