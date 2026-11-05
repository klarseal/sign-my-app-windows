using System.Windows;
using System.Windows.Controls;

namespace SignMyApp.App.Views;

public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty IsWindowActiveProperty = DependencyProperty.Register(
        nameof(IsWindowActive), typeof(bool), typeof(TitleBar), new PropertyMetadata(true));
    public static readonly DependencyProperty IsMaximizedProperty = DependencyProperty.Register(
        nameof(IsMaximized), typeof(bool), typeof(TitleBar), new PropertyMetadata(false));

    public bool IsWindowActive { get => (bool)GetValue(IsWindowActiveProperty); set => SetValue(IsWindowActiveProperty, value); }
    public bool IsMaximized { get => (bool)GetValue(IsMaximizedProperty); set => SetValue(IsMaximizedProperty, value); }

    public TitleBar() => InitializeComponent();

    void Minimize(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) SystemCommands.MinimizeWindow(window);
    }

    void Close(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) SystemCommands.CloseWindow(window);
    }

    void MaximizeOrRestore(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window) return;
        if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
        else SystemCommands.MaximizeWindow(window);
    }
}
