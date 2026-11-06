using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.Windows.Shapes.Path;
using System.Windows.Shell;
using SignMyApp.App.Controls;
using SignMyApp.App.Views;

namespace SignMyApp.App.Tests;

public class TitleBarTests
{
    [Fact]
    public void Chrome_preserves_native_window_behaviour_and_insets_maximized_content() => WpfHost.Run(() =>
    {
        var window = new MainWindow(); // No Show, EnsureHandle, or HWND creation.
        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.Equal(32, chrome.CaptionHeight);
        Assert.Equal(new Thickness(6), chrome.ResizeBorderThickness);
        Assert.Equal(new Thickness(0, 0, 0, 1), chrome.GlassFrameThickness);
        Assert.False(chrome.UseAeroCaptionButtons);
        Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
        Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
        Assert.NotNull(window.Icon);
        var content = (Grid)window.Content;
        var caption = (TitleBar)window.FindName("Caption");
        Assert.Equal(new Thickness(0), content.Margin);
        window.WindowState = WindowState.Maximized;
        WpfHost.Pump();
        Assert.Equal(SystemParameters.WindowResizeBorderThickness, content.Margin);
        Assert.True(caption.IsMaximized);
        window.WindowState = WindowState.Normal;
        WpfHost.Pump();
        Assert.Equal(new Thickness(0), content.Margin);
        Assert.False(caption.IsMaximized);
        Assert.Equal(0, new System.Windows.Interop.WindowInteropHelper(window).Handle);
    });

