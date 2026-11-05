using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using SignMyApp.App.Controls;
using SignMyApp.App.ViewModels;

namespace SignMyApp.App.Views;

public partial class SignDialog : UserControl
{
    SignDialogViewModel? _vm;
    SignKind? _focusedFor;
    protected override AutomationPeer OnCreateAutomationPeer() => new DialogAutomationPeer(this);

    public SignDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as SignDialogViewModel;
            _focusedFor = null;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
            if (_vm?.AlwaysOpen == true) BringExplanationIntoView();
        };
    }

    /// <summary>The element that gets focus when the window opens or changes state.</summary>
    public UIElement InitialFocusTarget => (_vm?.Kind) switch
    {
        SignKind.Progress => StopButton,
        SignKind.Done => BackButton,
        SignKind.Error => ErrorSecondary,
        _ => CancelButton,
    };

    /// <summary>The Sign window opens on Cancel (founder decision 1). Esc is Cancel.</summary>
    public void ApplyInitialFocus()
    {
        _focusedFor = _vm?.Kind;
        FocusRing.Show(InitialFocusTarget);
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignDialogViewModel.AlwaysOpen) && _vm?.AlwaysOpen == true)
            BringExplanationIntoView();
        if (_vm is null || !IsVisible) return;
        if (_focusedFor is null || _focusedFor == _vm.Kind) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, ApplyInitialFocus);
    }

    void BringExplanationIntoView() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if (_vm?.AlwaysOpen == true) AlwaysExplanation.BringIntoView();
    });
}
