using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SignMyApp.App.Controls;

/// <summary>Native DWM owns the shadow and rounding. Windows 10 gets a square WPF edge.</summary>
internal static class WindowFrame
{
    internal const int CornerPreference = 33;
    internal const int BorderColor = 34;
    internal const uint NoBorder = 0xFFFFFFFE;

    internal static Color ChooseBorderColor(bool active, Color activeColor, Color inactiveColor) =>
        active ? activeColor : inactiveColor;

    // DWM accepts COLORREF (0x00BBGGRR), not WPF's ARGB.
    internal static uint ToColorRef(Color color) => (uint)(color.R | color.G << 8 | color.B << 16);

    internal static uint NativeBorderColor(bool active, bool maximized, Color activeColor, Color inactiveColor) =>
        maximized ? NoBorder : ToColorRef(ChooseBorderColor(active, activeColor, inactiveColor));

    internal static void Apply(Border fallback, bool active, bool maximized, DpiScale dpi,
        Func<int, uint, int> setAttribute)
    {
        var activeColor = (Color)fallback.FindResource("Window.BorderActiveColor");
        var inactiveColor = (Color)fallback.FindResource("Window.BorderInactiveColor");
        // Unsupported attributes return a failing HRESULT; never gate on the OS version string.
        var supported = setAttribute(BorderColor, NativeBorderColor(active, maximized, activeColor, inactiveColor)) >= 0;
        _ = setAttribute(CornerPreference, maximized || !supported ? 1u : 2u); // DONOTROUND / ROUND
        fallback.BorderBrush = new SolidColorBrush(ChooseBorderColor(active, activeColor, inactiveColor));
        var width = (double)fallback.FindResource("Window.BorderWidth");
        fallback.BorderThickness = new Thickness(width / dpi.DpiScaleX, width / dpi.DpiScaleY,
            width / dpi.DpiScaleX, width / dpi.DpiScaleY);
        fallback.CornerRadius = new CornerRadius(0);
        fallback.Visibility = !supported && !maximized ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static int SetAttribute(nint hwnd, int attribute, uint value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(uint));

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);
}
