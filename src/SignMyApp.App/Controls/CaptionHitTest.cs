using System.Windows;

namespace SignMyApp.App.Controls;

/// <summary>Screen pixel mapping, independent of HWNDs and DPI. Zero means defer to WindowChrome.</summary>
internal static class CaptionHitTest
{
    internal const int MaxButton = 9; // HTMAXBUTTON

    internal static Point ScreenPoint(nint lParam) => new(
        unchecked((short)(long)lParam), unchecked((short)((long)lParam >> 16)));

    internal static int Map(Point screenPoint, Rect maximizeBounds, bool canMaximize) =>
        canMaximize && maximizeBounds.Width > 0 && maximizeBounds.Height > 0 &&
        screenPoint.X >= maximizeBounds.Left && screenPoint.X < maximizeBounds.Right &&
        screenPoint.Y >= maximizeBounds.Top && screenPoint.Y < maximizeBounds.Bottom ? MaxButton : 0;
}
