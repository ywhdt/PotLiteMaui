using PotLiteMaui.Models;
using PotLiteMaui.Services;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui;

public partial class MainPage : ContentPage
{
	private readonly ISettingsStore _settingsStore;
	private readonly ITranslationService _translationService;
	private readonly ITextSelectionService _textSelectionService;
	private readonly IHotkeyService _hotkeyService;
	private readonly ITrayService _trayService;
	private readonly IStartupService _startupService;
	private readonly ISecureCredentialStore _credentialStore;
	private readonly ITranslationHistoryStore _historyStore;
	private readonly IPopupPlacementService _popupPlacementService;
	private readonly PickerOption[] _sourceLanguages =
	[
		new("auto", "自动检测"),
		new("zh", "中文"),
		new("en", "英文"),
		new("ja", "日文"),
		new("ko", "韩文"),
		new("fr", "法文"),
		new("de", "德文"),
		new("es", "西班牙文")
	];
	private readonly PickerOption[] _targetLanguages =
	[
		new("zh", "中文"),
		new("en", "英文"),
		new("ja", "日文"),
		new("ko", "韩文"),
		new("fr", "法文"),
		new("de", "德文"),
		new("es", "西班牙文")
	];

	private AppSettings _settings = new();
	private PickerOption[] _providerOptions = [];
	private IReadOnlyList<HistoryEntry> _historyEntries = [];
	private bool _isLoading;
	private bool _isBusy;
	private bool _trayInitialized;

	public MainPage(
		ISettingsStore settingsStore,
		ITranslationService translationService,
		ITextSelectionService textSelectionService,
		IHotkeyService hotkeyService,
		ITrayService trayService,
		IStartupService startupService,
		ISecureCredentialStore credentialStore,
		ITranslationHistoryStore historyStore,
		IPopupPlacementService popupPlacementService)
	{
		InitializeComponent();
		_settingsStore = settingsStore;
		_translationService = translationService;
		_textSelectionService = textSelectionService;
		_hotkeyService = hotkeyService;
		_trayService = trayService;
		_startupService = startupService;
		_credentialStore = credentialStore;
		_historyStore = historyStore;
		_popupPlacementService = popupPlacementService;
		_hotkeyService.HotkeyPressed += OnHotkeyPressed;
		_providerOptions = _translationService.Providers
			.Select(provider => new PickerOption(provider.Id, provider.DisplayName))
			.ToArray();
		SourceLanguagePicker.ItemsSource = _sourceLanguages;
		TargetLanguagePicker.ItemsSource = _targetLanguages;
		ProviderPicker.ItemsSource = _providerOptions;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadSettingsAsync();
		if (!_trayInitialized && Window is not null)
		{
			_trayService.Initialize(Window, () => _settings);
			_trayInitialized = true;
		}
		await ApplyPlatformStateAsync();
		await ReloadHistoryAsync();
	}

	private async Task LoadSettingsAsync()
	{
		_isLoading = true;
		try
		{
			_settings = await _settingsStore.LoadAsync();
			if (_startupService.IsSupported)
			{
				_settings.StartWithSystem = _startupService.IsEnabled();
			}

			GlobalHotkeySwitch.IsToggled = _settings.GlobalHotkeyEnabled;
			HotkeyEntry.Text = _settings.Hotkey;
			RunInTraySwitch.IsToggled = _settings.RunInTray;
			StartWithSystemSwitch.IsToggled = _settings.StartWithSystem;
			StartWithSystemSwitch.IsEnabled = _startupService.IsSupported;
			HistoryEnabledSwitch.IsToggled = _settings.HistoryEnabled;
			HistoryLimitEntry.Text = _settings.HistoryLimit.ToString();
			SelectPickerValue(SourceLanguagePicker, _sourceLanguages, _settings.SourceLanguage);
			SelectPickerValue(TargetLanguagePicker, _targetLanguages, _settings.TargetLanguage);
			SelectPickerValue(ProviderPicker, _providerOptions, _settings.DefaultProvider);
			AzureEndpointEntry.Text = _settings.AzureEndpoint;
			AzureRegionEntry.Text = _settings.AzureRegion;
			OpenAIModelEntry.Text = _settings.OpenAIModel;
			OpenAICustomPromptEditor.Text = _settings.OpenAICustomPrompt;
			OpenAIKeyEntry.Text = await _credentialStore.GetAsync(SecureCredentialKeys.OpenAIApiKey) ?? string.Empty;
			AzureKeyEntry.Text = await _credentialStore.GetAsync(SecureCredentialKeys.AzureTranslatorKey) ?? string.Empty;
			UpdateProviderSections();
		}
		finally
		{
			_isLoading = false;
		}
	}

