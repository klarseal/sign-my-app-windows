using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SignMyApp.App.Controls;
using SignMyApp.App.Services;
using SignMyApp.App.ViewModels;
using SignMyApp.App.Views;

namespace SignMyApp.App.Tests;

public class AccessibilityReviewTests
{
    static MainView View(Scenario s)
    {
        var view = new MainView { DataContext = s.Vm };
        WpfHost.Layout(view, 1024, 640);
        return view;
    }

    static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }

    static AutomationPeer Peer(UIElement element) => UIElementAutomationPeer.CreatePeerForElement(element)!;
    static string Name(UIElement element) => Peer(element).GetName();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settings_entry_and_return_show_rings_on_Back_and_the_opening_settings_button(bool withFiles) => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().InitAsync();
        if (withFiles) await s.Vm.ChooseFilesCommand.RunAsync();
        var view = View(s);
        var opener = (Button)view.FindName(withFiles ? "ListSettingsButton" : "FirstSettingsButton");
        s.Vm.OpenSettingsCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        Assert.True(FocusRing.GetIsVisible(view.SettingsScreen.BackFocusTarget));
        s.Vm.BackCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        Assert.True(FocusRing.GetIsVisible(opener));
        Assert.Equal(withFiles ? Screen.List : Screen.First, s.Vm.Screen);
    });

    [Fact]
    public void Entering_the_file_screen_focuses_a_selected_row_container() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().InitAsync();
        var view = View(s);
        await s.Vm.ChooseFilesCommand.RunAsync();
        WpfHost.Layout(view, 1024, 640);
        var row = Assert.IsType<ListBoxItem>(view.FileFocusTarget);
        Assert.Same(s.Vm.SelectedRow, row.DataContext);
        Assert.True(FocusRing.GetIsVisible(row));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closing_a_dialog_without_an_opener_uses_a_row_or_certificate_never_the_list(bool removeRows) => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        await s.Vm.OpenCertificatesCommand.RunAsync();
        WpfHost.Layout(view, 1024, 640);
        if (removeRows) s.Vm.Rows.Clear();
        s.Vm.CertDialog!.CancelCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var target = view.FileFocusTarget;
        if (removeRows) Assert.Same(view.FindName("CertificateButton"), target);
        else Assert.IsType<ListBoxItem>(target);
        Assert.True(FocusRing.GetIsVisible(target));
        Assert.False(FocusRing.GetIsVisible((ListBox)view.FindName("FileList")));
    });

    [Fact]
    public void Keep_restores_that_rows_Forget_and_removal_chooses_the_next_row_then_timestamp() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario(trustThisProject: true).ResultsAsync();
        var view = View(s);
        s.Vm.OpenSettingsCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var settings = s.Vm.Settings;
        var first = settings.TrustRows[0];
        var next = settings.TrustRows[1];
        first.AskCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var keep = Children<Button>(view.SettingsScreen).Single(b => b.Name == "KeepButton" && ReferenceEquals(b.DataContext, first));
        Assert.True(FocusRing.GetIsVisible(keep));
        Assert.Equal($"Keep {first.Folder}", Name(keep));
        var confirmForget = Children<Button>(view.SettingsScreen).Single(b => ReferenceEquals(b.Command, first.ForgetCommand));
        Assert.Equal($"Forget {first.Folder}", Name(confirmForget));
        first.KeepCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var restored = Assert.IsType<Button>(view.SettingsScreen.LastFocusTarget);
        Assert.Equal("ForgetButton", restored.Name);
        Assert.Same(first, restored.DataContext);
        Assert.True(FocusRing.GetIsVisible(restored));
        first.AskCommand.Execute(null);
        first.ForgetCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        Assert.Same(next, ((Button)view.SettingsScreen.LastFocusTarget!).DataContext);
        next.AskCommand.Execute(null);
        next.ForgetCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        Assert.Same(view.SettingsScreen.FindName("TimestampBox"), view.SettingsScreen.LastFocusTarget);
        Assert.True(FocusRing.GetIsVisible(view.SettingsScreen.LastFocusTarget!));
    });

    [Fact]
    public void Forgetting_the_last_row_focuses_timestamp_even_when_earlier_rows_remain() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario(trustThisProject: true).ResultsAsync();
        var view = View(s);
        s.Vm.OpenSettingsCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var last = s.Vm.Settings.TrustRows.Last();
        last.AskCommand.Execute(null);
        last.ForgetCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        Assert.Single(s.Vm.Settings.TrustRows);
        Assert.Same(view.SettingsScreen.FindName("TimestampBox"), view.SettingsScreen.LastFocusTarget);
    });

    [Fact]
    public void Sans_font_resolves_to_the_embedded_Manrope_resources() => WpfHost.Run(() =>
    {
        var family = (FontFamily)Application.Current.FindResource("Font.Sans");
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.Bold })
        {
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var glyph));
            Assert.Equal("pack", glyph.FontUri.Scheme);
            Assert.Contains("/signmyapp;component/fonts/manrope-", glyph.FontUri.AbsoluteUri.ToLowerInvariant());
        }
    });

    [Fact]
    public void Compound_buttons_have_names_from_automation_peers() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        Assert.Equal("Add files", Name(Children<Button>(view).Single(b => ReferenceEquals(b.Command, s.Vm.AddFilesCommand))));
        Assert.Equal("Check again", Name(Children<Button>(view).First(b => ReferenceEquals(b.Command, s.Vm.CheckAgainCommand))));
        Assert.Equal("Files", Name((Button)view.SettingsScreen.FindName("BackButton")));
        await s.Vm.OpenCertificatesCommand.RunAsync();
        WpfHost.Layout(view, 1024, 640);
        var refresh = Children<Button>(view.CertificateDialogView).Where(b => ReferenceEquals(b.Command, s.Vm.CertDialog!.RefreshCommand)).ToList();
        Assert.Equal(2, refresh.Count);
        Assert.All(refresh, b => Assert.Equal(s.Vm.CertDialog!.RefreshLabel, Name(b)));
    });

    [Fact]
    public void File_row_peer_name_updates_for_ticks_and_status_including_bulk_ticks() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        var row = s.Row("MyApp.exe");
        var container = (ListBoxItem)((ListBox)view.FindName("FileList")).ItemContainerGenerator.ContainerFromItem(row);
        Assert.Equal("MyApp.exe, Not signed, ticked for signing", Name(container));
        row.IsChecked = false;
        WpfHost.Pump();
        Assert.Equal("MyApp.exe, Not signed, not ticked for signing", Name(container));
        s.Vm.AllChecked = true;
        WpfHost.Pump();
        Assert.Equal(row.RowAutomationName, Name(container));
        Assert.EndsWith(", ticked for signing", Name(container));
        row.Kind = RowKind.Checking;
        WpfHost.Pump();
        Assert.Contains(Text.StatusChecking, Name(container));
    });

    [Fact]
    public void Language_items_expose_their_names_and_support_type_search() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().InitAsync();
        var view = View(s);
        s.Vm.OpenSettingsCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var combo = (ComboBox)view.SettingsScreen.FindName("LanguageBox");
        Assert.Equal("Name", TextSearch.GetTextPath(combo));
        foreach (var option in s.Vm.Settings.Languages)
        {
            Assert.Equal(option.Name, option.ToString());
            Assert.Equal(option.Name, Name(new ComboBoxItem { Content = option }));
        }
    });

    [Fact]
    public void Modal_background_is_disabled_and_dialogs_are_UIA_windows() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        var page = (Grid)view.FindName("Page");
        Assert.True(page.IsEnabled);
        await s.Vm.OpenCertificatesCommand.RunAsync();
        WpfHost.Layout(view, 1024, 640);
        Assert.False(page.IsEnabled);
        Assert.False(Peer((Button)view.FindName("CertificateButton")).IsEnabled());
        Assert.True(view.CertificateDialogView.IsEnabled);
        Assert.Equal(AutomationControlType.Window, Peer(view.CertificateDialogView).GetAutomationControlType());
        Assert.Equal(Text.ChooseCertificate, Name(view.CertificateDialogView));
        s.Vm.CertDialog!.CancelCommand.Execute(null);
        await s.Vm.PrimaryCommand.RunAsync();
        WpfHost.Layout(view, 1024, 640);
        Assert.Equal(AutomationControlType.Window, Peer(view.SignDialogPart).GetAutomationControlType());
        Assert.Equal(s.Vm.SignDialog!.Title, Name(view.SignDialogPart));
        Assert.False(page.IsEnabled);
        s.Vm.SignDialog.CancelCommand.Execute(null);
        WpfHost.Pump();
        Assert.True(page.IsEnabled);
    });

    static byte[] Pixels(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Modal_input_disabling_preserves_background_pixels(bool recreateView) => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        var page = (Grid)view.FindName("Page");
        var before = Pixels(page);
        await s.Vm.OpenCertificatesCommand.RunAsync();
        WpfHost.Layout(view, 1024, 640);
        if (recreateView)
        {
            view = View(s);
            page = (Grid)view.FindName("Page");
        }
        Assert.True(before.SequenceEqual(Pixels(page)), string.Join("; ", Children<Button>(page).Select(b => $"{b.Name}/{b.Content}: enabled={b.IsEnabled} keep={ModalAppearance.GetKeepEnabled(b)} fg={b.Foreground}")));
    });

    sealed class OffScreenSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    [Fact]
    public void Certificate_keyboard_focus_selects_and_Enter_uses_the_focused_radio() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        await s.Vm.OpenCertificatesCommand.RunAsync();
        var view = View(s);
        var dialog = view.CertificateDialogView;
        var group = (ItemsControl)dialog.FindName("CertList");
        Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(group));
        var radio = Children<RadioButton>(group).Last(r => r.IsEnabled);
        radio.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, radio) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        var row = (CertificateRowViewModel)radio.DataContext;
        Assert.Same(row, s.Vm.CertDialog!.SelectedRow);
        Assert.Single(s.Vm.CertDialog.Rows, r => r.IsSelected);
        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, new OffScreenSource(), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        radio.RaiseEvent(enter);
        Assert.True(enter.Handled);
        Assert.Same(row.Certificate, s.Vm.Certificate);
        Assert.Equal(Overlay.None, s.Vm.Overlay);
        Assert.All(Children<Button>(view), button => Assert.False(button.IsDefault));
    });

    [Fact]
    public void Status_regions_are_polite_and_raise_live_changes_on_bound_updates() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        var changed = new HashSet<UIElement>();
        void Changed(UIElement element) => changed.Add(element);
        LiveRegion.Changed += Changed;
        try
        {
            var footer = Children<TextBlock>(view).Single(t => LiveRegion.GetText(t) == s.Vm.FootNote && AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite);
            s.Row("MyApp.exe").IsChecked = false;
            s.Vm.SelectedRow = s.Row("Contoso.Json.dll");
            WpfHost.Layout(view, 1024, 640);
            var detail = (DetailPanel)view.FindName("Detail");
            var status = (TextBlock)detail.FindName("StatusLabel");
            Assert.Contains(footer, changed);
            Assert.Contains(status, changed);
            Assert.Equal(AutomationLiveSetting.Polite, Peer(status).GetLiveSetting());
            await s.Vm.PrimaryCommand.RunAsync();
            WpfHost.Layout(view, 1024, 640);
            var title = (TextBlock)view.SignDialogPart.FindName("TitleText");
            var signStatus = (TextBlock)view.SignDialogPart.FindName("StatusText");
            changed.Clear();
            await s.Vm.SignDialog!.SignOnceCommand.RunAsync();
            WpfHost.Layout(view, 1024, 640);
            Assert.Contains(title, changed);
            Assert.Contains(signStatus, changed);
            Assert.Equal(AutomationLiveSetting.Polite, Peer(title).GetLiveSetting());
            Assert.Equal(AutomationLiveSetting.Polite, Peer(signStatus).GetLiveSetting());
        }
        finally { LiveRegion.Changed -= Changed; }
    });

    [Fact]
    public void Invalid_timestamp_is_exposed_by_the_field_peer_and_announced_then_cleared() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().InitAsync();
        var view = View(s);
        s.Vm.OpenSettingsCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var box = (TextBox)view.SettingsScreen.FindName("TimestampBox");
        var error = (TextBlock)view.SettingsScreen.FindName("TimestampError");
        var notices = new List<UIElement>();
        void Changed(UIElement element) => notices.Add(element);
        LiveRegion.Changed += Changed;
        try
        {
            var saved = s.Vm.Settings.EffectiveTimestampUrl;
            s.Vm.Settings.TimestampUrl = "not a URL";
            WpfHost.Pump();
            Assert.Equal(Text.NotAValidAddress, Peer(box).GetHelpText());
            Assert.Equal(Text.NotAValidAddress, Peer(box).GetItemStatus());
            Assert.Equal(AutomationLiveSetting.Polite, Peer(error).GetLiveSetting());
            Assert.Contains(error, notices);
            Assert.Equal(saved, s.Vm.Settings.EffectiveTimestampUrl);
            notices.Clear();
            s.Vm.Settings.TimestampUrl = "https://timestamp.digicert.com";
            WpfHost.Pump();
            Assert.Empty(Peer(box).GetHelpText());
            Assert.Empty(Peer(box).GetItemStatus());
            Assert.Contains(error, notices);
        }
        finally { LiveRegion.Changed -= Changed; }
    });

    [Fact]
    public void Select_all_exposes_mixed_state_then_selects_and_clears_eligible_files() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        var box = (CheckBox)view.FindName("SelectAll");
        Assert.True(box.IsThreeState);
        Assert.True(box.IsChecked);
        s.Row("MyApp.exe").IsChecked = false;
        WpfHost.Pump();
        Assert.Null(box.IsChecked);
        var toggle = (System.Windows.Automation.Provider.IToggleProvider)Peer(box);
        Assert.Equal(ToggleState.Indeterminate, toggle.ToggleState);
        toggle.Toggle();
        WpfHost.Pump();
        Assert.True(s.Vm.AllChecked);
        toggle.Toggle();
        WpfHost.Pump();
        Assert.False(s.Vm.AllChecked);
        Assert.DoesNotContain(s.Vm.Rows, row => row.IsChecked);
        toggle.Toggle();
        WpfHost.Pump();
        Assert.True(s.Vm.AllChecked);
    });

    [Fact]
    public void Virtual_file_list_realizes_only_a_viewport_and_scrolls_to_the_selected_row() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        for (var i = 0; i < 1000; i++) s.Vm.Rows.Add(new FileRowViewModel($@"C:\work\App\file{i}.exe"));
        var view = View(s);
        var list = (ListBox)view.FindName("FileList");
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
        Assert.InRange(Children<ListBoxItem>(list).Count(), 1, 50);
        s.Vm.SelectedRow = s.Vm.Rows.Last();
        var row = Assert.IsType<ListBoxItem>(view.FileFocusTarget);
        Assert.Same(s.Vm.SelectedRow, row.DataContext);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Always_explanation_scrolls_into_the_small_dialog_viewport(bool initiallyOpen) => WpfHost.RunAsync(async () =>
    {
        var s = new Scenario();
        var vm = await s.ReviewAsync(withMsi: true);
        vm.AlwaysOpen = initiallyOpen;
        var view = View(s);
        var dialog = view.SignDialogPart;
        if (!initiallyOpen) vm.ToggleAlwaysCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var body = (ScrollViewer)dialog.FindName("Body");
        var explanation = (Border)dialog.FindName("AlwaysExplanation");
        Assert.True(body.VerticalOffset > 0);
        var bottom = explanation.TranslatePoint(new Point(0, explanation.ActualHeight), body).Y;
        Assert.InRange(bottom, 0, body.ViewportHeight + 1);
    });

    [Fact]
    public void Settings_Escape_keeps_a_confirm_then_goes_back_and_respects_an_open_dropdown() => WpfHost.RunAsync(async () =>
    {
        var s = await new Scenario().ResultsAsync();
        var view = View(s);
        s.Vm.OpenSettingsCommand.Execute(null);
        WpfHost.Layout(view, 1024, 640);
        var row = s.Vm.Settings.TrustRows[0];
        row.AskCommand.Execute(null);
        Assert.False(view.HandleKey(Key.Escape, languageDropDownOpen: true));
        Assert.True(row.IsConfirming);
        Assert.True(s.Vm.IsSettings);
        Assert.True(view.HandleKey(Key.Escape));
        Assert.False(row.IsConfirming);
        Assert.True(s.Vm.IsSettings);
        // The popup is deliberately never opened: no HWNDs in the off-screen test host.
        Assert.True(view.HandleKey(Key.Escape));
        Assert.True(s.Vm.IsList);
    });
}
