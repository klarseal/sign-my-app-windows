using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SignMyApp.App.ViewModels;

namespace SignMyApp.App.Controls;

public sealed class InverseBoolConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var active = values[0] is true;
        if (values[1] is UIElement page) ModalAppearance.Prepare(page, active);
        return !active;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>Disabling modal background input must not grey out the approved backdrop rendering.</summary>
public static class ModalAppearance
{
    public static readonly DependencyProperty KeepEnabledProperty = DependencyProperty.RegisterAttached(
        "KeepEnabled", typeof(bool), typeof(ModalAppearance), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetKeepEnabled(DependencyObject element) => (bool)element.GetValue(KeepEnabledProperty);
    public static void SetKeepEnabled(DependencyObject element, bool value) => element.SetValue(KeepEnabledProperty, value);

    internal static void Prepare(UIElement page, bool active)
    {
        // A second binding transfer while the page is already disabled must retain the snapshot.
        if (active && !page.IsEnabled) return;
        SetKeepEnabled(page, active);
        Visit(page, active);
    }

    static void Visit(DependencyObject element, bool active)
    {
        if (element is Control control && !BindingOperations.IsDataBound(control, KeepEnabledProperty))
            SetKeepEnabled(control, active && control.IsEnabled);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i), active);
    }
}

/// <summary>Raises UIA's live-region event when a bound status changes, including visibility-only errors.</summary>
public static class LiveRegion
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(LiveRegion), new PropertyMetadata(null, OnTextChanged));
    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);
    internal static event Action<UIElement>? Changed;

    static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element) return;
        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        Changed?.Invoke(element);
    }
}

public sealed class DialogAutomationPeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Window;
    protected override string GetClassNameCore() => Owner.GetType().Name;
}

/// <summary>The header displays mixed state, but each activation selects all or clears all.</summary>
public sealed class SelectAllCheckBox : CheckBox
{
    protected override void OnToggle() => SetCurrentValue(IsCheckedProperty, IsChecked != true);
}

/// <summary>
/// The focus ring (1.5 px ink, 2 px offset) shows when focus arrives from the keyboard, or when a dialog
/// puts it on its first control; not after a mouse click. Templates trigger on <c>FocusRing.IsVisible</c>.
/// </summary>
public static class FocusRing
{
    public static readonly DependencyProperty IsVisibleProperty = DependencyProperty.RegisterAttached(
        "IsVisible", typeof(bool), typeof(FocusRing), new FrameworkPropertyMetadata(false));

    public static bool GetIsVisible(DependencyObject d) => (bool)d.GetValue(IsVisibleProperty);
    public static void SetIsVisible(DependencyObject d, bool v) => d.SetValue(IsVisibleProperty, v);

    static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGot), true);
        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLost), true);
        EventManager.RegisterClassHandler(typeof(UIElement), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown), true);
    }

    // Clicking a row which already has focus produces no Got/LostKeyboardFocus event.
    // Clear its previous keyboard ring (also when the click lands on its checkbox/content).
    static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DependencyObject d) SetIsVisible(d, false);
    }

    static void OnGot(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.NewFocus) || sender is not DependencyObject d) return;
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) SetIsVisible(d, true);
    }

    static void OnLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OldFocus) && sender is DependencyObject d) SetIsVisible(d, false);
    }

    /// <summary>Focus an element and show its ring (dialogs opening on Cancel).</summary>
    public static void Show(UIElement element)
    {
        element.Focus();
        SetIsVisible(element, true);
    }
}

/// <summary>Status tone to the status brush from the theme.</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is Tone t ? t switch
        {
            Tone.Action => "Brush.Status.Action",
            Tone.Success => "Brush.Status.Success",
            Tone.Warn => "Brush.Status.Warn",
            Tone.Danger => "Brush.Status.Danger",
            Tone.Neutral => "Brush.Status.Neutral",
            _ => null,
        } : null;
        return key is not null && Application.Current?.TryFindResource(key) is Brush b ? b : Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true = Visible. Parameter "invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool v = value is bool b && b;
        if (parameter as string == "invert") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Non-empty string or non-null value = Visible.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool v = value switch { null => false, string s => s.Length > 0, Tone t => t != Tone.None, _ => true };
        if (parameter as string == "invert") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Height to a pill corner radius (half the height), plus an optional extra (for the focus ring).</summary>
public sealed class PillRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double h = value is double d && !double.IsNaN(d) ? d : 0;
        double extra = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
        return new CornerRadius(h / 2 + extra);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0..1 to a width share of the parent (progress bar fill).</summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double f = values.Length > 0 && values[0] is double a ? a : 0;
        double w = values.Length > 1 && values[1] is double b ? b : 0;
        return Math.Max(0, Math.Min(1, f)) * w;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>The 12 px loading arc: a light ring with an ink quarter, turning.</summary>
public sealed class Spinner : Control
{
    static Spinner()
    {
        FocusableProperty.OverrideMetadata(typeof(Spinner), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Spinner), new FrameworkPropertyMetadata(false));
    }
}
