using System.Runtime.InteropServices;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsPopupPlacementService : IPopupPlacementService
{
	public Point GetPreferredPopupPosition(double width, double height)
	{
		return GetCursorPos(out var point)
			? new Point(point.X + 18, point.Y + 18)
			: new Point(160, 120);
	}

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out PointNative point);

	private struct PointNative
	{
		public int X;
		public int Y;
	}
}
