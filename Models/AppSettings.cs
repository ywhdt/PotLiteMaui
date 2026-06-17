namespace PotLiteMaui.Models;

public static class TranslationProviderIds
{
	public const string GoogleWeb = "GoogleWeb";
	public const string AzureDictionary = "AzureDictionary";
	public const string OpenAI = "OpenAI";
}

public static class SecureCredentialKeys
{
	public const string OpenAIApiKey = "OpenAIApiKey";
	public const string AzureTranslatorKey = "AzureTranslatorKey";
}

public sealed class AppSettings
{
	public bool GlobalHotkeyEnabled { get; set; } = true;
	public string Hotkey { get; set; } = "Ctrl+Alt+T";
	public string SourceLanguage { get; set; } = "auto";
	public string TargetLanguage { get; set; } = "zh";
	public string DefaultProvider { get; set; } = TranslationProviderIds.GoogleWeb;
	public bool RunInTray { get; set; } = true;
	public bool StartWithSystem { get; set; }
	public bool HistoryEnabled { get; set; } = true;
	public int HistoryLimit { get; set; }
	public int PopupAutoHideSeconds { get; set; } = 12;
	public string OpenAIModel { get; set; } = "gpt-4o-mini";
	public string OpenAICustomPrompt { get; set; } = string.Empty;
	public string AzureEndpoint { get; set; } = "https://api.cognitive.microsofttranslator.com";
	public string AzureRegion { get; set; } = string.Empty;
	public int CopyDelayMs { get; set; } = 180;
}
