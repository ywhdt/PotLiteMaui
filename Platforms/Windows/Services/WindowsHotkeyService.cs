using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;
using PotLiteMaui.Services.Platform;
using WinRT.Interop;
using MauiApplication = Microsoft.Maui.Controls.Application;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsHotkeyService : IHotkeyService, IDisposable
{
	private const int HotkeyId = 0x504F54;
	private const uint ModAlt = 0x0001;
	private const uint ModControl = 0x0002;
	private const uint ModShift = 0x0004;
	private const uint ModWin = 0x0008;
	private const uint ModNoRepeat = 0x4000;
	private const uint WmHotkey = 0x0312;

	private readonly SubclassProc _subclassProc;
	private nint _hwnd;
	private bool _subclassInstalled;

	public WindowsHotkeyService()
	{
		_subclassProc = WindowSubclassProc;
	}

	public event EventHandler? HotkeyPressed;
	public bool IsRegistered { get; private set; }
	public string HotkeyText { get; private set; } = "Ctrl+Alt+T";

	public bool Register(string hotkey)
	{
		Unregister();
		HotkeyText = string.IsNullOrWhiteSpace(hotkey) ? "Ctrl+Alt+T" : hotkey.Trim();
		if (!TryParseHotkey(HotkeyText, out var modifiers, out var virtualKey))
		{
			return false;
		}

		_hwnd = ResolveWindowHandle();
		if (_hwnd == 0)
		{
			return false;
		}

		if (!_subclassInstalled)
		{
			_subclassInstalled = SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero);
			if (!_subclassInstalled)
			{
				return false;
			}
		}

		IsRegistered = RegisterHotKey(_hwnd, HotkeyId, modifiers | ModNoRepeat, virtualKey);
		return IsRegistered;
	}

	public void Unregister()
	{
		if (IsRegistered && _hwnd != 0)
		{
			UnregisterHotKey(_hwnd, HotkeyId);
			IsRegistered = false;
		}
	}

	public void Dispose()
	{
		Unregister();
		if (_subclassInstalled && _hwnd != 0)
		{
			RemoveWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero);
			_subclassInstalled = false;
		}
	}

	private nint WindowSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint refData)
	{
		if (message == WmHotkey && wParam == HotkeyId)
		{
			MainThread.BeginInvokeOnMainThread(() => HotkeyPressed?.Invoke(this, EventArgs.Empty));
			return 0;
		}

		return DefSubclassProc(hwnd, message, wParam, lParam);
	}

	private static bool TryParseHotkey(string value, out uint modifiers, out uint virtualKey)
	{
		modifiers = 0;
		virtualKey = 0;
		foreach (var rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var part = rawPart.ToUpperInvariant();
			switch (part)
			{
				case "CTRL":
				case "CONTROL":
					modifiers |= ModControl;
					break;
				case "ALT":
				case "OPTION":
					modifiers |= ModAlt;
					break;
				case "SHIFT":
					modifiers |= ModShift;
					break;
				case "WIN":
				case "CMD":
				case "COMMAND":
					modifiers |= ModWin;
					break;
				default:
					virtualKey = ParseVirtualKey(part);
					break;
			}
		}

		return modifiers != 0 && virtualKey != 0;
	}

	private static uint ParseVirtualKey(string key)
	{
		if (key.Length == 1)
		{
			var ch = key[0];
			if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
			{
				return ch;
			}
		}

		if (key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
		{
			return (uint)(0x70 + functionKey - 1);
		}

		return key switch
		{
			"SPACE" => 0x20,
			"ENTER" => 0x0D,
			"ESC" or "ESCAPE" => 0x1B,
			_ => 0
		};
	}

	private static nint ResolveWindowHandle()
	{
		var mauiWindow = MauiApplication.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as WinUIWindow;
		return mauiWindow is null ? 0 : WindowNative.GetWindowHandle(mauiWindow);
	}

	private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint refData);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

	[DllImport("comctl32.dll", SetLastError = true)]
	private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

	[DllImport("comctl32.dll", SetLastError = true)]
	private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

	[DllImport("comctl32.dll")]
	private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam);
}
