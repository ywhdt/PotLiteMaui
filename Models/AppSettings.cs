namespace PotLiteMaui.Models;

public static class TranslationProviderIds
{
	public const string GoogleWeb = "GoogleWeb";
	public const string BingDictionary = "AzureDictionary";
	public const string AzureDictionary = BingDictionary;
	public const string OpenAI = "OpenAI";
}

public static class SecureCredentialKeys
{
	public const string OpenAIApiKey = "OpenAIApiKey";
}

public sealed class AppSettings
{
	public bool GlobalHotkeyEnabled { get; set; } = true;
	public string Hotkey { get; set; } = "Ctrl+Alt+T";
	public string SourceLanguage { get; set; } = "auto";
	public string TargetLanguage { get; set; } = "zh";
	public string DefaultProvider { get; set; } = TranslationProviderIds.GoogleWeb;
	public bool MultiProviderEnabled { get; set; }
	public List<string> EnabledProviderIds { get; set; } = [TranslationProviderIds.GoogleWeb];
	public bool RunInTray { get; set; } = true;
	public bool StartWithSystem { get; set; }
	public bool HistoryEnabled { get; set; } = true;
	public int HistoryLimit { get; set; }
	public int PopupAutoHideSeconds { get; set; } = 12;
	public int ResultFontSize { get; set; } = 15;
	public string OpenAIModel { get; set; } = "gpt-4o-mini";
	public string OpenAICustomPrompt { get; set; } = string.Empty;
	public int CopyDelayMs { get; set; } = 180;
}
