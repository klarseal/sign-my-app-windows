using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SignMyApp.App.Controls;
using SignMyApp.App.ViewModels;

namespace SignMyApp.App.Views;

public partial class SettingsView : UserControl
{
    SettingsViewModel? _settings;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_settings is not null)
            {
                _settings.FocusRequested -= OnFocusRequested;
                _settings.ConfirmFocusRequested -= OnConfirmFocusRequested;
            }
            _settings = (DataContext as MainViewModel)?.Settings;
            if (_settings is not null)
            {
                _settings.FocusRequested += OnFocusRequested;
                _settings.ConfirmFocusRequested += OnConfirmFocusRequested;
            }
        };
    }

    internal UIElement BackFocusTarget => BackButton;
    internal bool IsLanguageDropDownOpen => LanguageBox.IsDropDownOpen;
    internal UIElement? LastFocusTarget { get; private set; }

    void OnConfirmFocusRequested(TrustRowViewModel row) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        UpdateLayout();
        var keep = Find<Button>(this).FirstOrDefault(b => b.Name == "KeepButton" && ReferenceEquals(b.DataContext, row));
        if (keep is not null) FocusIfConfirming(keep);
    });

    void OnFocusRequested(TrustRowViewModel? row) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        UpdateLayout();
        var target = row is null ? null : Find<Button>(this).FirstOrDefault(b => b.Name == "ForgetButton" && ReferenceEquals(b.DataContext, row));
        LastFocusTarget = target ?? (UIElement)TimestampBox;
        FocusRing.Show(LastFocusTarget);
    });

    void OnKeepLoaded(object sender, RoutedEventArgs e) => FocusIfConfirming(sender);

    void OnKeepVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FocusIfConfirming(sender));
    }

    static void FocusIfConfirming(object sender)
    {
        if (sender is Button { DataContext: TrustRowViewModel { IsConfirming: true } } b) FocusRing.Show(b);
    }

    /// <summary>Puts focus on Keep in an open Forget confirm (the safe answer).</summary>
    public void FocusConfirm()
    {
        foreach (var b in Find<Button>(this))
            if (b.Name == "KeepButton") FocusIfConfirming(b);
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Find<T>(c)) yield return d;
        }
    }
}
