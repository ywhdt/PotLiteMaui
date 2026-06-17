using Microsoft.Extensions.Logging;
using PotLiteMaui.Services;
using PotLiteMaui.Services.Platform;
using PotLiteMaui.Services.Translation;
#if WINDOWS
using PotLiteMaui.Platforms.Windows.Services;
#elif MACCATALYST
using PotLiteMaui.Platforms.MacCatalyst.Services;
#endif

namespace PotLiteMaui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton(new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30)
		});
		builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
		builder.Services.AddSingleton<ITranslationHistoryStore, JsonTranslationHistoryStore>();
		builder.Services.AddSingleton<ITranslationProvider, GoogleWebTranslateProvider>();
		builder.Services.AddSingleton<ITranslationProvider, BingDictionaryProvider>();
		builder.Services.AddSingleton<ITranslationProvider, OpenAITranslationProvider>();
		builder.Services.AddSingleton<ITranslationService, TranslationService>();
#if WINDOWS
		builder.Services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
		builder.Services.AddSingleton<ITextSelectionService, WindowsSelectionCaptureService>();
		builder.Services.AddSingleton<ITrayService, WindowsTrayService>();
		builder.Services.AddSingleton<IStartupService, WindowsStartupService>();
		builder.Services.AddSingleton<ISecureCredentialStore, WindowsSecureCredentialStore>();
		builder.Services.AddSingleton<IAudioPlaybackService, WindowsAudioPlaybackService>();
		builder.Services.AddSingleton<IResultPopupService, WindowsResultPopupService>();
#elif MACCATALYST
		builder.Services.AddSingleton<IHotkeyService, MacHotkeyService>();
		builder.Services.AddSingleton<ITextSelectionService, MacTextSelectionService>();
		builder.Services.AddSingleton<ITrayService, MacTrayService>();
		builder.Services.AddSingleton<IStartupService, MacStartupService>();
		builder.Services.AddSingleton<ISecureCredentialStore, MauiSecureCredentialStore>();
		builder.Services.AddSingleton<IAudioPlaybackService, LauncherAudioPlaybackService>();
		builder.Services.AddSingleton<IResultPopupService, DefaultResultPopupService>();
#else
		builder.Services.AddSingleton<IHotkeyService, UnsupportedHotkeyService>();
		builder.Services.AddSingleton<ITextSelectionService, UnsupportedTextSelectionService>();
		builder.Services.AddSingleton<ITrayService, UnsupportedTrayService>();
		builder.Services.AddSingleton<IStartupService, UnsupportedStartupService>();
		builder.Services.AddSingleton<ISecureCredentialStore, MauiSecureCredentialStore>();
		builder.Services.AddSingleton<IAudioPlaybackService, LauncherAudioPlaybackService>();
		builder.Services.AddSingleton<IResultPopupService, DefaultResultPopupService>();
#endif
		builder.Services.AddTransient<MainPage>();

		return builder.Build();
	}
}
