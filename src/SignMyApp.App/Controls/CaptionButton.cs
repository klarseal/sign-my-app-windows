using System.Windows;
using System.Windows.Controls;

namespace SignMyApp.App.Controls;

/// <summary>Non-client mouse state complements Button's ordinary client mouse and UIA input.</summary>
public sealed class CaptionButton : Button
{
    public static readonly DependencyProperty IsCaptionActiveProperty = DependencyProperty.Register(
        nameof(IsCaptionActive), typeof(bool), typeof(CaptionButton), new PropertyMetadata(true));
    public static readonly DependencyProperty IsNonClientHoverProperty = DependencyProperty.Register(
        nameof(IsNonClientHover), typeof(bool), typeof(CaptionButton), new PropertyMetadata(false));
    public static readonly DependencyProperty IsNonClientPressedProperty = DependencyProperty.Register(
        nameof(IsNonClientPressed), typeof(bool), typeof(CaptionButton), new PropertyMetadata(false));

    public bool IsCaptionActive { get => (bool)GetValue(IsCaptionActiveProperty); set => SetValue(IsCaptionActiveProperty, value); }
    public bool IsNonClientHover { get => (bool)GetValue(IsNonClientHoverProperty); set => SetValue(IsNonClientHoverProperty, value); }
    public bool IsNonClientPressed { get => (bool)GetValue(IsNonClientPressedProperty); set => SetValue(IsNonClientPressedProperty, value); }
}
