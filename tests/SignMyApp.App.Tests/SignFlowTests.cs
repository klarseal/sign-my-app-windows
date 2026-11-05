using SignMyApp.App.Demo;
using SignMyApp.App.ViewModels;
using SignMyApp.Core;

namespace SignMyApp.App.Tests;

public class DetailPanelTests
{
    [Fact]
    public async Task Every_state_has_one_line_and_its_next_step()
    {
        var s = await new Scenario().ResultsAsync();
        DetailViewModel D(string name) { s.Vm.SelectedRow = s.Row(name); return s.Vm.Detail!; }

        var un = D("MyApp.exe");
        Assert.Equal("Not signed", un.Label);
        Assert.Equal("Windows shows \u201cUnknown publisher\u201d.", un.Line);
        Assert.False(un.HasWhy);
        Assert.Empty(un.Facts);
        Assert.Equal("Sign it", un.ActionLabel);
        Assert.Equal("a3f19c0e5b7d\u2026d6e0f1a2b3c4", un.ShaShort);

        var nt = D("Updater.exe");
        Assert.Equal("No timestamp", nt.Label);
        Assert.Equal(Tone.Warn, nt.Tone);
        Assert.Equal("Signature valid until 1 Jan 2028.", nt.Line);
        Assert.Equal("The certificate expires on 1 Jan 2028. A timestamp proves the file was signed before that, so the signature stays valid.", nt.WhyText);
        Assert.Equal(["Publisher", "Issued by", "Valid", "Timestamp"], nt.Facts.Select(f => f.Key));
        Assert.Equal(["Example Software Ltd", "Example Code Signing CA", "2 Oct 2026 \u2013 1 Jan 2028", "None"], nt.Facts.Select(f => f.Value));
        Assert.Equal("Sign again", nt.ActionLabel);
        Assert.False(nt.IsWhyOpen);
        nt.ToggleWhyCommand.Execute(null);
        Assert.True(nt.IsWhyOpen);

        var good = D("Contoso.Json.dll");
        Assert.Equal("Signed", good.Label);
        Assert.Equal("Nothing to do.", good.Line);
        Assert.Equal(["Contoso Ltd", "Example Issuing CA 2", "12 Jun 2025 \u2013 11 Jun 2026", "3 Feb 2026, 09:14 UTC"], good.Facts.Select(f => f.Value));
        Assert.False(good.HasAction);

        var ch = D("Legacy.Plugin.dll");
        Assert.Equal("Changed since signing", ch.Label);
        Assert.Equal(Tone.Danger, ch.Tone);
        Assert.Equal("Windows treats it as not signed.", ch.Line);
        Assert.Equal("The file no longer matches its signature. If you built it, sign it again. If not, get a fresh copy.", ch.WhyText);
        Assert.Equal(["Publisher", "Issued by", "Timestamp"], ch.Facts.Select(f => f.Key));
        Assert.Equal("14 Oct 2026, 11:02 UTC", ch.Facts[2].Value);
        Assert.Equal("Sign again", ch.ActionLabel);
    }

    [Fact]
    public async Task Why_closes_again_when_another_file_is_picked()
    {
        var s = await new Scenario().ResultsAsync();
        s.Vm.SelectedRow = s.Row("Updater.exe");
        s.Vm.Detail!.ToggleWhyCommand.Execute(null);
        s.Vm.SelectedRow = s.Row("Legacy.Plugin.dll");
        Assert.False(s.Vm.Detail!.IsWhyOpen);
    }

    [Fact]
    public async Task Copy_puts_the_full_SHA_256_on_the_clipboard()
    {
        var s = await new Scenario().ResultsAsync();
        s.Vm.Detail!.CopyShaCommand.Execute(null);
        Assert.Equal([DemoData.Sha["MyApp.exe"]], s.Demo.Shell.Copied);
    }

    [Fact]
    public async Task Sign_it_opens_the_Sign_window_for_that_one_file()
    {
        var s = await new Scenario().ResultsAsync();
        s.Vm.SelectedRow = s.Row("Legacy.Plugin.dll");
        await s.Vm.SignSelectedRowCommand.RunAsync();
        var d = s.Vm.SignDialog!;
        Assert.Equal(SignKind.Review, d.Kind);
        Assert.Equal("Sign 1 file", d.Title);
        Assert.Equal(["Legacy.Plugin.dll"], d.Files.Select(f => f.Name));
    }
}

