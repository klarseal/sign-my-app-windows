using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SignMyApp.App.Controls;

namespace SignMyApp.App.Tests;

public class WindowFrameTests
{
    [Theory]
    [InlineData(true, false, 0xCDD3D3u)]
    [InlineData(false, false, 0xDEE3E3u)]
    [InlineData(true, true, 0xFFFFFFFEu)]
    [InlineData(false, true, 0xFFFFFFFEu)]
    public void Native_colour_is_COLORREF_and_maximized_suppresses_the_border(bool active, bool maximized, uint expected) =>
        Assert.Equal(expected, WindowFrame.NativeBorderColor(active, maximized,
            Color.FromRgb(0xD3, 0xD3, 0xCD), Color.FromRgb(0xE3, 0xE3, 0xDE)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Colour_choice_uses_the_supplied_tokens(bool active)
    {
        var first = Color.FromRgb(1, 2, 3);
        var second = Color.FromRgb(4, 5, 6);
        Assert.Equal(active ? first : second, WindowFrame.ChooseBorderColor(active, first, second));
        Assert.Equal(0x030201u, WindowFrame.ToColorRef(first));
    }

    [Theory]
    [InlineData(true, true, false, "#D3D3CD", 2u)]
    [InlineData(true, false, false, "#E3E3DE", 2u)]
    [InlineData(false, true, false, "#D3D3CD", 1u)]
    [InlineData(false, false, false, "#E3E3DE", 1u)]
    [InlineData(true, true, true, "#D3D3CD", 1u)]
    [InlineData(false, false, true, "#E3E3DE", 1u)]
    public void Attribute_results_drive_the_square_fallback_and_maximize_state(
        bool supported, bool active, bool maximized, string color, uint corners) => WpfHost.Run(() =>
    {
        var window = new MainWindow();
        var fallback = (Border)window.FindName("FallbackWindowBorder");
        var calls = new List<(int Attribute, uint Value)>();
        WindowFrame.Apply(fallback, active, maximized, new DpiScale(1.5, 2), (attribute, value) =>
        {
            calls.Add((attribute, value));
            return supported ? 0 : unchecked((int)0x80070057); // E_INVALIDARG on Windows 10.
        });
        Assert.Equal(new[] { WindowFrame.BorderColor, WindowFrame.CornerPreference }, calls.Select(c => c.Attribute));
        Assert.Equal(corners, calls[1].Value);
        Assert.Equal(maximized ? WindowFrame.NoBorder : WindowFrame.ToColorRef((Color)ColorConverter.ConvertFromString(color)), calls[0].Value);
        Assert.Equal(!supported && !maximized ? Visibility.Visible : Visibility.Collapsed, fallback.Visibility);
        Assert.Equal(new CornerRadius(0), fallback.CornerRadius);
        Assert.Equal(new Thickness(1 / 1.5, .5, 1 / 1.5, .5), fallback.BorderThickness);
        Assert.Equal((Color)ColorConverter.ConvertFromString(color), ((SolidColorBrush)fallback.BorderBrush).Color);
        Assert.False(fallback.IsHitTestVisible);
        Assert.Equal(0, new System.Windows.Interop.WindowInteropHelper(window).Handle);
    });

    [Fact]
    public void Unsupported_border_tracks_activation_maximize_and_restore() => WpfHost.Run(() =>
    {
        var fallback = (Border)new MainWindow().FindName("FallbackWindowBorder");
        foreach (var (active, maximized) in new[] { (true, false), (false, false), (true, true), (false, true), (true, false) })
        {
            WindowFrame.Apply(fallback, active, maximized, new DpiScale(1, 1), (_, _) => -1);
            Assert.Equal(maximized ? Visibility.Collapsed : Visibility.Visible, fallback.Visibility);
            Assert.Equal((Color)Application.Current.FindResource(active ? "Window.BorderActiveColor" : "Window.BorderInactiveColor"),
                ((SolidColorBrush)fallback.BorderBrush).Color);
        }
    });
}
