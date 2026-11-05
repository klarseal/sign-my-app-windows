using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using SignMyApp.App.Controls;
using SignMyApp.App.ViewModels;

namespace SignMyApp.App.Views;

public partial class CertificateDialog : UserControl
{
    public CertificateDialog() => InitializeComponent();
    protected override AutomationPeer OnCreateAutomationPeer() => new DialogAutomationPeer(this);

    void OnRowFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is RadioButton { DataContext: CertificateRowViewModel row } && DataContext is CertificateDialogViewModel vm)
            vm.SelectRowCommand.Execute(row);
    }

    void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not RadioButton { DataContext: CertificateRowViewModel { IsUsable: true } row }
            || DataContext is not CertificateDialogViewModel vm) return;
        vm.SelectRowCommand.Execute(row);
        if (vm.UseCommand.CanExecute(null)) vm.UseCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>Focus the chosen certificate; with none to choose, the main button.</summary>
    public void ApplyInitialFocus()
    {
        var radio = FindChildren<RadioButton>(CertList).FirstOrDefault(r => r.IsChecked == true);
        if (radio is not null) { FocusRing.Show(radio); return; }
        if (RefreshPrimary.IsVisible) { FocusRing.Show(RefreshPrimary); return; }
        FocusRing.Show(CancelButton);
    }

    static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in FindChildren<T>(c)) yield return d;
        }
    }
}