	private async void OnSettingsChanged(object? sender, EventArgs e)
	{
		if (_isLoading)
		{
			return;
		}

		CaptureSettingsFromUi();
		UpdateProviderSections();
		await _settingsStore.SaveAsync(_settings);
		await ApplyPlatformStateAsync();
	}

	private async void OnCredentialChanged(object? sender, EventArgs e)
	{
		if (_isLoading)
		{
			return;
		}

		await _credentialStore.SetAsync(SecureCredentialKeys.OpenAIApiKey, OpenAIKeyEntry.Text?.Trim() ?? string.Empty);
		await _credentialStore.SetAsync(SecureCredentialKeys.AzureTranslatorKey, AzureKeyEntry.Text?.Trim() ?? string.Empty);
	}

	private async void OnCaptureSelectionClicked(object? sender, EventArgs e)
	{
		await CaptureAndTranslateSelectionAsync();
	}

	private async void OnManualTranslateClicked(object? sender, EventArgs e)
	{
		await TranslateTextAsync(InputEditor.Text, showWindow: false);
	}

	private async void OnCopyResultClicked(object? sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(ResultEditor.Text))
		{
			SetStatus("没有可复制的结果");
			return;
		}

		await Clipboard.Default.SetTextAsync(ResultEditor.Text);
		SetStatus("结果已复制");
	}

	private async void OnDeleteHistoryClicked(object? sender, EventArgs e)
	{
		if (HistoryPicker.SelectedItem is not HistoryEntry entry)
		{
			SetStatus("请选择要删除的历史记录");
			return;
		}

		await _historyStore.DeleteAsync(entry.Id);
		await ReloadHistoryAsync();
		SetStatus("历史记录已删除");
	}

	private async void OnClearHistoryClicked(object? sender, EventArgs e)
	{
		await _historyStore.ClearAsync();
		await ReloadHistoryAsync();
		SetStatus("历史记录已清空");
	}

	private void OnHistorySelected(object? sender, EventArgs e)
	{
		if (HistoryPicker.SelectedItem is not HistoryEntry entry)
		{
			return;
		}

		InputEditor.Text = entry.SourceText;
		ResultEditor.Text = entry.ResultText;
	}

	private void OnHotkeyPressed(object? sender, EventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(async () => await CaptureAndTranslateSelectionAsync());
	}

	private async Task CaptureAndTranslateSelectionAsync()
	{
		if (_isBusy)
		{
			return;
		}

		try
		{
			SetBusy(true, "正在读取选中文本");
			var selectedText = await _textSelectionService.CaptureSelectedTextAsync(_settings.CopyDelayMs);
			if (string.IsNullOrWhiteSpace(selectedText))
			{
				SetStatus("没有读取到选中文本");
				return;
			}

			InputEditor.Text = selectedText.Trim();
			await TranslateTextAsync(InputEditor.Text, showWindow: true);
		}
		catch (Exception ex)
		{
			SetStatus(ex.Message);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async Task TranslateTextAsync(string? text, bool showWindow)
	{
		if (_isBusy && !showWindow)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			SetStatus("请输入原文");
			return;
		}

		try
		{
			if (!showWindow)
			{
				SetBusy(true, "正在翻译");
			}

			CaptureSettingsFromUi();
			await _settingsStore.SaveAsync(_settings);
			var result = await _translationService.TranslateAsync(new TranslationRequest(
				text.Trim(),
				_settings.SourceLanguage,
				_settings.TargetLanguage,
				_settings.DefaultProvider,
				_settings));
			ResultEditor.Text = result.DisplayText;
			await _historyStore.AddAsync(result, _settings);
			await ReloadHistoryAsync();
			SetStatus($"完成：{DateTime.Now:HH:mm:ss}");
			if (showWindow)
			{
				ShowResultWindow(result);
			}
		}
		catch (Exception ex)
		{
			SetStatus(ex.Message);
		}
		finally
		{
			if (!showWindow)
			{
				SetBusy(false);
			}
		}
	}

	private async Task ApplyPlatformStateAsync()
	{
		await Task.Yield();
		if (_settings.GlobalHotkeyEnabled)
		{
			var registered = _hotkeyService.Register(_settings.Hotkey);
			HotkeyStateLabel.Text = registered
				? $"已监听 {_hotkeyService.HotkeyText}"
				: "当前平台不支持或快捷键注册失败";
		}
		else
		{
			_hotkeyService.Unregister();
			HotkeyStateLabel.Text = "监听已关闭";
		}

		if (_startupService.IsSupported)
		{
			_startupService.SetEnabled(_settings.StartWithSystem);
		}

		_trayService.SetListening(_hotkeyService.IsRegistered);
	}

	private async Task ReloadHistoryAsync()
	{
		_historyEntries = await _historyStore.LoadAsync();
		HistoryPicker.ItemsSource = _historyEntries.ToList();
	}

	private void CaptureSettingsFromUi()
	{
		_settings.GlobalHotkeyEnabled = GlobalHotkeySwitch.IsToggled;
		_settings.Hotkey = string.IsNullOrWhiteSpace(HotkeyEntry.Text) ? "Ctrl+Alt+T" : HotkeyEntry.Text.Trim();
		_settings.RunInTray = RunInTraySwitch.IsToggled;
		_settings.StartWithSystem = StartWithSystemSwitch.IsToggled;
		_settings.HistoryEnabled = HistoryEnabledSwitch.IsToggled;
		_settings.HistoryLimit = int.TryParse(HistoryLimitEntry.Text, out var historyLimit) ? Math.Max(0, historyLimit) : 0;
		_settings.SourceLanguage = (SourceLanguagePicker.SelectedItem as PickerOption)?.Code ?? "auto";
		_settings.TargetLanguage = (TargetLanguagePicker.SelectedItem as PickerOption)?.Code ?? "zh";
		_settings.DefaultProvider = (ProviderPicker.SelectedItem as PickerOption)?.Code ?? TranslationProviderIds.GoogleWeb;
		_settings.AzureEndpoint = AzureEndpointEntry.Text?.Trim() ?? string.Empty;
		_settings.AzureRegion = AzureRegionEntry.Text?.Trim() ?? string.Empty;
		_settings.OpenAIModel = OpenAIModelEntry.Text?.Trim() ?? string.Empty;
		_settings.OpenAICustomPrompt = OpenAICustomPromptEditor.Text?.Trim() ?? string.Empty;
	}

	private void UpdateProviderSections()
	{
		var provider = (ProviderPicker.SelectedItem as PickerOption)?.Code ?? _settings.DefaultProvider;
		GoogleSection.IsVisible = provider == TranslationProviderIds.GoogleWeb;
		AzureSection.IsVisible = provider == TranslationProviderIds.AzureDictionary;
		OpenAISection.IsVisible = provider == TranslationProviderIds.OpenAI;
	}

	private void ShowResultWindow(TranslationResult result)
	{
		const double width = 520;
		const double height = 380;
		var location = _popupPlacementService.GetPreferredPopupPosition(width, height);
		var window = new Window(new TranslationResultPage(result, _settings.PopupAutoHideSeconds))
		{
			Title = "翻译结果",
			Width = width,
			Height = height,
			X = location.X,
			Y = location.Y
		};
		Application.Current?.OpenWindow(window);
	}

	private void SetBusy(bool isBusy, string? message = null)
	{
		_isBusy = isBusy;
		BusyIndicator.IsVisible = isBusy;
		BusyIndicator.IsRunning = isBusy;
		CaptureButton.IsEnabled = !isBusy;
		if (message is not null)
		{
			SetStatus(message);
		}
	}

	private void SetStatus(string message)
	{
		StatusLabel.Text = message;
	}

	private static void SelectPickerValue(Picker picker, PickerOption[] options, string code)
	{
		picker.SelectedItem = options.FirstOrDefault(item => item.Code == code) ?? options.FirstOrDefault();
	}

	private sealed record PickerOption(string Code, string Label)
	{
		public override string ToString() => Label;
	}
}