    [Fact]
    public void Caption_names_sizes_glyphs_and_tab_order_match_the_design() => WpfHost.Run(() =>
    {
        var bar = new TitleBar();
        WpfHost.Layout(bar, 1024, 32);
        foreach (var name in new[] { "Minimize", "Maximize", "Close" })
        {
            var button = (CaptionButton)bar.FindName(name + "Button");
            Assert.Equal(name, AutomationProperties.GetName(button));
            Assert.False(button.IsTabStop);
            Assert.False(button.Focusable);
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(button));
            Assert.Equal(46, button.ActualWidth);
            Assert.Equal(32, button.ActualHeight);
            var glyph = Assert.IsType<Path>(button.Content);
            Assert.Equal(10, glyph.Width);
            Assert.Equal(10, glyph.Height);
            Assert.Equal(1, glyph.StrokeThickness);
        }
        var image = (Image)bar.FindName("AppIcon");
        Assert.Equal(16, image.ActualHeight);
        Assert.Equal(8, image.TranslatePoint(new Point(), bar).X);
        Assert.Equal(12, ((TextBlock)bar.FindName("CaptionTitle")).FontSize);
        var normal = bar.MaximizeGlyph.Data;
        bar.IsMaximized = true;
        WpfHost.Pump();
        Assert.Equal("Restore", AutomationProperties.GetName(bar.MaximizeButton));
        Assert.NotEqual(normal.ToString(), bar.MaximizeGlyph.Data.ToString());
        bar.IsMaximized = false;
        WpfHost.Pump();
        Assert.Equal("Maximize", AutomationProperties.GetName(bar.MaximizeButton));
        Assert.Equal(normal.ToString(), bar.MaximizeGlyph.Data.ToString());
    });

    [Theory]
    [InlineData("Minimize", "#E3E3DE", "#D3D3CD", "#111112")]
    [InlineData("Maximize", "#E3E3DE", "#D3D3CD", "#111112")]
    [InlineData("Close", "#C42B1C", "#C84031", "#FFFFFF")]
    public void Caption_triggers_render_the_design_states(string name, string hover, string pressed, string foreground) => WpfHost.Run(() =>
    {
        var bar = new TitleBar();
        WpfHost.Layout(bar, 800, 32);
        var button = (CaptionButton)bar.FindName(name + "Button");
        AssertColor("#111112", button.Foreground);
        AssertColor("#00FFFFFF", button.Background);
        // The actual NC input path drives these same triggers (no mouse or native window needed).
        button.IsNonClientHover = true;
        WpfHost.Pump();
        AssertColor(hover, button.Background);
        AssertColor(foreground, button.Foreground);
        button.IsNonClientPressed = true;
        WpfHost.Pump();
        AssertColor(pressed, button.Background);
        AssertColor(foreground, button.Foreground);
        button.IsNonClientPressed = false;
        button.IsNonClientHover = false;
        bar.IsWindowActive = false;
        WpfHost.Pump();
        AssertColor("#A3A39D", button.Foreground);
        AssertColor("#68686C", ((TextBlock)bar.FindName("CaptionTitle")).Foreground);
        bar.IsWindowActive = true;
        WpfHost.Pump();
        AssertColor("#111112", button.Foreground);
    });

    static void AssertColor(string expected, Brush actual) =>
        Assert.Equal((Color)ColorConverter.ConvertFromString(expected), Assert.IsType<SolidColorBrush>(actual).Color);

    [Theory]
    [InlineData(100, 50, true, 9)]
    [InlineData(145.9, 81.9, true, 9)]
    [InlineData(146, 60, true, 0)]
    [InlineData(110, 82, true, 0)]
    [InlineData(99, 60, true, 0)]
    [InlineData(110, 49, true, 0)]
    [InlineData(110, 60, false, 0)]
    public void Hit_test_maps_only_the_enabled_maximize_rectangle(double x, double y, bool enabled, int expected) =>
        Assert.Equal(expected, CaptionHitTest.Map(new Point(x, y), new Rect(100, 50, 46, 32), enabled));

    [Theory]
    [InlineData(-1920, -100, 1)]
    [InlineData(-1920, -100, 1.5)]
    [InlineData(2000, 200, 2)]
    public void Screen_mapping_handles_negative_monitor_coordinates_and_scaled_bounds(int x, int y, double dpiScale)
    {
        var encoded = (nint)((uint)(ushort)x | ((uint)(ushort)y << 16));
        var point = CaptionHitTest.ScreenPoint(encoded);
        Assert.Equal(new Point(x, y), point);
        Assert.Equal(9, CaptionHitTest.Map(point, new Rect(x, y, 46 * dpiScale, 32 * dpiScale), true));
        Assert.Equal(0, CaptionHitTest.Map(point, Rect.Empty, true));
        Assert.Equal(0, CaptionHitTest.Map(point, new Rect(x, y, 0, 32), true));
    }

    [Fact]
    public void Embedded_icon_has_all_five_png_frames() => WpfHost.Run(() =>
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/SignMyApp;component/Assets/klarseal.ico"))!;
        using var stream = resource.Stream;
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(5, reader.ReadUInt16());
        var entries = new List<(int Size, uint Length, uint Offset)>();
        foreach (var expected in new[] { 16, 24, 32, 48, 256 })
        {
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            Assert.Equal(expected, width == 0 ? 256 : width);
            Assert.Equal(expected, height == 0 ? 256 : height);
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            entries.Add((expected, reader.ReadUInt32(), reader.ReadUInt32()));
        }
        foreach (var (size, length, offset) in entries)
        {
            stream.Position = offset;
            using var png = new MemoryStream(reader.ReadBytes((int)length));
            var frame = new PngBitmapDecoder(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Single();
            Assert.Equal(size, frame.PixelWidth);
            Assert.Equal(size, frame.PixelHeight);
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[size * size * 4];
            bgra.CopyPixels(pixels, size * 4, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels.Take(4)); // square black plate
            Assert.Contains(Enumerable.Range(0, size * size), p => pixels[p * 4] > 240); // white mark
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sign_header_keeps_the_vector_mark_in_review_and_errors(bool error) => WpfHost.RunAsync(async () =>
    {
        var scenario = new Scenario();
        var vm = await scenario.ReviewAsync(withMsi: false);
        if (error)
        {
            scenario.Demo.Signer.FailOnce("MyApp.exe", SignMyApp.Core.SignOutcome.SessionClosed);
            await vm.SignOnceCommand.RunAsync();
        }
        var view = new MainView { DataContext = scenario.Vm };
        WpfHost.Layout(view, 1024, 608);
        var mark = (Path)view.SignDialogPart.FindName("KlarsealMark");
        Assert.Equal(17, mark.Height);
        Assert.InRange(mark.ActualHeight, 16.99, 17.01); // The original arcs have slightly non-integral bounds.
        Assert.Equal(Visibility.Visible, mark.Visibility);
        AssertColor("#111112", mark.Fill);
        Assert.Equal(((Geometry)Application.Current.FindResource("Geo.KlarsealMark")).ToString(), mark.Data.ToString());
    });
}
