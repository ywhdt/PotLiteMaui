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
using WinUIColors = Microsoft.UI.Colors;
using WinUICornerRadius = Microsoft.UI.Xaml.CornerRadius;
using WinUIGrid = Microsoft.UI.Xaml.Controls.Grid;
using WinUIGridLength = Microsoft.UI.Xaml.GridLength;
using WinUIGridUnitType = Microsoft.UI.Xaml.GridUnitType;
using WinUIHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
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

	public void Show(TranslationBatchResult result, int autoHideSeconds)
	{
		MainThread.BeginInvokeOnMainThread(() => ShowCore(result, Math.Clamp(autoHideSeconds, 0, 60)));
	}

	private void ShowCore(TranslationBatchResult result, int autoHideSeconds)
	{
		CloseUnpinnedSessions();

		var state = new PopupState();
		var session = new PopupSession(new WinUIWindow { Title = "翻译结果" }, state);
		session.Window.Content = CreateContent(result, state, () => session.Window.Close());
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

		PrepareWindowBeforeShow(session.Window);
		session.Window.Activate();
		PromoteWindow(session.Window);
		EnableCloseOnDeactivate(session);

		if (autoHideSeconds > 0)
		{
			_ = AutoCloseAsync(session, autoHideSeconds);
		}
	}

	private static WinUIGrid CreateContent(TranslationBatchResult result, PopupState state, Action close)
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
		foreach (var item in result.DisplayItems)
		{
			resultStack.Children.Add(CreateResultSection(
				item.IsSuccess ? item.ProviderName : $"{item.ProviderName} 失败",
				item.Text,
				isError: !item.IsSuccess));
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

		var buttons = new WinUIStackPanel
		{
			Orientation = WinUIOrientation.Horizontal,
			HorizontalAlignment = WinUIHorizontalAlignment.Right,
			Spacing = 8
		};
		var copyButton = CreateActionButton("复制");
		copyButton.Click += async (_, _) => await Clipboard.Default.SetTextAsync(result.DisplayText);
		var pinButton = CreateActionButton("固定");
		pinButton.Click += (_, _) =>
		{
			state.IsPinned = !state.IsPinned;
			pinButton.Content = state.IsPinned ? "取消固定" : "固定";
		};
		var closeButton = CreateActionButton("关闭");
		closeButton.Click += (_, _) => close();
		buttons.Children.Add(copyButton);
		buttons.Children.Add(pinButton);
		buttons.Children.Add(closeButton);
		WinUIGrid.SetRow(buttons, 2);
		root.Children.Add(buttons);

		return root;
	}

	private static WinUIStackPanel CreateHeader(TranslationBatchResult result)
	{
		var header = new WinUIStackPanel { Spacing = 5 };
		header.Children.Add(new WinUITextBlock
		{
			Text = $"{result.ProviderName}  {result.CreatedAt:HH:mm:ss}",
			FontSize = 13,
			Foreground = new WinUISolidColorBrush(WinUIColors.Teal)
		});
		header.Children.Add(new WinUITextBlock
		{
			Text = result.SourceText,
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

	private static WinUIBorder CreateResultSection(string title, string text, bool isError)
	{
		var panel = new WinUIStackPanel { Spacing = 7 };
		panel.Children.Add(new WinUITextBlock
		{
			Text = title,
			FontSize = 13,
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			Foreground = new WinUISolidColorBrush(isError ? WinUIColors.Firebrick : WinUIColors.DarkSlateGray)
		});
		panel.Children.Add(new WinUITextBlock
		{
			Text = text,
			FontSize = 15,
			TextWrapping = TextWrapping.Wrap
		});

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

	private sealed record PopupSession(WinUIWindow Window, PopupState State);

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
