using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SignMyApp.App.Controls;
using SignMyApp.App.ViewModels;

namespace SignMyApp.App.Views;

public partial class MainView : UserControl
{
    /// <summary>Below this the 1024 × 640 layout is used.</summary>
    public const double CompactWidth = 1180;
    public const double CompactHeight = 720;

    IInputElement? _opener;
    UIElement? _settingsOpener;
    Screen _shownScreen;
    MainViewModel? _vm;
    bool _keyboardNavigation = true;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateCompact(e.NewSize);
        DataContextChanged += OnDataContextChanged;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _vm?.DragLeave();
        Drop += OnDrop;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += (_, _) => _keyboardNavigation = false;
    }

    public CertificateDialog CertificateDialogView => CertDialogView;
    public SignDialog SignDialogPart => SignDialogView;

    public void UpdateCompact(Size size)
    {
        if (_vm is null || size.Width <= 0) return;
        _vm.IsCompact = size.Width < CompactWidth || size.Height < CompactHeight;
    }

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as MainViewModel;
        _shownScreen = _vm?.Screen ?? Screen.First;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsFirstRun) or nameof(MainViewModel.IsList) or nameof(MainViewModel.IsSettings) or nameof(MainViewModel.Screen) or "" or null)
        {
            var screen = _vm?.Screen ?? Screen.First;
            if (screen != _shownScreen)
            {
                var fromSettings = _shownScreen == Screen.Settings;
                var fromList = _shownScreen == Screen.List;
                _shownScreen = screen;
                if (screen == Screen.Settings)
                    _settingsOpener = Keyboard.FocusedElement is UIElement focused && (ReferenceEquals(focused, ListSettingsButton) || ReferenceEquals(focused, FirstSettingsButton))
                        ? focused : fromList ? ListSettingsButton : FirstSettingsButton;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (_vm?.HasOverlay == true) return;
                    if (_vm?.IsSettings == true) FocusRing.Show(SettingsScreen.BackFocusTarget);
                    else if (fromSettings && _settingsOpener is { IsEnabled: true } opener) FocusRing.Show(opener);
                    else if (_vm?.IsList == true) FocusFileRow();
                    else FocusRing.Show(ChooseFilesButton);
                });
            }
        }
        if (e.PropertyName is not (nameof(MainViewModel.Overlay) or "" or null)) return;
        var overlay = _vm?.Overlay ?? Overlay.None;
        if (overlay == _shownOverlay) return;
        var previous = _shownOverlay;
        _shownOverlay = overlay;
        if (overlay != Overlay.None)
        {
            if (previous == Overlay.None) _opener = Keyboard.FocusedElement is DependencyObject focused && IsAncestorOf(focused) ? Keyboard.FocusedElement : null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, ApplyDialogFocus);
        }
        else
        {
            var back = _opener;
            _opener = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (back is UIElement el && IsAncestorOf(el) && el.IsVisible && el.IsEnabled) FocusRing.Show(el);
                else FocusFileRow();
            });
        }
    }

    Overlay _shownOverlay = Overlay.None;

    internal UIElement FileFocusTarget
    {
        get
        {
            var row = FileList.SelectedItem ?? FileList.Items.Cast<object>().FirstOrDefault();
            if (row is not null)
            {
                FileList.ScrollIntoView(row);
                FileList.UpdateLayout();
                if (FileList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item) return item;
            }
            return CertificateButton;
        }
    }

    void FocusFileRow()
    {
        var target = FileFocusTarget;
        target.Focus();
        FocusRing.SetIsVisible(target, _keyboardNavigation);
    }

    /// <summary>Puts focus inside the open dialog: Cancel for the Sign window, the chosen certificate otherwise.</summary>
    public void ApplyDialogFocus()
    {
        switch (_vm?.Overlay)
        {
            case Overlay.Sign: SignDialogView.ApplyInitialFocus(); break;
            case Overlay.Certificates: CertDialogView.ApplyInitialFocus(); break;
        }
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _keyboardNavigation = true;
        if (HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>Esc = Cancel in a dialog (never signs), Keep in a Forget confirm.</summary>
    public bool HandleKey(Key key) => HandleKey(key, SettingsScreen.IsLanguageDropDownOpen);

    internal bool HandleKey(Key key, bool languageDropDownOpen)
    {
        if (key != Key.Escape || _vm is null) return false;
        if (_vm.Overlay == Overlay.Sign && _vm.SignDialog is { } s) return s.Escape();
        if (_vm.Overlay == Overlay.Certificates && _vm.CertDialog is { } c) { c.CancelCommand.Execute(null); return true; }
        if (_vm.IsSettings && languageDropDownOpen) return false;
        if (_vm.IsSettings && _vm.Settings.TrustRows.FirstOrDefault(r => r.IsConfirming) is { } row) { row.KeepCommand.Execute(null); return true; }
        if (_vm.IsSettings) { _vm.BackCommand.Execute(null); return true; }
        return false;
    }

    /// <summary>The list is one tab stop: arrows move, Space ticks the selected file.</summary>
    void OnFileListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && _vm is not null)
        {
            _vm.ToggleSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop) && _vm is { HasOverlay: false };
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        _vm?.DragEnter(files);
        e.Handled = true;
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm is null || _vm.HasOverlay) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) { _vm.DragLeave(); return; }
        e.Handled = true;
        try { await _vm.DropAsync(paths); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}
