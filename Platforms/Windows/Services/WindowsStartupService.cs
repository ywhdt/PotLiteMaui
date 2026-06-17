using Microsoft.Win32;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsStartupService : IStartupService
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "PotLiteMaui";

	public bool IsSupported => true;

	public bool IsEnabled()
	{
		using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
		return key?.GetValue(ValueName) is string;
	}

	public void SetEnabled(bool enabled)
	{
		using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ??
			Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
		if (enabled)
		{
			key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
		}
		else
		{
			key.DeleteValue(ValueName, throwOnMissingValue: false);
		}
	}
}
