using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsSelectionCaptureService : ITextSelectionService
{
	private const ushort VkControl = 0x11;
	private const ushort VkC = 0x43;
	private const uint InputKeyboard = 1;
	private const uint KeyEventKeyUp = 0x0002;

	public async Task<string?> CaptureSelectedTextAsync(int copyDelayMs, CancellationToken cancellationToken = default)
	{
		Exception? lastError = null;
		for (var attempt = 0; attempt < 2; attempt++)
		{
			try
			{
				var text = await CaptureWithClipboardAsync(copyDelayMs + attempt * 120, cancellationToken);
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
			catch (Exception ex)
			{
				lastError = ex;
			}
		}

		if (lastError is not null)
		{
			throw lastError;
		}

		return null;
	}

	private static async Task<string?> CaptureWithClipboardAsync(int copyDelayMs, CancellationToken cancellationToken)
	{
		var previousText = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync());
		var sentinel = $"__POT_LITE_EMPTY_{Guid.NewGuid():N}__";
		await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(sentinel));

		SendCopyShortcut();
		await Task.Delay(Math.Clamp(copyDelayMs, 100, 900), cancellationToken);

		var capturedText = await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.GetTextAsync());
		await RestoreClipboardAsync(previousText);

		if (string.IsNullOrWhiteSpace(capturedText) || capturedText == sentinel)
		{
			return null;
		}

		return capturedText;
	}

	private static async Task RestoreClipboardAsync(string? previousText)
	{
		await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(previousText ?? string.Empty));
	}

	private static void SendCopyShortcut()
	{
		var inputs = new[]
		{
			CreateKeyboardInput(VkControl, 0),
			CreateKeyboardInput(VkC, 0),
			CreateKeyboardInput(VkC, KeyEventKeyUp),
			CreateKeyboardInput(VkControl, KeyEventKeyUp)
		};
		var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
		if (sent != inputs.Length)
		{
			throw new InvalidOperationException("无法发送复制快捷键");
		}
	}

	private static Input CreateKeyboardInput(ushort keyCode, uint flags)
	{
		return new Input
		{
			Type = InputKeyboard,
			Data = new InputUnion
			{
				KeyboardInput = new KeyboardInput
				{
					VirtualKey = keyCode,
					Flags = flags
				}
			}
		};
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

	[StructLayout(LayoutKind.Sequential)]
	private struct Input
	{
		public uint Type;
		public InputUnion Data;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)]
		public MouseInput MouseInput;

		[FieldOffset(0)]
		public KeyboardInput KeyboardInput;

		[FieldOffset(0)]
		public HardwareInput HardwareInput;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MouseInput
	{
		public int X;
		public int Y;
		public uint MouseData;
		public uint Flags;
		public uint Time;
		public nint ExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KeyboardInput
	{
		public ushort VirtualKey;
		public ushort ScanCode;
		public uint Flags;
		public uint Time;
		public nint ExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct HardwareInput
	{
		public uint Message;
		public ushort LowParam;
		public ushort HighParam;
	}
}
