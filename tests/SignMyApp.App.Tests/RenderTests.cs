using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SignMyApp.App.Controls;
using SignMyApp.App.Demo;
using SignMyApp.App.ViewModels;
using SignMyApp.App.Views;
using SignMyApp.Core;

namespace SignMyApp.App.Tests;

/// <summary>
/// Off-screen renders of the canvas boards to tests/artifacts/*.png, at 1280 × 800 and 1024 × 640.
/// Views are measured, arranged and drawn with RenderTargetBitmap on the STA test thread. No window is shown.
/// </summary>
public class RenderTests
{
    public static readonly (int W, int H, string Tag)[] Sizes = [(1280, 800, "1280x800"), (1024, 640, "1024x640")];

    static MainView View(MainViewModel vm, int w, int h)
    {
        var view = new MainView { DataContext = vm };
        vm.IsCompact = w < MainView.CompactWidth || h < MainView.CompactHeight;
        WpfHost.Layout(view, w, h);
        return view;
    }

    static void RenderBoth(string board, Func<Task<MainViewModel>> build, Action<MainView>? after = null)
    {
        foreach (var (w, h, tag) in Sizes)
        {
            WpfHost.RunAsync(async () =>
            {
                var vm = await build();
                var view = View(vm, w, h);
                if (vm.HasOverlay) view.ApplyDialogFocus();
                after?.Invoke(view);
                var path = WpfHost.Render(view, w, h, board + "-" + tag);
                Assert.True(new FileInfo(path).Length > 10_000, path);
            });
        }
    }

    [Fact]
    public void Board_1_1_first_run() => RenderBoth("1.1-first-run", async () => (await new Scenario().InitAsync()).Vm);

    [Fact]
    public void Board_1_1_drag_over() => RenderBoth("1.1-drag-over", async () =>
    {
        var s = await new Scenario().InitAsync();
        s.Vm.DragEnter(true);
        return s.Vm;
    });

    [Fact]
    public void Board_1_2_checking() => RenderBoth("1.2-checking", async () =>
    {
        var s = await new Scenario().InitAsync();
        s.Demo.Checker.Hold("Contoso.Json.dll");
        s.Demo.Checker.Hold("Legacy.Plugin.dll");
        _ = s.Vm.ChooseFilesCommand.RunAsync();
        s.Vm.SelectedRow = s.Row("Contoso.Json.dll");
        return s.Vm;
    });

    [Fact]
    public void Board_1_3_results_certificate_found() => RenderBoth("1.3-results", async () => (await new Scenario().ResultsAsync()).Vm);

    [Fact]
    public void Board_1_4_results_no_certificate() => RenderBoth("1.4-results-no-certificate", async () => (await new Scenario(DemoCertificates.None).ResultsAsync()).Vm);

    [Fact]
    public void Board_2_1_choose_a_certificate() => RenderBoth("2.1-choose-certificate", async () =>
    {
        var s = await new Scenario().ResultsAsync();
        await s.Vm.OpenCertificatesCommand.RunAsync();
        return s.Vm;
    });

    [Fact]
    public void Board_3_1_sign_window_review() => RenderBoth("3.1-sign-review", async () =>
    {
        var s = new Scenario();
        await s.ReviewAsync(withMsi: false);
        return s.Vm;
    });

    [Fact]
    public void Board_3_2_sign_window_installer_order() => RenderBoth("3.2-sign-installer", async () =>
    {
        var s = new Scenario();
        await s.ReviewAsync(withMsi: true);
        return s.Vm;
    });

