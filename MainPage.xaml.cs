using PotLiteMaui.Models;
using PotLiteMaui.Services;
using PotLiteMaui.Services.Platform;
using Microsoft.Maui.Controls.Shapes;

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
	private readonly IResultPopupService _resultPopupService;
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
	private TranslationBatchResult? _currentResult;
	private string _currentDisplayText = string.Empty;
	private HistoryEntry? _selectedHistoryEntry;
	private List<string> _providerOrder = [TranslationProviderIds.GoogleWeb];
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
		IResultPopupService resultPopupService)
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
		_resultPopupService = resultPopupService;
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
			MultiProviderCheckBox.IsChecked = _settings.MultiProviderEnabled;
			ApplyProviderSelectionsFromSettings();
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

	private async void OnMoveProviderUpClicked(object? sender, EventArgs e)
	{
		await MoveSelectedProviderAsync(-1);
	}

	private async void OnMoveProviderDownClicked(object? sender, EventArgs e)
	{
		await MoveSelectedProviderAsync(1);
	}

	private async void OnCopyResultClicked(object? sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_currentDisplayText))
		{
			SetStatus("没有可复制的结果");
			return;
		}

		await Clipboard.Default.SetTextAsync(_currentDisplayText);
		SetStatus("结果已复制");
	}

	private async void OnDeleteHistoryClicked(object? sender, EventArgs e)
	{
		if (_selectedHistoryEntry is not { } entry)
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
			SetBusy(false);
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
			var result = await _translationService.TranslateManyAsync(new TranslationBatchRequest(
				text.Trim(),
				_settings.SourceLanguage,
				_settings.TargetLanguage,
				GetSelectedProviderIds(),
				_settings));
			ShowResult(result);
			await _historyStore.AddAsync(result, _settings);
			await ReloadHistoryAsync();
			SetStatus(result.Failures.Count == 0
				? $"完成：{DateTime.Now:HH:mm:ss}"
				: $"完成 {result.Results.Count} 个，失败 {result.Failures.Count} 个");
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
		RenderHistoryList();
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
		_settings.MultiProviderEnabled = MultiProviderCheckBox.IsChecked;
		_settings.EnabledProviderIds = GetOrderedCheckedProviderIds().ToList();
		_settings.AzureEndpoint = AzureEndpointEntry.Text?.Trim() ?? string.Empty;
		_settings.AzureRegion = AzureRegionEntry.Text?.Trim() ?? string.Empty;
		_settings.OpenAIModel = OpenAIModelEntry.Text?.Trim() ?? string.Empty;
		_settings.OpenAICustomPrompt = OpenAICustomPromptEditor.Text?.Trim() ?? string.Empty;
	}

	private void UpdateProviderSections()
	{
		var providers = GetVisibleProviderIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
		ProviderPicker.IsEnabled = !MultiProviderCheckBox.IsChecked;
		MultiProviderOptionsGrid.IsEnabled = MultiProviderCheckBox.IsChecked;
		ProviderOrderGrid.IsEnabled = MultiProviderCheckBox.IsChecked;
		ProviderOrderGrid.IsVisible = MultiProviderCheckBox.IsChecked;
		SyncProviderOrderWithChecks();
		RenderProviderOrderPicker();
		GoogleSection.IsVisible = providers.Contains(TranslationProviderIds.GoogleWeb);
		AzureSection.IsVisible = providers.Contains(TranslationProviderIds.AzureDictionary);
		OpenAISection.IsVisible = providers.Contains(TranslationProviderIds.OpenAI);
	}

	private IReadOnlyList<string> GetSelectedProviderIds()
	{
		if (!MultiProviderCheckBox.IsChecked)
		{
			return [(ProviderPicker.SelectedItem as PickerOption)?.Code ?? _settings.DefaultProvider];
		}

		var selected = GetOrderedCheckedProviderIds().ToArray();
		return selected.Length > 0 ? selected : [_settings.DefaultProvider];
	}

	private IEnumerable<string> GetOrderedCheckedProviderIds()
	{
		SyncProviderOrderWithChecks();
		return _providerOrder.Where(IsProviderChecked).ToArray();
	}

	private IEnumerable<string> GetCheckedProviderIdsInDefaultOrder()
	{
		if (GoogleProviderCheckBox.IsChecked)
		{
			yield return TranslationProviderIds.GoogleWeb;
		}

		if (AzureProviderCheckBox.IsChecked)
		{
			yield return TranslationProviderIds.AzureDictionary;
		}

		if (OpenAIProviderCheckBox.IsChecked)
		{
			yield return TranslationProviderIds.OpenAI;
		}
	}

	private IEnumerable<string> GetVisibleProviderIds()
	{
		return MultiProviderCheckBox.IsChecked
			? GetSelectedProviderIds()
			: [(ProviderPicker.SelectedItem as PickerOption)?.Code ?? _settings.DefaultProvider];
	}

	private void ApplyProviderSelectionsFromSettings()
	{
		_providerOrder = NormalizeProviderOrder(_settings.EnabledProviderIds);
		var selected = (_settings.EnabledProviderIds.Count > 0
				? _settings.EnabledProviderIds
				: [_settings.DefaultProvider])
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		GoogleProviderCheckBox.IsChecked = selected.Contains(TranslationProviderIds.GoogleWeb);
		AzureProviderCheckBox.IsChecked = selected.Contains(TranslationProviderIds.AzureDictionary);
		OpenAIProviderCheckBox.IsChecked = selected.Contains(TranslationProviderIds.OpenAI);
	}

	private async Task MoveSelectedProviderAsync(int direction)
	{
		if (ProviderOrderPicker.SelectedItem is not PickerOption selected)
		{
			SetStatus("请选择要调整顺序的服务");
			return;
		}

		SyncProviderOrderWithChecks();
		var index = _providerOrder.FindIndex(id => string.Equals(id, selected.Code, StringComparison.OrdinalIgnoreCase));
		var nextIndex = index + direction;
		if (index < 0 || nextIndex < 0 || nextIndex >= _providerOrder.Count)
		{
			return;
		}

		(_providerOrder[index], _providerOrder[nextIndex]) = (_providerOrder[nextIndex], _providerOrder[index]);
		_settings.EnabledProviderIds = GetOrderedCheckedProviderIds().ToList();
		RenderProviderOrderPicker(selected.Code);
		await _settingsStore.SaveAsync(_settings);
		SetStatus("显示顺序已更新");
	}

	private void SyncProviderOrderWithChecks()
	{
		var checkedIds = GetCheckedProviderIdsInDefaultOrder().ToHashSet(StringComparer.OrdinalIgnoreCase);
		_providerOrder = NormalizeProviderOrder(_providerOrder)
			.Where(checkedIds.Contains)
			.ToList();
		foreach (var id in GetCheckedProviderIdsInDefaultOrder())
		{
			if (!_providerOrder.Contains(id, StringComparer.OrdinalIgnoreCase))
			{
				_providerOrder.Add(id);
			}
		}
	}

	private void RenderProviderOrderPicker(string? selectedCode = null)
	{
		var currentCode = (ProviderOrderPicker.SelectedItem as PickerOption)?.Code;
		var options = GetOrderedCheckedProviderIds()
			.Select(id => _providerOptions.FirstOrDefault(item => item.Code == id) ?? new PickerOption(id, id))
			.ToArray();
		ProviderOrderPicker.ItemsSource = options;
		if (options.Length == 0)
		{
			ProviderOrderPicker.SelectedItem = null;
			return;
		}

		ProviderOrderPicker.SelectedItem = options.FirstOrDefault(item => item.Code == selectedCode)
			?? options.FirstOrDefault(item => item.Code == currentCode)
			?? options[0];
	}

	private List<string> NormalizeProviderOrder(IEnumerable<string> providerIds)
	{
		var knownIds = _providerOptions.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var normalized = providerIds
			.Where(id => knownIds.Contains(id))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		foreach (var id in _providerOptions.Select(item => item.Code))
		{
			if (!normalized.Contains(id, StringComparer.OrdinalIgnoreCase))
			{
				normalized.Add(id);
			}
		}

		return normalized;
	}

	private bool IsProviderChecked(string providerId)
	{
		return providerId switch
		{
			TranslationProviderIds.GoogleWeb => GoogleProviderCheckBox.IsChecked,
			TranslationProviderIds.AzureDictionary => AzureProviderCheckBox.IsChecked,
			TranslationProviderIds.OpenAI => OpenAIProviderCheckBox.IsChecked,
			_ => false
		};
	}

	private void ShowResult(TranslationBatchResult result)
	{
		_currentResult = result;
		_currentDisplayText = result.DisplayText;
		ResultStack.Children.Clear();

		foreach (var item in result.DisplayItems)
		{
			ResultStack.Children.Add(CreateResultCard(
				item.IsSuccess ? item.ProviderName : $"{item.ProviderName} 失败",
				item.Text,
				isError: !item.IsSuccess));
		}

		if (ResultStack.Children.Count == 0)
		{
			ResultStack.Children.Add(CreateMutedLabel("没有翻译结果"));
		}
	}

	private void RenderHistoryList()
	{
		HistoryStack.Children.Clear();
		if (_historyEntries.Count == 0)
		{
			_selectedHistoryEntry = null;
			HistoryDetailBorder.IsVisible = false;
			HistoryStack.Children.Add(CreateMutedLabel("暂无历史记录"));
			return;
		}

		foreach (var entry in _historyEntries)
		{
			HistoryStack.Children.Add(CreateHistoryCard(entry));
		}
	}

	private View CreateHistoryCard(HistoryEntry entry)
	{
		var title = new Label
		{
			Text = entry.DisplayTitle,
			FontAttributes = FontAttributes.Bold,
			FontSize = 14,
			LineBreakMode = LineBreakMode.TailTruncation
		};
		var subtitle = new Label
		{
			Text = entry.DisplaySubtitle,
			FontSize = 12,
			TextColor = Colors.SlateGray
		};
		var layout = new VerticalStackLayout
		{
			Spacing = 4,
			Children = { title, subtitle }
		};
		var border = new Border
		{
			Stroke = Colors.LightGray,
			StrokeShape = new RoundRectangle { CornerRadius = 8 },
			Padding = 10,
			Content = layout
		};
		border.GestureRecognizers.Add(new TapGestureRecognizer
		{
			Command = new Command(() => SelectHistoryEntry(entry))
		});
		return border;
	}

	private void SelectHistoryEntry(HistoryEntry entry)
	{
		_selectedHistoryEntry = entry;
		InputEditor.Text = entry.SourceText;
		_currentResult = null;
		_currentDisplayText = entry.ResultText;
		HistoryDetailBorder.IsVisible = true;
		HistoryDetailTitleLabel.Text = entry.DisplaySubtitle;
		HistoryDetailSourceLabel.Text = entry.SourceText;
		HistoryDetailResultLabel.Text = entry.ResultText;
		ShowHistoryResult(entry);
		SetStatus("已载入历史记录");
	}

	private void ShowHistoryResult(HistoryEntry entry)
	{
		ResultStack.Children.Clear();
		var items = entry.Results.Count > 0
			? entry.Results
			: [new HistoryResultItem
			{
				ProviderId = entry.ProviderId,
				ProviderName = entry.ProviderName,
				IsSuccess = true,
				Text = entry.ResultText
			}];

		foreach (var item in OrderHistoryItems(items))
		{
			ResultStack.Children.Add(CreateResultCard(
				item.IsSuccess ? item.ProviderName : $"{item.ProviderName} 失败",
				item.Text,
				isError: !item.IsSuccess));
		}
	}

	private static IEnumerable<HistoryResultItem> OrderHistoryItems(IEnumerable<HistoryResultItem> items)
	{
		var list = items.ToList();
		return list.Any(item => item.Order != 0)
			? list.OrderBy(item => item.Order)
			: list;
	}

	private static View CreateResultCard(string title, string text, bool isError)
	{
		var titleLabel = new Label
		{
			Text = title,
			FontSize = 13,
			FontAttributes = FontAttributes.Bold,
			TextColor = isError ? Colors.Firebrick : Colors.DarkSlateGray
		};
		var bodyLabel = new Label
		{
			Text = text,
			FontSize = 15,
			LineBreakMode = LineBreakMode.WordWrap
		};
		return new Border
		{
			Stroke = isError ? Colors.IndianRed : Colors.LightGray,
			StrokeShape = new RoundRectangle { CornerRadius = 8 },
			Padding = 12,
			Content = new VerticalStackLayout
			{
				Spacing = 6,
				Children = { titleLabel, bodyLabel }
			}
		};
	}

	private static Label CreateMutedLabel(string text)
	{
		return new Label
		{
			Text = text,
			TextColor = Colors.SlateGray
		};
	}

	private void ShowResultWindow(TranslationBatchResult result)
	{
		_resultPopupService.Show(result, _settings.PopupAutoHideSeconds);
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
