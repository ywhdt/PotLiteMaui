using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PotLiteMaui.Models;
using PotLiteMaui.Services.Platform;
using WinRT.Interop;
using WinUIBorder = Microsoft.UI.Xaml.Controls.Border;
using WinUIButton = Microsoft.UI.Xaml.Controls.Button;
using WinUIColumnDefinition = Microsoft.UI.Xaml.Controls.ColumnDefinition;
using WinUIColors = Microsoft.UI.Colors;
using WinUICornerRadius = Microsoft.UI.Xaml.CornerRadius;
using WinUIGrid = Microsoft.UI.Xaml.Controls.Grid;
using WinUIGridLength = Microsoft.UI.Xaml.GridLength;
using WinUIGridUnitType = Microsoft.UI.Xaml.GridUnitType;
using WinUIHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using WinUIHyperlinkButton = Microsoft.UI.Xaml.Controls.HyperlinkButton;
using WinUIOrientation = Microsoft.UI.Xaml.Controls.Orientation;
using WinUIRowDefinition = Microsoft.UI.Xaml.Controls.RowDefinition;
using WinUIScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using WinUIScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using WinUISolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WinUIStackPanel = Microsoft.UI.Xaml.Controls.StackPanel;
using WinUIThickness = Microsoft.UI.Xaml.Thickness;
using WinUITextBlock = Microsoft.UI.Xaml.Controls.TextBlock;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsResultPopupService : IResultPopupService
{
	private static readonly nint HwndTopmost = new(-1);
	private const int GwlExStyle = -20;
	private const int SwShownormal = 1;
	private const long WsExToolWindow = 0x00000080L;
	private const long WsExAppWindow = 0x00040000L;
	private const uint SwpNoSize = 0x0001;
	private const uint SwpNoMove = 0x0002;
	private const uint SwpNoActivate = 0x0010;
	private const uint SwpFrameChanged = 0x0020;
	private const uint SwpShowWindow = 0x0040;
	private const int DefaultWidth = 560;
	private const int DefaultHeight = 430;
	private const int MinWidth = 360;
	private const int MinHeight = 260;
	private const int MaxWidth = 1100;
	private const int MaxHeight = 900;

	private readonly List<PopupSession> _sessions = [];
	private readonly string _statePath = Path.Combine(FileSystem.AppDataDirectory, "popup-window.json");
	private readonly IAudioPlaybackService _audioPlaybackService;

	public WindowsResultPopupService(IAudioPlaybackService audioPlaybackService)
	{
		_audioPlaybackService = audioPlaybackService;
	}

	public void Show(TranslationBatchResult result, int autoHideSeconds, double resultFontSize)
	{
		MainThread.BeginInvokeOnMainThread(() => ShowCore(
			result,
			Math.Clamp(autoHideSeconds, 0, 60),
			ClampResultFontSize(resultFontSize)));
	}

	public IResultPopupSession? ShowLoading(string sourceText, string message, double resultFontSize)
	{
		if (!MainThread.IsMainThread)
		{
			MainThread.BeginInvokeOnMainThread(() => ShowLoadingCore(sourceText, message, ClampResultFontSize(resultFontSize)));
			return null;
		}

		return ShowLoadingCore(sourceText, message, ClampResultFontSize(resultFontSize));
	}

	public void UpdateLoading(IResultPopupSession? session, string sourceText, string message, double resultFontSize)
	{
		if (session is not PopupSession popupSession)
		{
			return;
		}

		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (_sessions.Contains(popupSession))
			{
				popupSession.Window.Content = CreateLoadingContent(
					sourceText,
					message,
					popupSession.State,
					() => popupSession.Window.Close(),
					ClampResultFontSize(resultFontSize));
			}
		});
	}

	public void Update(IResultPopupSession? session, TranslationBatchResult result, int autoHideSeconds, double resultFontSize)
	{
		if (session is not PopupSession popupSession)
		{
			Show(result, autoHideSeconds, resultFontSize);
			return;
		}

		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (!_sessions.Contains(popupSession))
			{
				return;
			}

			popupSession.Window.Content = CreateContent(
				result,
				popupSession.State,
				() => popupSession.Window.Close(),
				_audioPlaybackService,
				ClampResultFontSize(resultFontSize));

			var clampedAutoHide = Math.Clamp(autoHideSeconds, 0, 60);
			if (clampedAutoHide > 0)
			{
				_ = AutoCloseAsync(popupSession, clampedAutoHide);
			}
		});
	}

	private void ShowCore(TranslationBatchResult result, int autoHideSeconds, double resultFontSize)
	{
		CloseUnpinnedSessions();

		var session = CreateSession();
		session.Window.Content = CreateContent(result, session.State, () => session.Window.Close(), _audioPlaybackService, resultFontSize);
		ShowSession(session);

		if (autoHideSeconds > 0)
		{
			_ = AutoCloseAsync(session, autoHideSeconds);
		}
	}

	private PopupSession ShowLoadingCore(string sourceText, string message, double resultFontSize)
	{
		CloseUnpinnedSessions();

		var session = CreateSession();
		session.Window.Content = CreateLoadingContent(
			sourceText,
			message,
			session.State,
			() => session.Window.Close(),
			resultFontSize);
		ShowSession(session);
		return session;
	}

	private PopupSession CreateSession()
	{
		var state = new PopupState();
		var session = new PopupSession(new WinUIWindow { Title = "翻译结果" }, state);
		session.Window.Closed += (_, _) =>
		{
			SaveWindowSize(session.Window);
			_sessions.Remove(session);
		};
		session.Window.Activated += (_, args) =>
		{
			if (state.CanCloseOnDeactivate &&
				!state.IsPinned &&
				args.WindowActivationState == WindowActivationState.Deactivated)
			{
				session.Window.Close();
			}
		};
		_sessions.Add(session);
		return session;
	}

	private void ShowSession(PopupSession session)
	{
		PrepareWindowBeforeShow(session.Window);
		session.Window.Activate();
		PromoteWindow(session.Window);
		EnableCloseOnDeactivate(session);
	}

	private static WinUIGrid CreateContent(
		TranslationBatchResult result,
		PopupState state,
		Action close,
		IAudioPlaybackService audioPlaybackService,
		double resultFontSize)
	{
		var root = new WinUIGrid
		{
			Padding = new WinUIThickness(18),
			Background = new WinUISolidColorBrush(WinUIColors.FloralWhite),
			RowDefinitions =
			{
				new WinUIRowDefinition { Height = WinUIGridLength.Auto },
				new WinUIRowDefinition { Height = new WinUIGridLength(1, WinUIGridUnitType.Star) },
				new WinUIRowDefinition { Height = WinUIGridLength.Auto }
			}
		};

		var header = new WinUIBorder
		{
			Background = new WinUISolidColorBrush(WinUIColors.White),
			BorderBrush = new WinUISolidColorBrush(WinUIColors.Gainsboro),
			BorderThickness = new WinUIThickness(1),
			CornerRadius = new WinUICornerRadius(12),
			Padding = new WinUIThickness(12),
			Child = CreateHeader(result)
		};
		WinUIGrid.SetRow(header, 0);
		root.Children.Add(header);

		var resultStack = new WinUIStackPanel { Spacing = 10 };
		var sections = result.Results
			.Select(item => (item.Order, Section: CreateResultSection(item, audioPlaybackService, resultFontSize)))
			.Concat(result.Failures.Select(item => (
				item.Order,
				Section: CreateResultSection($"{item.ProviderName} 失败", item.ErrorMessage, isError: true, resultFontSize))));
		foreach (var item in sections.OrderBy(item => item.Order))
		{
			resultStack.Children.Add(item.Section);
		}

		if (resultStack.Children.Count == 0)
		{
			resultStack.Children.Add(new WinUITextBlock { Text = "没有翻译结果" });
		}

		var scroll = new WinUIScrollViewer
		{
			Content = resultStack,
			Margin = new WinUIThickness(0, 12, 0, 12),
			VerticalScrollBarVisibility = WinUIScrollBarVisibility.Auto
		};
		WinUIGrid.SetRow(scroll, 1);
		root.Children.Add(scroll);

		var buttons = CreateFooterButtons(state, close, result.DisplayText);
		WinUIGrid.SetRow(buttons, 2);
		root.Children.Add(buttons);

		return root;
	}

	private static WinUIGrid CreateLoadingContent(
		string sourceText,
		string message,
		PopupState state,
		Action close,
		double resultFontSize)
	{
		var root = new WinUIGrid
		{
			Padding = new WinUIThickness(18),
			Background = new WinUISolidColorBrush(WinUIColors.FloralWhite),
			RowDefinitions =
			{
				new WinUIRowDefinition { Height = WinUIGridLength.Auto },
				new WinUIRowDefinition { Height = new WinUIGridLength(1, WinUIGridUnitType.Star) },
				new WinUIRowDefinition { Height = WinUIGridLength.Auto }
			}
		};

		var header = new WinUIBorder
		{
			Background = new WinUISolidColorBrush(WinUIColors.White),
			BorderBrush = new WinUISolidColorBrush(WinUIColors.Gainsboro),
			BorderThickness = new WinUIThickness(1),
			CornerRadius = new WinUICornerRadius(12),
			Padding = new WinUIThickness(12),
			Child = CreateHeader("翻译中", sourceText, DateTimeOffset.Now)
		};
		WinUIGrid.SetRow(header, 0);
		root.Children.Add(header);

		var stack = new WinUIStackPanel { Spacing = 10 };
		stack.Children.Add(CreateResultSection("状态", message, isError: false, resultFontSize));
		var scroll = new WinUIScrollViewer
		{
			Content = stack,
			Margin = new WinUIThickness(0, 12, 0, 12),
			VerticalScrollBarVisibility = WinUIScrollBarVisibility.Auto
		};
		WinUIGrid.SetRow(scroll, 1);
		root.Children.Add(scroll);

		var buttons = CreateFooterButtons(state, close, copyText: null);
		WinUIGrid.SetRow(buttons, 2);
		root.Children.Add(buttons);
		return root;
	}

	private static WinUIStackPanel CreateFooterButtons(PopupState state, Action close, string? copyText)
	{
		var buttons = new WinUIStackPanel
		{
			Orientation = WinUIOrientation.Horizontal,
			HorizontalAlignment = WinUIHorizontalAlignment.Right,
			Spacing = 8
		};

		if (!string.IsNullOrWhiteSpace(copyText))
		{
			var copyButton = CreateActionButton("复制");
			copyButton.Click += async (_, _) => await Clipboard.Default.SetTextAsync(copyText);
			buttons.Children.Add(copyButton);
		}

		var pinButton = CreateActionButton(state.IsPinned ? "取消固定" : "固定");
		pinButton.Click += (_, _) =>
		{
			state.IsPinned = !state.IsPinned;
			pinButton.Content = state.IsPinned ? "取消固定" : "固定";
		};
		var closeButton = CreateActionButton("关闭");
		closeButton.Click += (_, _) => close();
		buttons.Children.Add(pinButton);
		buttons.Children.Add(closeButton);
		return buttons;
	}

	private static WinUIStackPanel CreateHeader(TranslationBatchResult result)
	{
		return CreateHeader(result.ProviderName, result.SourceText, result.CreatedAt);
	}

	private static WinUIStackPanel CreateHeader(string title, string sourceText, DateTimeOffset createdAt)
	{
		var header = new WinUIStackPanel { Spacing = 5 };
		header.Children.Add(new WinUITextBlock
		{
			Text = $"{title}  {createdAt:HH:mm:ss}",
			FontSize = 13,
			Foreground = new WinUISolidColorBrush(WinUIColors.Teal)
		});
		header.Children.Add(new WinUITextBlock
		{
			Text = string.IsNullOrWhiteSpace(sourceText) ? "正在读取选中文本" : sourceText,
			FontSize = 15,
			MaxLines = 2,
			TextTrimming = TextTrimming.CharacterEllipsis,
			TextWrapping = TextWrapping.Wrap
		});
		return header;
	}

	private static WinUIButton CreateActionButton(string text)
	{
		return new WinUIButton
		{
			Content = text,
			MinWidth = 72,
			Padding = new WinUIThickness(12, 6, 12, 6)
		};
	}

	private static double ClampResultFontSize(double value)
	{
		return Math.Clamp(value, 12, 30);
	}

	private static WinUIBorder CreateResultSection(
		TranslationResult result,
		IAudioPlaybackService audioPlaybackService,
		double resultFontSize)
	{
		return CreateResultSection(
			result.ProviderName,
			result.DisplayText,
			isError: false,
			resultFontSize,
			result.Dictionary,
			audioPlaybackService);
	}

	private static WinUIBorder CreateResultSection(string title, string text, bool isError, double resultFontSize)
	{
		return CreateResultSection(title, text, isError, resultFontSize, dictionary: null, audioPlaybackService: null);
	}

	private static WinUIBorder CreateResultSection(
		string title,
		string text,
		bool isError,
		double resultFontSize,
		DictionaryResult? dictionary,
		IAudioPlaybackService? audioPlaybackService)
	{
		var panel = new WinUIStackPanel { Spacing = 7 };
		panel.Children.Add(new WinUITextBlock
		{
			Text = title,
			FontSize = Math.Max(12, resultFontSize - 2),
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			Foreground = new WinUISolidColorBrush(isError ? WinUIColors.Firebrick : WinUIColors.DarkSlateGray)
		});
		if (dictionary is not null)
		{
			AddDictionaryContent(panel, dictionary, audioPlaybackService, resultFontSize);
		}
		else
		{
			panel.Children.Add(new WinUITextBlock
			{
				Text = text,
				FontSize = resultFontSize,
				TextWrapping = TextWrapping.Wrap
			});
		}

		return new WinUIBorder
		{
			Background = new WinUISolidColorBrush(WinUIColors.White),
			BorderBrush = new WinUISolidColorBrush(isError ? WinUIColors.IndianRed : WinUIColors.LightGray),
			BorderThickness = new WinUIThickness(1),
			CornerRadius = new WinUICornerRadius(10),
			Padding = new WinUIThickness(12),
			Child = panel
		};
	}

	private static void AddDictionaryContent(
		WinUIStackPanel panel,
		DictionaryResult dictionary,
		IAudioPlaybackService? audioPlaybackService,
		double resultFontSize)
	{
		if (!string.IsNullOrWhiteSpace(dictionary.Term))
		{
			panel.Children.Add(new WinUITextBlock
			{
				Text = dictionary.Term,
				FontSize = resultFontSize + 3,
				FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
				TextWrapping = TextWrapping.Wrap
			});
		}

		AddPronunciationRows(panel, dictionary, audioPlaybackService, resultFontSize);

		AddDictionaryTranslations(panel, dictionary, resultFontSize);
		AddDictionaryExamples(panel, dictionary, resultFontSize);
		AddDictionarySourceLink(panel, dictionary, resultFontSize);
	}

	private static void AddDictionaryTranslations(
		WinUIStackPanel panel,
		DictionaryResult dictionary,
		double resultFontSize)
	{
		var translations = dictionary.Translations
			.Where(item => !string.IsNullOrWhiteSpace(item.DisplayTarget))
			.Take(8)
			.ToList();
		if (translations.Count == 0)
		{
			return;
		}

		panel.Children.Add(CreateDictionarySectionTitle("释义", resultFontSize));
		foreach (var group in translations.GroupBy(item => item.DisplayPartOfSpeech))
		{
			panel.Children.Add(CreateTranslationGroup(group.Key, group.ToList(), resultFontSize));
		}
	}

	private static WinUIGrid CreateTranslationGroup(
		string partOfSpeech,
		IReadOnlyList<DictionaryTranslation> translations,
		double resultFontSize)
	{
		var grid = new WinUIGrid
		{
			ColumnSpacing = 10,
			Margin = new WinUIThickness(0, 2, 0, 4)
		};
		grid.ColumnDefinitions.Add(new WinUIColumnDefinition { Width = WinUIGridLength.Auto });
		grid.ColumnDefinitions.Add(new WinUIColumnDefinition { Width = new WinUIGridLength(1, WinUIGridUnitType.Star) });

		var badge = CreatePartOfSpeechBadge(partOfSpeech, resultFontSize);
		WinUIGrid.SetColumn(badge, 0);
		grid.Children.Add(badge);

		var definitions = new WinUIStackPanel { Spacing = 5 };
		foreach (var translation in translations)
		{
			definitions.Children.Add(new WinUITextBlock
			{
				Text = translation.DisplayTarget,
				FontSize = resultFontSize,
				TextWrapping = TextWrapping.Wrap
			});

			if (translation.BackTranslations.Count > 0)
			{
				definitions.Children.Add(new WinUITextBlock
				{
					Text = $"相关：{string.Join(", ", translation.BackTranslations.Take(5))}",
					FontSize = Math.Max(12, resultFontSize - 2),
					Foreground = new WinUISolidColorBrush(WinUIColors.DimGray),
					TextWrapping = TextWrapping.Wrap
				});
			}
		}

		WinUIGrid.SetColumn(definitions, 1);
		grid.Children.Add(definitions);
		return grid;
	}

	private static void AddDictionaryExamples(
		WinUIStackPanel panel,
		DictionaryResult dictionary,
		double resultFontSize)
	{
		var examples = dictionary.Examples
			.Where(item => !string.IsNullOrWhiteSpace(item.Source) && !string.IsNullOrWhiteSpace(item.Target))
			.Take(4)
			.ToList();
		if (examples.Count == 0)
		{
			return;
		}

		panel.Children.Add(CreateDictionarySectionTitle("例句", resultFontSize));
		foreach (var example in examples)
		{
			panel.Children.Add(CreateExampleBlock(example, resultFontSize));
		}
	}

	private static WinUIGrid CreateExampleBlock(DictionaryExample example, double resultFontSize)
	{
		var grid = new WinUIGrid
		{
			ColumnSpacing = 9,
			Margin = new WinUIThickness(0, 2, 0, 6)
		};
		grid.ColumnDefinitions.Add(new WinUIColumnDefinition { Width = new WinUIGridLength(3) });
		grid.ColumnDefinitions.Add(new WinUIColumnDefinition { Width = new WinUIGridLength(1, WinUIGridUnitType.Star) });

		grid.Children.Add(new WinUIBorder
		{
			Width = 3,
			Background = new WinUISolidColorBrush(WinUIColors.Teal),
			CornerRadius = new WinUICornerRadius(2),
			VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch
		});

		var text = new WinUIStackPanel { Spacing = 3 };
		text.Children.Add(new WinUITextBlock
		{
			Text = example.Source,
			FontSize = Math.Max(12, resultFontSize - 1),
			TextWrapping = TextWrapping.Wrap
		});
		text.Children.Add(new WinUITextBlock
		{
			Text = example.Target,
			FontSize = Math.Max(12, resultFontSize - 2),
			Foreground = new WinUISolidColorBrush(WinUIColors.DimGray),
			TextWrapping = TextWrapping.Wrap
		});
		WinUIGrid.SetColumn(text, 1);
		grid.Children.Add(text);
		return grid;
	}

	private static WinUITextBlock CreateDictionarySectionTitle(string text, double resultFontSize)
	{
		return new WinUITextBlock
		{
			Text = text,
			FontSize = Math.Max(12, resultFontSize - 1),
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			Foreground = new WinUISolidColorBrush(WinUIColors.Teal),
			Margin = new WinUIThickness(0, 6, 0, 0)
		};
	}

	private static WinUIBorder CreatePartOfSpeechBadge(string text, double resultFontSize)
	{
		return new WinUIBorder
		{
			Background = new WinUISolidColorBrush(WinUIColors.LightCyan),
			BorderBrush = new WinUISolidColorBrush(WinUIColors.PowderBlue),
			BorderThickness = new WinUIThickness(1),
			CornerRadius = new WinUICornerRadius(6),
			Padding = new WinUIThickness(8, 3, 8, 3),
			Child = new WinUITextBlock
			{
				Text = text,
				FontSize = Math.Max(12, resultFontSize - 3),
				FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
				Foreground = new WinUISolidColorBrush(WinUIColors.DarkCyan)
			}
		};
	}

	private static void AddPronunciationRows(
		WinUIStackPanel panel,
		DictionaryResult dictionary,
		IAudioPlaybackService? audioPlaybackService,
		double resultFontSize)
	{
		var hasAudio = audioPlaybackService is not null &&
			dictionary.Pronunciations.Any(item => !string.IsNullOrWhiteSpace(item.AudioUrl));
		if (!hasAudio)
		{
			return;
		}

		var row = new WinUIStackPanel
		{
			Orientation = WinUIOrientation.Horizontal,
			Spacing = 8
		};

		if (audioPlaybackService is not null)
		{
			foreach (var pronunciation in dictionary.Pronunciations.Where(item => !string.IsNullOrWhiteSpace(item.AudioUrl)))
			{
				var button = CreateActionButton(pronunciation.DisplayLabel);
				button.FontSize = Math.Max(12, resultFontSize - 3);
				button.Click += async (_, _) => await audioPlaybackService.PlayAsync(pronunciation.AudioUrl);
				row.Children.Add(button);
				if (!string.IsNullOrWhiteSpace(pronunciation.Phonetic))
				{
					row.Children.Add(new WinUITextBlock
					{
						Text = pronunciation.Phonetic,
						FontSize = Math.Max(12, resultFontSize - 1),
						VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
						Foreground = new WinUISolidColorBrush(WinUIColors.DimGray)
					});
				}
			}
		}

		if (row.Children.Count > 0)
		{
			panel.Children.Add(row);
		}
	}

	private static void AddDictionarySourceLink(WinUIStackPanel panel, DictionaryResult dictionary, double resultFontSize)
	{
		if (string.IsNullOrWhiteSpace(dictionary.SourceUrl))
		{
			return;
		}

		var link = new WinUIHyperlinkButton
		{
			Content = "必应词典网页",
			FontSize = Math.Max(12, resultFontSize - 3),
			Padding = new WinUIThickness(0),
			HorizontalAlignment = WinUIHorizontalAlignment.Left
		};
		link.Click += async (_, _) =>
		{
			if (Uri.TryCreate(dictionary.SourceUrl, UriKind.Absolute, out var sourceUri))
			{
				await Launcher.Default.OpenAsync(sourceUri);
			}
		};
		panel.Children.Add(link);
	}

	private void CloseUnpinnedSessions()
	{
		foreach (var session in _sessions.Where(item => !item.State.IsPinned).ToArray())
		{
			session.Window.Close();
		}
	}

	private async Task AutoCloseAsync(PopupSession session, int autoHideSeconds)
	{
		await Task.Delay(TimeSpan.FromSeconds(autoHideSeconds));
		session.Window.DispatcherQueue.TryEnqueue(() =>
		{
			if (!session.State.IsPinned)
			{
				session.Window.Close();
			}
		});
	}

	private static void EnableCloseOnDeactivate(PopupSession session)
	{
		_ = Task.Run(async () =>
		{
			await Task.Delay(450);
			session.Window.DispatcherQueue.TryEnqueue(() => session.State.CanCloseOnDeactivate = true);
		});
	}

	private void PrepareWindowBeforeShow(WinUIWindow window)
	{
		var hwnd = WindowNative.GetWindowHandle(window);
		if (hwnd == 0)
		{
			return;
		}

		var style = GetWindowLongPtrNative(hwnd, GwlExStyle).ToInt64();
		SetWindowLongPtrNative(hwnd, GwlExStyle, new nint((style | WsExToolWindow) & ~WsExAppWindow));
		var size = LoadWindowSize();
		var location = GetPopupLocation(size.Width, size.Height);
		SetWindowPos(hwnd, HwndTopmost, location.X, location.Y, size.Width, size.Height, SwpNoActivate | SwpFrameChanged);
	}

	private static void PromoteWindow(WinUIWindow window)
	{
		var hwnd = WindowNative.GetWindowHandle(window);
		if (hwnd == 0)
		{
			return;
		}

		ShowWindow(hwnd, SwShownormal);
		SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
		BringWindowToTop(hwnd);
		SetForegroundWindow(hwnd);
		SetFocus(hwnd);
	}

	private PopupSize LoadWindowSize()
	{
		try
		{
			if (!File.Exists(_statePath))
			{
				return new PopupSize(DefaultWidth, DefaultHeight);
			}

			var size = JsonSerializer.Deserialize<PopupSize>(File.ReadAllText(_statePath));
			if (size is null)
			{
				return new PopupSize(DefaultWidth, DefaultHeight);
			}

			return new PopupSize(
				Math.Clamp(size.Width, MinWidth, MaxWidth),
				Math.Clamp(size.Height, MinHeight, MaxHeight));
		}
		catch
		{
			return new PopupSize(DefaultWidth, DefaultHeight);
		}
	}

	private void SaveWindowSize(WinUIWindow window)
	{
		try
		{
			var hwnd = WindowNative.GetWindowHandle(window);
			if (hwnd == 0 || !GetWindowRect(hwnd, out var rect))
			{
				return;
			}

			var size = new PopupSize(
				Math.Clamp(rect.Right - rect.Left, MinWidth, MaxWidth),
				Math.Clamp(rect.Bottom - rect.Top, MinHeight, MaxHeight));
			Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
			File.WriteAllText(_statePath, JsonSerializer.Serialize(size));
		}
		catch
		{
			// Size persistence is best-effort; popup display must not fail because of it.
		}
	}

	private static PointNative GetPopupLocation(int width, int height)
	{
		if (!GetCursorPos(out var cursor))
		{
			return new PointNative { X = 160, Y = 120 };
		}

		var x = cursor.X + 18;
		var y = cursor.Y + 18;
		var screenWidth = GetSystemMetrics(SystemMetricScreenWidth);
		var screenHeight = GetSystemMetrics(SystemMetricScreenHeight);
		if (x + width > screenWidth - 12)
		{
			x = Math.Max(12, cursor.X - width - 18);
		}

		if (y + height > screenHeight - 12)
		{
			y = Math.Max(12, cursor.Y - height - 18);
		}

		return new PointNative { X = x, Y = y };
	}

	private sealed record PopupSession(WinUIWindow Window, PopupState State) : IResultPopupSession;

	private sealed class PopupState
	{
		public bool IsPinned { get; set; }
		public bool CanCloseOnDeactivate { get; set; }
	}

	private sealed record PopupSize(int Width, int Height);

	private const int SystemMetricScreenWidth = 0;
	private const int SystemMetricScreenHeight = 1;

	[StructLayout(LayoutKind.Sequential)]
	private struct PointNative
	{
		public int X;
		public int Y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RectNative
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out PointNative point);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int index);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hwnd, out RectNative rect);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hwnd);

	[DllImport("user32.dll")]
	private static extern bool BringWindowToTop(nint hwnd);

	[DllImport("user32.dll")]
	private static extern nint SetFocus(nint hwnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hwnd, int command);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern nint GetWindowLongPtrNative(nint hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern nint SetWindowLongPtrNative(nint hwnd, int index, nint newLong);
}
