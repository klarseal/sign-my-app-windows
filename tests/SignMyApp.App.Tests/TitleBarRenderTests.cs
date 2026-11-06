using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SignMyApp.App.Services;
using SignMyApp.App.Controls;
using SignMyApp.App.Views;

namespace SignMyApp.App.Tests;

public class TitleBarRenderTests
{
    [Fact]
    public void Board_4_6_window_edge_and_app_icon() => WpfHost.Run(() =>
    {
        // Canvas v46 TitleBar.dc.html: reproduce its approved two previews and icon sizes.
        // These are off-screen illustrations of DWM states, not a claim to have exercised DWM.
        var section = new StackPanel { Margin = new Thickness(40, 48, 40, 0) };
        section.Children.Add(Label("WINDOW EDGE ON A WHITE DESKTOP · NATIVE SHADOW, 1 PX BORDER, 8 PX CORNERS, GREY CHROME BAND"));
        var desktop = new Grid { Margin = new Thickness(40) };
        desktop.ColumnDefinitions.Add(new ColumnDefinition());
        desktop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        desktop.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var active in new[] { true, false })
        {
            var column = new StackPanel();
            Grid.SetColumn(column, active ? 0 : 2);
            var preview = new StackPanel();
            preview.Children.Add(new TitleBar { IsWindowActive = active });
            var header = new DockPanel { Height = 56, Margin = new Thickness(20, 0, 16, 0) };
            var pill = new Button
            {
                Content = "Example Software Ltd", Height = 36, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Style = (Style)Application.Current.FindResource("Btn.White"), VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(pill, Dock.Right);
            header.Children.Add(pill);
            header.Children.Add(new TextBlock { Text = "MyApp", FontSize = 15.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            preview.Children.Add(new Border
            {
                Background = (Brush)Application.Current.FindResource("Chrome.Background"),
                BorderBrush = (Brush)Application.Current.FindResource("Chrome.Baseline"),
                BorderThickness = (Thickness)Application.Current.FindResource("Chrome.BaselineThickness"), Child = header,
            });
            for (int i = 0; i < 4; i++)
                preview.Children.Add(new ListBoxItem
                {
                    Height = 44, MinHeight = 44, IsSelected = i == 0,
                    Style = (Style)Application.Current.FindResource("FileRow"),
                });
            var color = (Color)Application.Current.FindResource(active ? "Window.BorderActiveColor" : "Window.BorderInactiveColor");
            var frame = new Border
            {
                BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Background = Brushes.White, Child = preview,
                Effect = new DropShadowEffect { Color = Color.FromRgb(17, 17, 18), BlurRadius = active ? 32 : 16, ShadowDepth = active ? 8 : 4, Direction = 270, Opacity = active ? .18 : .10 },
            };
            // Clip the preview to the approved corners; the real app uses native DWM clipping.
            frame.SizeChanged += (_, _) => preview.Clip = new RectangleGeometry(new Rect(0, 0, preview.ActualWidth, preview.ActualHeight), 7, 7);
            column.Children.Add(frame);
            column.Children.Add(new TextBlock { Text = active ? "Active" : "Inactive", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 14) });
            column.Children.Add(new TextBlock
            {
                Text = active ? "Native DWM shadow and rounded corners. Border grey-300 #d3d3cd." : "Windows shrinks the shadow. Border grey-200 #e3e3de, title #68686c, glyphs #a3a39d.",
                FontSize = 12, Foreground = Brush("#5C5C5E"), TextWrapping = TextWrapping.Wrap,
            });
            desktop.Children.Add(column);
        }
        var desktopFrame = new Grid();
        desktopFrame.Children.Add(new System.Windows.Shapes.Rectangle { Stroke = Brush("#D3D3CD"), StrokeThickness = 1, StrokeDashArray = [3, 3], RadiusX = 6, RadiusY = 6 });
        desktopFrame.Children.Add(desktop);
        section.Children.Add(desktopFrame);
        section.Children.Add(Label("APP ICON · .EXE, TASKBAR, TITLE BAR · KLARSEAL-MARK-SQUARE-WHITE-ON-BLACK.SVG", new Thickness(0, 40, 0, 14)));
        var icons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var frame in AppIdentity.LoadWindowIcon().Decoder.Frames)
        {
            var icon = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 0) };
            icon.Children.Add(new Image { Source = frame, Width = frame.PixelWidth, Height = frame.PixelHeight });
            icon.Children.Add(new TextBlock { Text = frame.PixelWidth.ToString(), FontSize = 12, Foreground = Brush("#68686C"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
            icons.Children.Add(icon);
        }
        section.Children.Add(icons);
        section.Children.Add(new TextBlock { Text = "One .ico: 16, 24, 32, 48, 256 px. Sign window header: the ink mark, no plate, 17 px tall. App header: no mark.", FontSize = 13, Foreground = Brush("#5C5C5E"), Margin = new Thickness(0, 14, 0, 0) });
        var root = new Border { Background = Brushes.White, Child = section };
        System.Windows.Documents.TextElement.SetFontFamily(root, (FontFamily)Application.Current.FindResource("Font.Sans"));
        WpfHost.Render(root, 1280, 900, "4.6-window-edge-and-app-icon");
    });

    static TextBlock Label(string text, Thickness? margin = null) => new()
    {
        Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("#68686C"),
        Margin = margin ?? new Thickness(0, 0, 0, 14),
    };

    [Fact]
    public void Board_4_6_title_bar_states() => WpfHost.Run(() =>
    {
        // Approved TitleBar.dc.html, first section (the six caption states), rendered with production controls.
        var section = new StackPanel { Margin = new Thickness(40, 48, 40, 0) };
        section.Children.Add(new TextBlock
        {
            Text = "THE APP’S OWN TITLE BAR · 32 PX · CAPTION BUTTONS 46 × 32", FontSize = 10,
            FontWeight = FontWeights.SemiBold, Foreground = Brush("#68686C"), Margin = new Thickness(0, 0, 0, 14),
        });
        foreach (var (label, buttonName, pressed, active) in new[]
        {
            ("Active", "", false, true),
            ("Hover · Minimise", "Minimize", false, true),
            ("Pressed · Minimise", "Minimize", true, true),
            ("Hover · Close", "Close", false, true),
            ("Pressed · Close", "Close", true, true),
            ("Inactive window", "", false, false),
        })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var bar = new TitleBar { IsWindowActive = active };
            if (buttonName.Length > 0)
            {
                var button = (CaptionButton)bar.FindName(buttonName + "Button");
                button.IsNonClientHover = true;
                button.IsNonClientPressed = pressed;
            }
            var frame = new Border { BorderBrush = Brush(active ? "#D3D3CD" : "#E3E3DE"), BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(8, 8, 0, 0), Child = bar };
            Grid.SetColumn(frame, 1);
            row.Children.Add(frame);
            section.Children.Add(row);
        }
        section.Children.Add(new TextBlock
        {
            Text = "Maximised: the maximise glyph becomes Restore (two offset squares). Snap layouts on hovering Maximise, as Windows does.",
            FontSize = 13, Foreground = Brush("#5C5C5E"),
        });
        var root = new Border { Background = Brushes.White, Child = section };
        System.Windows.Documents.TextElement.SetFontFamily(root, (FontFamily)Application.Current.FindResource("Font.Sans"));
        WpfHost.Render(root, 1280, 400, "4.6-title-bar-states");
    });

    [Fact]
    public void Board_4_6_maximize_and_restore_states() => WpfHost.Run(() =>
    {
        var rows = new StackPanel { Margin = new Thickness(40, 48, 40, 0) };
        foreach (var maximized in new[] { false, true })
        foreach (var state in new[] { "Active", "Hover", "Pressed" })
        {
            rows.Children.Add(new TextBlock { Text = $"{(maximized ? "Restore" : "Maximize")} · {state}", FontSize = 13, Margin = new Thickness(0, 0, 0, 12) });
            var bar = new TitleBar { IsMaximized = maximized, Margin = new Thickness(0, 0, 0, 12) };
            bar.MaximizeButton.IsNonClientHover = state != "Active";
            bar.MaximizeButton.IsNonClientPressed = state == "Pressed";
            rows.Children.Add(bar);
        }
        WpfHost.Render(new Border { Background = Brushes.White, Child = rows }, 1280, 520, "4.6-maximize-restore-states");
    });

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