    [Fact]
    public void Board_3_3_signing() => RenderBoth("3.3-signing", async () =>
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        s.Demo.Signer.Hold("MyApp.Core.dll");
        _ = d.SignOnceCommand.RunAsync();
        return s.Vm;
    });

    [Fact]
    public void Board_3_4_signed_and_checked() => RenderBoth("3.4-signed", async () =>
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: true);
        await d.SignOnceCommand.RunAsync();
        return s.Vm;
    });

    [Fact]
    public void Board_3_5_back_in_the_list() => RenderBoth("3.5-back-in-list", async () =>
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: true);
        await d.SignOnceCommand.RunAsync();
        d.BackToFilesCommand.Execute(null);
        s.Vm.SelectedRow = s.Row("MyAppSetup.msi");
        s.Vm.Detail!.IsWhyOpen = true;
        return s.Vm;
    });

    public static TheoryData<string, string, SignOutcome> Errors => new()
    {
        { "session", "MyApp.exe", SignOutcome.SessionClosed },
        { "timestamp", "MyApp.exe", SignOutcome.TimestampUnreachable },
        { "changed", "MyApp.Core.dll", SignOutcome.FileChangedAfterReview },
        { "in-use", "Updater.exe", SignOutcome.FileInUse },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Board_3_6_errors(string tag, string file, SignOutcome outcome) => RenderBoth("3.6-error-" + tag, async () =>
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        s.Demo.Signer.FailOnce(file, outcome);
        await d.SignOnceCommand.RunAsync();
        Assert.Equal(SignKind.Error, d.Kind);
        return s.Vm;
    });

    [Fact]
    public void Board_3_6_on_demand_full_sha_and_always() => RenderBoth("3.6-review-full-sha-always", async () =>
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        d.ShowFull = true;
        d.AlwaysOpen = true;
        return s.Vm;
    });

    [Fact]
    public void Board_4_1_settings_forget_confirm() => RenderBoth("4.1-settings", async () =>
    {
        var s = await new Scenario(trustThisProject: true).ResultsAsync();
        s.Vm.OpenSettingsCommand.Execute(null);
        s.Vm.Settings.TrustRows[1].AskCommand.Execute(null);
        return s.Vm;
    }, view => { WpfHost.Layout(view, view.Width, view.Height); view.SettingsScreen.FocusConfirm(); });

    [Fact]
    public void Board_4_2_small_window_results()
    {
        WpfHost.RunAsync(async () =>
        {
            var s = await new Scenario().ResultsAsync();
            s.Vm.SelectedRow = s.Row("Updater.exe");
            var view = View(s.Vm, 1024, 640);
            Assert.True(s.Vm.IsCompact);
            WpfHost.Render(view, 1024, 640, "4.2-small-1024x640");
        });
    }

    [Fact]
    public void Board_4_3_small_window_sign_window_scrolls()
    {
        WpfHost.RunAsync(async () =>
        {
            var s = new Scenario();
            await s.ReviewAsync(withMsi: true);
            var view = View(s.Vm, 1024, 640);
            view.ApplyDialogFocus();
            WpfHost.Render(view, 1024, 640, "4.3-small-sign-window-1024x640");
            var dialog = view.SignDialogPart;
            var body = (ScrollViewer)dialog.FindName("Body");
            Assert.True(dialog.ActualHeight <= 640 - 32 + 0.5, "dialog fits the window height");
            Assert.True(body.ExtentHeight > body.ViewportHeight, "the Sign window body scrolls");
            var cancel = (Button)dialog.FindName("CancelButton");
            Assert.True(cancel.IsVisible || cancel.ActualHeight > 0);
            Assert.True(cancel.TranslatePoint(new Point(0, cancel.ActualHeight), view).Y <= 640, "the buttons stay in view");
        });
    }

    [Fact]
    public void Board_1_5_detail_panel_every_state()
    {
        WpfHost.RunAsync(async () =>
        {
            var s = await new Scenario().ResultsAsync();
            var cells = new List<(string Eyebrow, DetailViewModel Vm)>();
            DetailViewModel Pick(string name) { s.Vm.SelectedRow = s.Row(name); return s.Vm.Detail!; }
            cells.Add(("Not signed · certificate found", Pick("MyApp.exe")));
            var none = await new Scenario(DemoCertificates.None).ResultsAsync();
            cells.Add(("Not signed · no certificate", none.Vm.Detail!));
            cells.Add(("No timestamp", Pick("Updater.exe")));
            var why = new DetailViewModel(s.Row("Updater.exe"), canSign: true, sign: null, shell: null) { IsWhyOpen = true };
            cells.Add(("No timestamp · Why? opened", why));
            cells.Add(("Signed · nothing to do", Pick("Contoso.Json.dll")));
            cells.Add(("Changed since signing", Pick("Legacy.Plugin.dll")));
            var after = new Scenario();
            var d = await after.ReviewAsync(withMsi: true);
            await d.SignOnceCommand.RunAsync();
            d.BackToFilesCommand.Execute(null);
            after.Vm.SelectedRow = after.Row("MyApp.exe");
            cells.Add(("Just signed · verified", after.Vm.Detail!));
            after.Vm.SelectedRow = after.Row("MyAppSetup.msi");
            after.Vm.Detail!.IsWhyOpen = true;
            cells.Add(("Installer held back · Why? opened", after.Vm.Detail!));
            var checking = await new Scenario().InitAsync();
            checking.Demo.Checker.Hold("Contoso.Json.dll");
            _ = checking.Vm.ChooseFilesCommand.RunAsync();
            checking.Vm.SelectedRow = checking.Row("Contoso.Json.dll");
            cells.Add(("Checking", checking.Vm.Detail!));

            var grid = new WrapPanel { ItemWidth = 400, Margin = new Thickness(40, 40, 0, 0), Background = Brush("#FAFAF9") };
            foreach (var (eyebrow, vm) in cells)
            {
                var stack = new StackPanel { Margin = new Thickness(0, 0, 24, 40) };
                stack.Children.Add(new TextBlock { Text = eyebrow.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("#68686C"), Margin = new Thickness(0, 0, 0, 10) });
                stack.Children.Add(new Border
                {
                    BorderBrush = Brush("#E3E3DE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = Brushes.White,
                    Child = new DetailPanel { DataContext = vm }, ClipToBounds = true,
                });
                grid.Children.Add(stack);
            }
            var root = new Border { Background = Brush("#FAFAF9"), Child = grid };
            WpfHost.Render(root, 2080, 1160, "1.5-detail-states");
        });
    }

    [Fact]
    public void Board_2_2_certificate_states()
    {
        WpfHost.RunAsync(async () =>
        {
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(40) };
            foreach (var (certs, eyebrow) in new[] { (DemoCertificates.None, "None found"), (DemoCertificates.LoggedOut, "SimplySign Desktop logged out"), (DemoCertificates.Expired, "Only an expired one") })
            {
                var s = await new Scenario(certs).ResultsAsync();
                await s.Vm.OpenCertificatesCommand.RunAsync();
                var stack = new StackPanel { Margin = new Thickness(0, 0, 40, 0), VerticalAlignment = VerticalAlignment.Top };
                stack.Children.Add(new TextBlock { Text = eyebrow.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("#4A4A4C"), Margin = new Thickness(0, 0, 0, 12) });
                stack.Children.Add(new CertificateDialog { DataContext = s.Vm.CertDialog });
                grid.Children.Add(stack);
            }
            var root = new Border { Background = Brush("#ECECE8"), Child = grid };
            WpfHost.Render(root, 1840, 600, "2.2-certificate-states");
        });
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>View behaviour that needs real WPF objects: focus, Esc, shortcuts, tab order, sizes.</summary>
public class ViewTests
{
    [Fact]
    public void Sign_window_opens_on_Cancel_with_the_focus_ring()
    {
        WpfHost.RunAsync(async () =>
        {
            var s = new Scenario();
            await s.ReviewAsync(withMsi: false);
            var view = new MainView { DataContext = s.Vm };
            WpfHost.Layout(view, 1280, 800);
            view.ApplyDialogFocus();
            var cancel = (Button)view.SignDialogPart.FindName("CancelButton");
            Assert.Same(cancel, view.SignDialogPart.InitialFocusTarget);
            Assert.True(FocusRing.GetIsVisible(cancel));
            Assert.Equal("Cancel", cancel.Content);
            var buttons = new[] { "CancelButton", "SignOnceButton", "AlwaysButton" }.Select(n => ((Button)view.SignDialogPart.FindName(n)).Content).ToList();
            Assert.Equal(["Cancel", "Sign once", "Always for this project"], buttons);
        });
    }

    [Fact]
    public void Esc_in_the_Sign_window_cancels_and_signs_nothing()
    {
        WpfHost.RunAsync(async () =>
        {
            var s = new Scenario();
            await s.ReviewAsync(withMsi: false);
            var view = new MainView { DataContext = s.Vm };
            WpfHost.Layout(view, 1280, 800);
            Assert.True(view.HandleKey(Key.Escape));
            Assert.Equal(Overlay.None, s.Vm.Overlay);
            Assert.Empty(s.Demo.Signer.Plans);
        });
    }

    [Fact]
    public void Esc_in_the_certificate_dialog_cancels()
    {
        WpfHost.RunAsync(async () =>
        {
            var s = await new Scenario().ResultsAsync();
            await s.Vm.OpenCertificatesCommand.RunAsync();
            var view = new MainView { DataContext = s.Vm };
            WpfHost.Layout(view, 1280, 800);
            Assert.True(view.HandleKey(Key.Escape));
            Assert.Equal(Overlay.None, s.Vm.Overlay);
        });
    }

    [Fact]
    public void Ctrl_O_adds_files_and_F5_checks_again()
    {
        WpfHost.Run(() =>
        {
            var s = new Scenario();
            var view = new MainView { DataContext = s.Vm };
            var bindings = view.InputBindings.OfType<KeyBinding>().ToList();
            Assert.Contains(bindings, b => b.Key == Key.O && b.Modifiers == ModifierKeys.Control && ReferenceEquals(Bound(view, b), s.Vm.AddFilesCommand));
            Assert.Contains(bindings, b => b.Key == Key.F5 && b.Modifiers == ModifierKeys.None && ReferenceEquals(Bound(view, b), s.Vm.CheckAgainCommand));
        });
    }

    static object? Bound(FrameworkElement view, KeyBinding b)
    {
        WpfHost.Layout(view, 1280, 800);
        return b.Command;
    }

    [Fact]
    public void Tab_order_follows_the_canvas()
    {
        WpfHost.RunAsync(async () =>
        {
            var s = await new Scenario().ResultsAsync();
            s.Vm.SelectedRow = s.Row("Updater.exe");
            var view = new MainView { DataContext = s.Vm };
            WpfHost.Layout(view, 1280, 800);
            var stops = TabStops(view.FindName("ListScreen") as DependencyObject ?? view).Select(Describe).ToList();
            var expected = new[] { "Certificate", "Add files", "Settings", "Select all", "File list", "Why?", "Copy SHA-256", "Sign again", "Check again", "Sign 4 files…" };
            Assert.Equal(expected, stops);
        });
    }

    static string Describe(UIElement e)
    {
        var name = System.Windows.Automation.AutomationProperties.GetName(e);
        return e switch
        {
            ListBox => "File list",
            CheckBox when name == "Select all files to sign" => "Select all",
            Button { Name: "CertificateButton" } => "Certificate",
            Button b when !string.IsNullOrEmpty(name) => name,
            Button { Content: string text } => text,
            Button b => TextOf(b),
            _ => e.GetType().Name,
        };
    }

    static string TextOf(DependencyObject d)
    {
        if (d is TextBlock t && !string.IsNullOrEmpty(t.Text)) return t.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var r = TextOf(VisualTreeHelper.GetChild(d, i));
            if (r.Length > 0) return r;
        }
        return "";
    }

    /// <summary>Tab stops in document order, as WPF walks them when no TabIndex is set. The list is one stop.</summary>
    static IEnumerable<UIElement> TabStops(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is UIElement { Visibility: not Visibility.Visible }) continue;
            if (c is ListBox lb) { yield return lb; continue; }
            if (c is Control { Focusable: true, IsTabStop: true, IsEnabled: true } ctrl && c is not ScrollViewer && c is not ItemsControl && c is not UserControl)
                yield return ctrl;
            foreach (var d in TabStops(c)) yield return d;
        }
    }

    [Fact]
    public void Main_window_minimum_is_1024_by_640()
    {
        WpfHost.Run(() =>
        {
            // Constructing a Window creates no HWND; it is never shown.
            var w = new MainWindow();
            Assert.Equal(1024, w.MinWidth);
            Assert.Equal(640, w.MinHeight);
            Assert.Equal(1280, w.Width);
            Assert.Equal(800, w.Height);
            Assert.Equal("Sign my app", w.Title);
        });
    }

    [Fact]
    public void Compact_layout_below_the_large_size()
    {
        WpfHost.Run(() =>
        {
            var s = new Scenario();
            var view = new MainView { DataContext = s.Vm };
            view.UpdateCompact(new Size(1010, 601));
            Assert.True(s.Vm.IsCompact);
            view.UpdateCompact(new Size(1266, 761));
            Assert.False(s.Vm.IsCompact);
        });
    }

    [Fact]
    public void Backdrop_is_ink_at_32_percent_and_focus_ring_is_1_5_px()
    {
        WpfHost.Run(() =>
        {
            var c = (Color)Application.Current.FindResource("Color.Backdrop");
            Assert.Equal(Color.FromArgb(0x52, 0x11, 0x11, 0x12), c);
            Assert.Equal(1.5, (double)Application.Current.FindResource("Focus.Width"));
            Assert.Equal(new Thickness(-3.5), (Thickness)Application.Current.FindResource("Focus.Outset"));
        });
    }
}
