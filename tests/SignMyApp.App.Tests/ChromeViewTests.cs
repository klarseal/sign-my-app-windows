using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SignMyApp.App.Controls;
using SignMyApp.App.Views;

namespace SignMyApp.App.Tests;

public class ChromeViewTests
{
    [Theory]
    [InlineData(1280, 768)]
    [InlineData(1024, 608)]
    public void Header_is_one_grey_band_with_white_pills_and_one_bottom_line(int width, int height) => WpfHost.RunAsync(async () =>
    {
        var scenario = await new Scenario().ResultsAsync();
        var view = new MainView { DataContext = scenario.Vm };
        WpfHost.Layout(view, width, height);
        AssertColor("#FFFFFF", view.Background);
        AssertBand((Border)view.FindName("AppHeader"));
        foreach (var name in new[] { "CertificateButton", "AddFilesButton" })
        {
            var button = (Button)view.FindName(name);
            AssertColor("#FFFFFF", button.Background);
            var bg = (Border)button.Template.FindName("Bg", button);
            AssertColor("#FFFFFF", bg.Background);
            Assert.Equal(button.ActualHeight / 2, bg.CornerRadius.TopLeft);
        }
        scenario.Vm.OpenSettingsCommand.Execute(null);
        WpfHost.Pump();
        AssertBand((Border)view.SettingsScreen.FindName("SettingsHeader"));
        AssertColor("#FFFFFF", view.SettingsScreen.Background);
    });

    [Fact]
    public void Inactive_caption_keeps_grey_band_and_unchanged_icon() => WpfHost.Run(() =>
    {
        var bar = new TitleBar();
        WpfHost.Layout(bar, 1024, 32);
        var icon = (Image)bar.FindName("AppIcon");
        var source = icon.Source;
        bar.IsWindowActive = false;
        WpfHost.Pump();
        AssertColor("#F5F5F3", bar.Background);
        AssertColor("#68686C", ((TextBlock)bar.FindName("CaptionTitle")).Foreground);
        Assert.Same(source, icon.Source);
        Assert.Equal(1, icon.Opacity);
        AssertColor("#A3A39D", bar.MaximizeButton.Foreground);
    });

    [Fact]
    public void First_run_body_stays_white_below_the_grey_caption() => WpfHost.RunAsync(async () =>
    {
        var scenario = await new Scenario().InitAsync();
        var view = new MainView { DataContext = scenario.Vm };
        WpfHost.Layout(view, 1024, 608);
        AssertColor("#FFFFFF", view.Background);
        Assert.Equal(Visibility.Visible, ((Grid)view.FindName("FirstRun")).Visibility);
    });

    [Fact]
    public void Files_chosen_with_a_mouse_get_selection_without_a_keyboard_ring() => WpfHost.RunAsync(async () =>
    {
        var scenario = await new Scenario().InitAsync();
        var view = new MainView { DataContext = scenario.Vm };
        WpfHost.Layout(view, 1280, 768);
        view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
        await scenario.Vm.ChooseFilesCommand.RunAsync();
        WpfHost.Layout(view, 1280, 768);
        var row = Assert.IsType<ListBoxItem>(view.FileFocusTarget);
        Assert.True(row.IsSelected);
        Assert.False(FocusRing.GetIsVisible(row));
        Assert.Equal(Visibility.Collapsed, ((Border)row.Template.FindName("Ring", row)).Visibility);
    });

    [Fact]
    public void Selected_row_uses_inset_bar_and_only_keyboard_focus_adds_the_ring() => WpfHost.RunAsync(async () =>
    {
        var scenario = await new Scenario().ResultsAsync();
        var view = new MainView { DataContext = scenario.Vm };
        WpfHost.Layout(view, 1280, 768);
        var list = (ListBox)view.FindName("FileList");
        var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(scenario.Vm.SelectedRow));
        var root = (Grid)row.Template.FindName("Root", row);
        var bar = (Border)row.Template.FindName("Bar", row);
        var ring = (Border)row.Template.FindName("Ring", row);
        FocusRing.SetIsVisible(row, false);
        WpfHost.Pump();
        AssertColor("#ECECE8", root.Background);
        AssertColor("#111112", bar.Background);
        Assert.Equal(3, bar.Width);
        Assert.Equal(0, bar.TranslatePoint(new Point(), root).X);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(Visibility.Collapsed, ring.Visibility);
        FocusRing.Show(row);
        WpfHost.Pump();
        Assert.Equal(Visibility.Visible, ring.Visibility);
        AssertColor("#111112", ring.BorderBrush);
        Assert.Equal(new Thickness(1.5), ring.BorderThickness);
        // A click on an already-focused row must remove its stale keyboard ring.
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
        WpfHost.Pump();
        Assert.False(FocusRing.GetIsVisible(row));
        Assert.Equal(Visibility.Collapsed, ring.Visibility);
        Assert.True(row.IsSelected);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        list.SelectedItem = list.Items[1];
        WpfHost.Pump();
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
        AssertColor("#FFFFFF", root.Background);
    });

    static void AssertBand(Border header)
    {
        AssertColor("#F5F5F3", header.Background);
        AssertColor("#E3E3DE", header.BorderBrush);
        Assert.Equal(new Thickness(0, 0, 0, 1), header.BorderThickness);
    }

    static void AssertColor(string expected, Brush actual) =>
        Assert.Equal((Color)ColorConverter.ConvertFromString(expected), Assert.IsType<SolidColorBrush>(actual).Color);
}