public class SignWindowTests
{
    [Fact]
    public async Task Review_shows_files_short_hashes_and_what_will_be_used()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        Assert.Equal(Overlay.Sign, s.Vm.Overlay);
        Assert.Equal(SignKind.Review, d.Kind);
        Assert.Equal("Sign 3 files", d.Title);
        Assert.Equal(["MyApp.exe", "MyApp.Core.dll", "Updater.exe"], d.Files.Select(f => f.Name));
        Assert.Equal(["a3f19c0e\u2026f1a2b3c4", "7e04b1d9\u20266f2a9b14", "2c9d5e0a\u2026e2a0f9b7"], d.Files.Select(f => f.ShaShort));
        Assert.All(d.Files, f => { Assert.True(f.ShowShortSha); Assert.False(f.ShowFullSha); });
        Assert.Equal(["Publisher", "Certificate", "Timestamp", "Project"], d.Details.Select(x => x.Key));
        Assert.Equal(["Example Software Ltd", "SimplySign \u00b7 until 1 Jan 2028", "http://time.certum.pl", @"C:\build\MyApp"], d.Details.Select(x => x.Value));
        Assert.True(d.Details[3].IsMono);
        Assert.Equal("", d.Status);
        Assert.True(d.CancelCommand.CanExecute(null));
        Assert.True(d.SignOnceCommand.CanExecute(null));
        Assert.True(d.AlwaysCommand.CanExecute(null));
        Assert.Empty(s.Demo.Signer.Plans);
    }

    [Fact]
    public async Task Full_SHA_256_and_Always_are_on_demand()
    {
        var d = await new Scenario().ReviewAsync(withMsi: false);
        Assert.Equal("Show full SHA-256", d.FullLabel);
        d.ToggleFullCommand.Execute(null);
        Assert.Equal("Hide full SHA-256", d.FullLabel);
        Assert.All(d.Files, f => { Assert.False(f.ShowShortSha); Assert.True(f.ShowFullSha); });
        Assert.False(d.AlwaysOpen);
        d.ToggleAlwaysCommand.Execute(null);
        Assert.True(d.AlwaysOpen);
    }

    [Fact]
    public async Task Installer_with_app_files_is_held_back_and_ordered()
    {
        var d = await new Scenario().ReviewAsync(withMsi: true);
        Assert.Equal(SignKind.Installer, d.Kind);
        Assert.Equal("Sign 4 files", d.Title);
        Assert.Equal("Now: 3 app files", d.Step1);
        Assert.Equal("Then: rebuild MyAppSetup.msi", d.Step2);
        Assert.Equal("Last: sign MyAppSetup.msi", d.Step3);
        Assert.Equal(["MyApp.exe", "MyApp.Core.dll", "Updater.exe"], d.AppRows.Select(r => r.Name));
        Assert.False(d.ShowFiles);
        Assert.True(d.ShowDetails);
    }

    [Fact]
    public async Task Cancel_and_Esc_never_sign()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        Assert.True(d.Escape());
        Assert.Equal(Overlay.None, s.Vm.Overlay);
        Assert.Empty(s.Demo.Signer.Plans);
        Assert.Empty(s.Demo.Signer.SignedPaths);

        await s.Vm.PrimaryCommand.RunAsync();
        s.Vm.SignDialog!.CancelCommand.Execute(null);
        Assert.Equal(Overlay.None, s.Vm.Overlay);
        Assert.Empty(s.Demo.Signer.Plans);
        Assert.False(s.Demo.Trust.IsTrusted(DemoData.Project, DemoData.SimplySign.Thumbprint));
    }

    [Fact]
    public async Task Sign_once_signs_the_app_files_then_says_rebuild()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: true);
        await d.SignOnceCommand.RunAsync();
        Assert.Equal(SignKind.Done, d.Kind);
        Assert.Equal("3 files signed", d.Title);
        Assert.Equal(Tone.Success, d.HeadTone);
        Assert.Equal("Signature and timestamp checked.", d.Line);
        Assert.Equal("Next: rebuild MyAppSetup.msi", d.Status);
        Assert.Equal(["Signed", "Signed", "Signed"], d.Files.Select(f => f.StateText));
        Assert.Equal(3, s.Demo.Signer.SignedPaths.Count);
        Assert.DoesNotContain(s.Demo.Signer.SignedPaths, p => p.EndsWith(".msi"));
        var plan = s.Demo.Signer.Plans.Single();
        Assert.Equal(DemoData.Project, plan.ProjectFolder);
        Assert.Equal(DemoData.SimplySign.Thumbprint, plan.Certificate.Thumbprint);
        Assert.Equal(64, plan.PlanHash.Length);
        Assert.Equal(DemoData.Sha["MyApp.Core.dll"], plan.Files[1].Sha256);
        Assert.False(s.Demo.Trust.IsTrusted(DemoData.Project, DemoData.SimplySign.Thumbprint));

        d.BackToFilesCommand.Execute(null);
        var vm = s.Vm;
        Assert.Equal(Overlay.None, vm.Overlay);
        Assert.Equal(ListMode.AfterSigning, vm.Mode);
        Assert.Equal(["Signed", "Signed", "Signed", "Signed", "Changed since signing", "Rebuild, then check"], vm.Rows.Select(r => r.StatusText));
        Assert.Equal(["4 signed", "1 changed", "1 to rebuild"], vm.Chips.Select(c => c.Label));
        Assert.Equal("Check again", vm.PrimaryLabel);
        Assert.False(vm.ShowGhostCheckAgain);
        Assert.Equal("", vm.FootNote);
        Assert.All(vm.Rows, r => Assert.False(r.IsChecked));

        Assert.Equal("Signature and timestamp checked.", vm.Detail!.Line);
        Assert.Equal("5 Nov 2026, 14:32 UTC", vm.Detail.Facts.Single(f => f.Key == "Timestamp").Value);
        vm.SelectedRow = s.Row("MyAppSetup.msi");
        Assert.Equal("Not signed yet", vm.Detail!.Label);
        Assert.Equal("Rebuild it with the signed files.", vm.Detail.Line);
        Assert.Equal(["Sign app files", "Rebuild the installer", "Check again, then sign it"], vm.Detail.Steps.Select(x => x.Label));

        await vm.PrimaryCommand.RunAsync();
        Assert.Equal(ListMode.Results, vm.Mode);
    }

    [Fact]
    public async Task Always_remembers_the_project_and_next_time_skips_the_click()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        await d.AlwaysCommand.RunAsync();
        Assert.True(s.Demo.Trust.IsTrusted(DemoData.Project, DemoData.SimplySign.Thumbprint));
        Assert.Equal(SignKind.Done, d.Kind);
        d.BackToFilesCommand.Execute(null);
        Assert.Contains(s.Vm.Settings.TrustRows, r => r.Folder == DemoData.Project);

        s.Vm.SelectedRow = s.Row("Legacy.Plugin.dll");
        await s.Vm.SignSelectedRowCommand.RunAsync();
        var again = s.Vm.SignDialog!;
        Assert.Equal(SignKind.Done, again.Kind);
        Assert.Equal(2, s.Demo.Signer.Plans.Count);
        Assert.Equal(["Legacy.Plugin.dll"], s.Demo.Signer.Plans[1].Files.Select(f => f.FileName));
    }

    [Fact]
    public async Task Progress_cannot_be_cancelled_and_stops_after_the_current_file()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        s.Demo.Signer.Hold("MyApp.Core.dll");
        var signing = d.SignOnceCommand.RunAsync();
        Assert.Equal(SignKind.Progress, d.Kind);
        Assert.Equal("Signing 3 files", d.Title);
        Assert.Equal("Keep SimplySign Desktop open.", d.Line);
        Assert.Equal("1 of 3", d.Status);
        Assert.Equal(["Signed", "Signing\u2026", "Waiting"], d.Files.Select(f => f.StateText));
        Assert.True(d.Files[1].Spin);
        Assert.InRange(d.ProgressValue, 0.46, 0.47);
        Assert.False(d.CancelCommand.CanExecute(null));
        Assert.True(d.Escape());
        Assert.Equal(Overlay.Sign, s.Vm.Overlay);

        d.StopCommand.Execute(null);
        s.Demo.Signer.Release("MyApp.Core.dll");
        await signing;
        Assert.Equal(SignKind.Done, d.Kind);
        Assert.Equal("2 files signed", d.Title);
        Assert.Equal(2, s.Demo.Signer.SignedPaths.Count);
    }

    [Fact]
    public async Task Session_closed_signs_nothing_and_sign_again_finishes()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        s.Demo.Signer.FailOnce("MyApp.exe", SignOutcome.SessionClosed);
        await d.SignOnceCommand.RunAsync();
        Assert.Equal(SignKind.Error, d.Kind);
        Assert.Equal("Certificate session closed", d.Title);
        Assert.Equal(Tone.Action, d.HeadTone);
        Assert.Equal("Log in to SimplySign Desktop, then sign again.", d.Fix);
        Assert.Equal("Nothing signed", d.Status);
        Assert.Equal("Cancel", d.ErrorSecondaryLabel);
        Assert.Equal("Sign again", d.ErrorPrimaryLabel);
        await d.ErrorPrimaryCommand.RunAsync();
        Assert.Equal(SignKind.Done, d.Kind);
        Assert.Equal("3 files signed", d.Title);
    }

    [Fact]
    public async Task Timestamp_unreachable()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        s.Demo.Signer.FailOnce("MyApp.exe", SignOutcome.TimestampUnreachable);
        await d.SignOnceCommand.RunAsync();
        Assert.Equal("Timestamp server not reachable", d.Title);
        Assert.Equal(Tone.Warn, d.HeadTone);
        Assert.Equal("Check your connection, then try again.", d.Fix);
        Assert.Equal("Nothing signed", d.Status);
        Assert.Equal(("Cancel", "Try again"), (d.ErrorSecondaryLabel, d.ErrorPrimaryLabel));
    }

    [Fact]
    public async Task File_changed_after_review_asks_for_a_new_review()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        s.Demo.Signer.FailOnce("MyApp.Core.dll", SignOutcome.FileChangedAfterReview);
        await d.SignOnceCommand.RunAsync();
        Assert.Equal("MyApp.Core.dll changed after review", d.Title);
        Assert.Equal("Review the files again.", d.Fix);
        Assert.Equal("1 of 3 signed", d.Status);
        Assert.Equal(("Close", "Review again"), (d.ErrorSecondaryLabel, d.ErrorPrimaryLabel));
        int checks = s.Demo.Checker.Calls;
        await d.ErrorPrimaryCommand.RunAsync();
        Assert.Equal(SignKind.Review, d.Kind);
        Assert.Equal(checks + 2, s.Demo.Checker.Calls);
        Assert.Equal(["MyApp.Core.dll", "Updater.exe"], d.Files.Select(f => f.Name));
    }

    [Fact]
    public async Task File_in_use_and_closing_keeps_what_was_signed()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        s.Demo.Signer.FailOnce("Updater.exe", SignOutcome.FileInUse);
        await d.SignOnceCommand.RunAsync();
        Assert.Equal("Updater.exe is in use", d.Title);
        Assert.Equal(Tone.Action, d.HeadTone);
        Assert.Equal("Close the program using it, then try again.", d.Fix);
        Assert.Equal("2 of 3 signed", d.Status);
        Assert.Equal(("Close", "Try again"), (d.ErrorSecondaryLabel, d.ErrorPrimaryLabel));
        d.CancelCommand.Execute(null);
        Assert.Equal(ListMode.AfterSigning, s.Vm.Mode);
        Assert.Equal(RowKind.JustSigned, s.Row("MyApp.exe").Kind);
        Assert.Equal(RowKind.NoTimestamp, s.Row("Updater.exe").Kind);
    }

    [Fact]
    public async Task Without_a_certificate_signing_opens_the_certificate_dialog()
    {
        var s = await new Scenario(DemoCertificates.None).ResultsAsync();
        await s.Vm.OpenSignAsync(Scenario.Checks("MyApp.exe"));
        Assert.Equal(Overlay.Certificates, s.Vm.Overlay);
        Assert.Equal(CertDialogKind.None, s.Vm.CertDialog!.Kind);
    }

    [Fact]
    public async Task Shortcuts_are_off_while_a_dialog_is_open()
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        int checks = s.Demo.Checker.Calls;
        s.Demo.Picker.Files = [System.IO.Path.Combine(DemoData.Folder, "Extra.dll")];
        await s.Vm.AddFilesCommand.RunAsync();     // Ctrl+O
        await s.Vm.CheckAgainCommand.RunAsync();   // F5
        await s.Vm.PrimaryCommand.RunAsync();
        s.Vm.OpenSettingsCommand.Execute(null);
        await s.Vm.OpenCertificatesCommand.RunAsync();
        Assert.Equal(checks, s.Demo.Checker.Calls);
        Assert.Equal(6, s.Vm.Rows.Count);
        Assert.True(s.Vm.IsList);
        Assert.Equal(Overlay.Sign, s.Vm.Overlay);
        Assert.Same(d, s.Vm.SignDialog);
    }
}
