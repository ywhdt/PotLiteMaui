using System.Runtime.InteropServices;
using PotLiteMaui.Models;
using PotLiteMaui.Services.Platform;
using WinRT.Interop;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiWindow = Microsoft.Maui.Controls.Window;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsTrayService : ITrayService, IDisposable
{
	private const uint WmClose = 0x0010;
	private const uint WmTray = 0x8001;
	private const uint WmLButtonDoubleClick = 0x0203;
	private const uint WmRButtonUp = 0x0205;
	private const int SwHide = 0;
	private const int SwShow = 5;
	private const uint NifMessage = 0x00000001;
	private const uint NifIcon = 0x00000002;
	private const uint NifTip = 0x00000004;
	private const uint NimAdd = 0x00000000;
	private const uint NimModify = 0x00000001;
	private const uint NimDelete = 0x00000002;
	private const uint IdiApplication = 32512;
	private const uint ImageIcon = 1;
	private const uint LrLoadFromFile = 0x00000010;
	private const int SmCxSmIcon = 49;
	private const int SmCySmIcon = 50;
	private const uint MfString = 0x00000000;
	private const uint TpmReturnCmd = 0x0100;
	private const uint TpmNonotify = 0x0080;
	private const uint CommandOpen = 1;
	private const uint CommandExit = 2;

	private readonly SubclassProc _subclassProc;
	private Func<AppSettings>? _settingsProvider;
	private nint _hwnd;
	private nint _icon;
	private bool _ownsIcon;
	private bool _subclassInstalled;
	private bool _trayAdded;
	private bool _allowClose;

	public WindowsTrayService()
	{
		_subclassProc = WindowSubclassProc;
	}

	public bool IsSupported => true;

	public void Initialize(MauiWindow mainWindow, Func<AppSettings> settingsProvider)
	{
		_settingsProvider = settingsProvider;
		var platformWindow = mainWindow.Handler?.PlatformView as WinUIWindow;
		if (platformWindow is null)
		{
			return;
		}

		_hwnd = WindowNative.GetWindowHandle(platformWindow);
		if (_hwnd == 0)
		{
			return;
		}

		if (!_subclassInstalled)
		{
			_subclassInstalled = SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero);
		}

		_icon = LoadTrayIcon();
		AddOrUpdateTrayIcon("划词翻译");
	}

	public void SetListening(bool isListening)
	{
		if (_hwnd != 0)
		{
			AddOrUpdateTrayIcon(isListening ? "划词翻译：监听中" : "划词翻译：已暂停");
		}
	}

	public void Dispose()
	{
		if (_trayAdded)
		{
			var data = CreateNotifyIconData("划词翻译");
			Shell_NotifyIcon(NimDelete, ref data);
			_trayAdded = false;
		}

		if (_subclassInstalled && _hwnd != 0)
		{
			RemoveWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero);
			_subclassInstalled = false;
		}

		if (_ownsIcon && _icon != 0)
		{
			DestroyIcon(_icon);
			_icon = 0;
			_ownsIcon = false;
		}
	}

	private nint LoadTrayIcon()
	{
		var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
		if (File.Exists(iconPath))
		{
			var icon = LoadImage(
				0,
				iconPath,
				ImageIcon,
				GetSystemMetrics(SmCxSmIcon),
				GetSystemMetrics(SmCySmIcon),
				LrLoadFromFile);
			if (icon != 0)
			{
				_ownsIcon = true;
				return icon;
			}
		}

		_ownsIcon = false;
		return LoadIcon(0, IdiApplication);
	}

	private void AddOrUpdateTrayIcon(string tip)
	{
		var data = CreateNotifyIconData(tip);
		if (_trayAdded)
		{
			Shell_NotifyIcon(NimModify, ref data);
		}
		else
		{
			_trayAdded = Shell_NotifyIcon(NimAdd, ref data);
		}
	}

	private NotifyIconData CreateNotifyIconData(string tip)
	{
		return new NotifyIconData
		{
			Size = Marshal.SizeOf<NotifyIconData>(),
			WindowHandle = _hwnd,
			Id = 1,
			Flags = NifMessage | NifIcon | NifTip,
			CallbackMessage = WmTray,
			Icon = _icon,
			Tip = tip.Length > 127 ? tip[..127] : tip
		};
	}

	private nint WindowSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint refData)
	{
		if (message == WmClose && !_allowClose && (_settingsProvider?.Invoke().RunInTray ?? false))
		{
			ShowWindow(hwnd, SwHide);
			return 0;
		}

		if (message == WmTray)
		{
			var mouseMessage = unchecked((uint)lParam);
			if (mouseMessage == WmLButtonDoubleClick)
			{
				ShowMainWindow();
				return 0;
			}

			if (mouseMessage == WmRButtonUp)
			{
				ShowContextMenu(hwnd);
				return 0;
			}
		}

		return DefSubclassProc(hwnd, message, wParam, lParam);
	}

	private void ShowContextMenu(nint hwnd)
	{
		var menu = CreatePopupMenu();
		AppendMenu(menu, MfString, CommandOpen, "打开");
		AppendMenu(menu, MfString, CommandExit, "退出");
		GetCursorPos(out var cursor);
		SetForegroundWindow(hwnd);
		var command = TrackPopupMenu(menu, TpmReturnCmd | TpmNonotify, cursor.X, cursor.Y, 0, hwnd, 0);
		DestroyMenu(menu);

		if (command == CommandOpen)
		{
			ShowMainWindow();
		}
		else if (command == CommandExit)
		{
			ExitApplication();
		}
	}

	private void ShowMainWindow()
	{
		if (_hwnd == 0)
		{
			return;
		}

		ShowWindow(_hwnd, SwShow);
		SetForegroundWindow(_hwnd);
	}

	private void ExitApplication()
	{
		_allowClose = true;
		MainThread.BeginInvokeOnMainThread(() => MauiApplication.Current?.Quit());
	}

	private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint refData);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NotifyIconData
	{
		public int Size;
		public nint WindowHandle;
		public uint Id;
		public uint Flags;
		public uint CallbackMessage;
		public nint Icon;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Tip;
	}

	private struct PointNative
	{
		public int X;
		public int Y;
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

	[DllImport("user32.dll")]
	private static extern nint LoadIcon(nint instance, uint iconName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint load);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int index);

	[DllImport("user32.dll")]
	private static extern bool DestroyIcon(nint icon);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out PointNative point);

	[DllImport("user32.dll")]
	private static extern nint CreatePopupMenu();

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool AppendMenu(nint menu, uint flags, uint idNewItem, string newItem);

	[DllImport("user32.dll")]
	private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

	[DllImport("user32.dll")]
	private static extern bool DestroyMenu(nint menu);

	[DllImport("comctl32.dll", SetLastError = true)]
	private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

	[DllImport("comctl32.dll", SetLastError = true)]
	private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

	[DllImport("comctl32.dll")]
	private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam);
}
