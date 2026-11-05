using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SignMyApp.App.Controls;
using SignMyApp.App.Views;

namespace SignMyApp.App.Tests;

public class TitleBarRenderTests
{
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
            var frame = new Border { BorderBrush = Brush("#D3D3CD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8, 8, 0, 0), Child = bar };
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
