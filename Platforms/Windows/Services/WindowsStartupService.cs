using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsStartupService : IStartupService
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "PotLiteMaui";
	private const string ShortcutFileName = "PotLiteMaui.lnk";

	public bool IsSupported => true;

	public bool IsEnabled()
	{
		return File.Exists(GetShortcutPath()) || HasLegacyRunValue();
	}

	public void SetEnabled(bool enabled)
	{
		if (enabled)
		{
			CreateStartupShortcut();
		}
		else
		{
			DeleteStartupShortcut();
		}

		RemoveLegacyRunValue();
	}

	private static bool HasLegacyRunValue()
	{
		using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
		return key?.GetValue(ValueName) is string;
	}

	private static void RemoveLegacyRunValue()
	{
		using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
		key?.DeleteValue(ValueName, throwOnMissingValue: false);
	}

	private static void CreateStartupShortcut()
	{
		var executablePath = GetExecutablePath();
		var shortcutPath = GetShortcutPath();
		Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

		var shellLink = (IShellLinkW)(object)new ShellLink();
		try
		{
			shellLink.SetPath(executablePath);
			shellLink.SetWorkingDirectory(Path.GetDirectoryName(executablePath)!);
			shellLink.SetDescription("PotLiteMaui");
			((IPersistFile)shellLink).Save(shortcutPath, true);
		}
		finally
		{
			Marshal.FinalReleaseComObject(shellLink);
		}
	}

	private static void DeleteStartupShortcut()
	{
		var shortcutPath = GetShortcutPath();
		if (File.Exists(shortcutPath))
		{
			File.Delete(shortcutPath);
		}
	}

	private static string GetShortcutPath()
	{
		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.Startup),
			ShortcutFileName);
	}

	private static string GetExecutablePath()
	{
		var path = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(path))
		{
			path = Process.GetCurrentProcess().MainModule?.FileName;
		}

		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			throw new InvalidOperationException("无法确定当前程序路径，无法设置开机启动");
		}

		return path;
	}

	[ComImport]
	[Guid("00021401-0000-0000-C000-000000000046")]
	private sealed class ShellLink
	{
	}

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("000214F9-0000-0000-C000-000000000046")]
	private interface IShellLinkW
	{
		void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, nint findData, uint flags);
		void GetIDList(out nint idList);
		void SetIDList(nint idList);
		void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
		void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
		void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
		void GetHotkey(out short hotkey);
		void SetHotkey(short hotkey);
		void GetShowCmd(out int showCommand);
		void SetShowCmd(int showCommand);
		void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathSize, out int iconIndex);
		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
		void Resolve(nint hwnd, uint flags);
		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
	}
}
