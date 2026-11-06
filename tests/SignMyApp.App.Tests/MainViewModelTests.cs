using SignMyApp.App.Demo;
using SignMyApp.App.Services;
using SignMyApp.App.ViewModels;
using SignMyApp.Core;

namespace SignMyApp.App.Tests;

public class FirstRunTests
{
    [Fact]
    public async Task Starts_on_the_drop_zone_with_both_choices()
    {
        var s = await new Scenario().InitAsync();
        Assert.True(s.Vm.IsFirstRun);
        Assert.Equal("Drop files or a folder", s.Vm.DropText);
        Assert.True(s.Vm.ShowDropButtons);
        Assert.True(s.Vm.ChooseFilesCommand.CanExecute(null));
        Assert.True(s.Vm.ChooseFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dragging_over_says_release_and_hides_the_buttons()
    {
        var s = await new Scenario().InitAsync();
        s.Vm.DragEnter(true);
        Assert.Equal("Release to check", s.Vm.DropText);
        Assert.False(s.Vm.ShowDropButtons);
        s.Vm.DragLeave();
        Assert.Equal("Drop files or a folder", s.Vm.DropText);
    }

    [Fact]
    public async Task Dropping_the_wrong_type_stays_and_says_which_types()
    {
        var s = await new Scenario().InitAsync();
        await s.Vm.DropAsync([@"C:\build\MyApp\readme.txt"]);
        Assert.True(s.Vm.IsFirstRun);
        Assert.Equal("Only .exe, .dll, .msi", s.Vm.DropText);
        Assert.Equal(0, s.Demo.Checker.Calls);
    }

    [Fact]
    public async Task Choose_folder_checks_every_supported_file_with_the_installer_last()
    {
        var s = await new Scenario().InitAsync();
        await s.Vm.ChooseFolderCommand.RunAsync();
        Assert.True(s.Vm.IsList);
        Assert.Equal(DemoData.FileNames, s.Vm.Rows.Select(r => r.FileName));
        Assert.Equal("MyAppSetup.msi", s.Vm.Rows.Last().FileName);
        Assert.Equal(6, s.Demo.Checker.Calls);
    }
}

public class ResultsTests
{
    [Fact]
    public async Task Results_show_status_chips_ticks_and_footer()
    {
        var s = await new Scenario().ResultsAsync();
        var vm = s.Vm;
        Assert.Equal(ListMode.Results, vm.Mode);
        Assert.Equal("MyApp", vm.ProjectName);
        Assert.Equal(DemoData.Folder, vm.ProjectPath);
        Assert.Equal(DemoData.Project, vm.ProjectFolder);
        Assert.Equal(["3 not signed", "1 no timestamp", "1 changed", "1 signed"], vm.Chips.Select(c => c.Label));
        Assert.Equal([Tone.Action, Tone.Warn, Tone.Danger, Tone.Success], vm.Chips.Select(c => c.Tone));
        Assert.Equal(["Not signed", "Not signed", "No timestamp", "Signed", "Changed since signing", "Not signed"], vm.Rows.Select(r => r.StatusText));
        Assert.Equal([true, true, true, false, false, true], vm.Rows.Select(r => r.IsChecked));
        Assert.All(vm.Rows, r => Assert.True(r.IsCheckEnabled));
        Assert.Equal(["None", "None", "Example Software Ltd", "Contoso Ltd", "Example Software Ltd", "None"], vm.Rows.Select(r => r.PublisherText));
        Assert.Equal("4 of 6 selected", vm.FootNote);
        Assert.Equal("Sign 4 files\u2026", vm.PrimaryLabel);
        Assert.True(vm.PrimaryCommand.CanExecute(null));
        Assert.True(vm.ShowGhostCheckAgain);
        Assert.True(vm.AllChecked);
        Assert.Equal("Example Software Ltd", vm.CertName);
        Assert.Equal("SimplySign", vm.CertSourceText);
        Assert.Equal("Example Software Ltd, SimplySign. Change certificate", vm.CertAria);
        Assert.Same(vm.Rows[0], vm.SelectedRow);
    }

    [Fact]
    public async Task Status_is_always_a_dot_plus_words()
    {
        var s = await new Scenario().ResultsAsync();
        Assert.All(s.Vm.Rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.StatusText));
            Assert.True(r.ShowDot);
            Assert.NotEqual(Tone.None, r.Tone);
        });
        Assert.All(s.Vm.Chips, c => Assert.Matches(@"^\d+ \w", c.Label));
    }

    [Fact]
    public async Task One_file_ticked_says_Sign_1_file()
    {
        var s = await new Scenario().ResultsAsync();
        s.Vm.AllChecked = false;
        Assert.Equal("Sign 0 files\u2026", s.Vm.PrimaryLabel);
        Assert.False(s.Vm.PrimaryCommand.CanExecute(null));
        s.Row("Updater.exe").IsChecked = true;
        Assert.Equal("Sign 1 file\u2026", s.Vm.PrimaryLabel);
        Assert.Equal("1 of 6 selected", s.Vm.FootNote);
        Assert.Null(s.Vm.AllChecked);
        s.Vm.AllChecked = true;
        Assert.Equal("Sign 4 files\u2026", s.Vm.PrimaryLabel);
    }

    [Fact]
    public async Task Space_ticks_the_selected_file()
    {
        var s = await new Scenario().ResultsAsync();
        s.Vm.SelectedRow = s.Row("Contoso.Json.dll");
        Assert.False(s.Row("Contoso.Json.dll").IsChecked);
        s.Vm.ToggleSelectedCommand.Execute(null);
        Assert.True(s.Row("Contoso.Json.dll").IsChecked);
        Assert.Equal("Sign 5 files\u2026", s.Vm.PrimaryLabel);
    }

    [Fact]
    public async Task Files_signed_by_another_publisher_are_never_pre_ticked()
    {
        var s = new Scenario();
        s.Demo.Checker.Override("Updater.exe", DemoData.CheckOf(@"C:\x\Updater.exe") with { Publisher = "Contoso Ltd" });
        await s.ResultsAsync();
        Assert.Equal(RowKind.NoTimestamp, s.Row("Updater.exe").Kind);
        Assert.False(s.Row("Updater.exe").IsChecked);
        Assert.Equal("Sign 3 files\u2026", s.Vm.PrimaryLabel);
    }

    [Fact]
    public async Task Without_a_certificate_nothing_is_tickable_and_the_footer_offers_one()
    {
        var s = await new Scenario(DemoCertificates.None).ResultsAsync();
        var vm = s.Vm;
        Assert.False(vm.HasCertificate);
        Assert.Equal("No certificate", vm.CertName);
        Assert.Equal(Tone.Neutral, vm.CertTone);
        Assert.Equal("No certificate. Choose a certificate", vm.CertAria);
        Assert.All(vm.Rows, r => { Assert.False(r.IsCheckEnabled); Assert.False(r.IsChecked); });
        Assert.False(vm.IsSelectAllEnabled);
        Assert.True(vm.ShowFind);
        Assert.False(vm.ShowPrimary);
        Assert.Equal("", vm.FootNote);
        Assert.True(vm.ShowGhostCheckAgain);
        Assert.Equal("", vm.Detail!.ActionLabel);
        vm.GetCertificateCommand.Execute(null);
        Assert.Equal(["https://klarseal.com/code-signing/"], s.Demo.Shell.Opened);
    }

    [Fact]
    public async Task Check_again_checks_every_file_again()
    {
        var s = await new Scenario().ResultsAsync();
        await s.Vm.CheckAgainCommand.RunAsync();
        Assert.Equal(12, s.Demo.Checker.Calls);
        Assert.Equal(ListMode.Results, s.Vm.Mode);
    }

    [Fact]
    public async Task Add_files_adds_without_duplicates()
    {
        var s = await new Scenario().ResultsAsync();
        s.Demo.Picker.Files = [System.IO.Path.Combine(DemoData.Folder, "Extra.dll"), System.IO.Path.Combine(DemoData.Folder, "MyApp.exe")];
        await s.Vm.AddFilesCommand.RunAsync();
        Assert.Equal(7, s.Vm.Rows.Count);
        Assert.Equal("Extra.dll", s.Vm.Rows[5].FileName);
        Assert.Equal("MyAppSetup.msi", s.Vm.Rows[6].FileName);
    }
}

public class CheckingTests
{
    [Fact]
    public async Task While_checking_the_list_is_locked_and_counts_up()
    {
        var s = await new Scenario().InitAsync();
        s.Demo.Checker.Hold("Contoso.Json.dll");
        s.Demo.Checker.Hold("Legacy.Plugin.dll");
        var running = s.Vm.ChooseFilesCommand.RunAsync();
        var vm = s.Vm;
        Assert.Equal(ListMode.Checking, vm.Mode);
        Assert.Equal(["3 of 6 checked"], vm.Chips.Select(c => c.Label));
        Assert.Equal(["Not signed", "Not signed", "No timestamp", "Checking\u2026", "Checking\u2026", "Waiting"], vm.Rows.Select(r => r.StatusText));
        Assert.True(s.Row("Contoso.Json.dll").ShowSpinner);
        Assert.False(s.Row("Contoso.Json.dll").ShowDot);
        Assert.Equal(Tone.Neutral, s.Row("MyAppSetup.msi").Tone);
        Assert.All(vm.Rows, r => Assert.False(r.IsCheckEnabled));
        Assert.Equal("Sign files\u2026", vm.PrimaryLabel);
        Assert.False(vm.PrimaryCommand.CanExecute(null));
        Assert.False(vm.ShowGhostCheckAgain);
        Assert.Equal("", vm.FootNote);

        vm.SelectedRow = s.Row("Contoso.Json.dll");
        Assert.Equal("Checking\u2026", vm.Detail!.Label);
        Assert.True(vm.Detail.ShowSpinner);
        Assert.All(vm.Detail.Facts, f => Assert.True(f.IsBar));
        Assert.Equal(["Publisher", "Issued by", "Valid", "Timestamp"], vm.Detail.Facts.Select(f => f.Key));

        s.Demo.Checker.Release("Contoso.Json.dll");
        s.Demo.Checker.Release("Legacy.Plugin.dll");
        await running;
        Assert.Equal(ListMode.Results, vm.Mode);
        Assert.Equal("Signed", vm.Detail!.Label);
    }
}

public class CertificateDialogTests
{
    [Fact]
    public async Task Several_lists_usable_first_with_the_expired_one_locked()
    {
        var s = await new Scenario().ResultsAsync();
        await s.Vm.OpenCertificatesCommand.RunAsync();
        var d = s.Vm.CertDialog!;
        Assert.Equal(Overlay.Certificates, s.Vm.Overlay);
        Assert.Equal(CertDialogKind.Several, d.Kind);
        Assert.Equal(3, d.Rows.Count);
        Assert.Equal(["Until 1 Jan 2028", "Until 18 Aug 2027", "Expired 30 Sep 2026"], d.Rows.Select(r => r.ValidText));
        Assert.Equal(["\u00b7 SimplySign", "\u00b7 Certificate store", "\u00b7 USB token"], d.Rows.Select(r => r.SourceText));
        Assert.Equal(["3A7F\u20265A6F", "9D21\u2026C2B4", "51E8\u20265D18"], d.Rows.Select(r => r.ThumbShort));
        Assert.Equal("Thumbprint 3A7F0B9CE412D866AA015FB39E74C2D0B81E5A6F", d.Rows[0].ThumbTooltip);
        Assert.True(d.Rows[0].IsSelected);
        Assert.False(d.Rows[2].IsUsable);
        Assert.True(d.ShowRefreshGhost);
        Assert.True(d.ShowUse);
        Assert.True(d.UseCommand.CanExecute(null));
    }

    [Fact]
    public async Task Use_this_certificate_switches_and_closes()
    {
        var s = await new Scenario().ResultsAsync();
        await s.Vm.OpenCertificatesCommand.RunAsync();
        var d = s.Vm.CertDialog!;
        d.SelectRowCommand.Execute(d.Rows[1]);
        d.Select(d.Rows[2]); // expired: ignored
        Assert.True(d.Rows[1].IsSelected);
        d.UseCommand.Execute(null);
        Assert.Equal(Overlay.None, s.Vm.Overlay);
        Assert.Equal(DemoData.Store.Thumbprint, s.Vm.Certificate!.Thumbprint);
        Assert.Equal("Certificate store", s.Vm.CertSourceText);
    }

    [Fact]
    public async Task Cancel_keeps_the_certificate()
    {
        var s = await new Scenario().ResultsAsync();
        await s.Vm.OpenCertificatesCommand.RunAsync();
        s.Vm.CertDialog!.CancelCommand.Execute(null);
        Assert.Equal(Overlay.None, s.Vm.Overlay);
        Assert.Equal(DemoData.SimplySign.Thumbprint, s.Vm.Certificate!.Thumbprint);
    }

    [Theory]
    [InlineData(DemoCertificates.None, CertDialogKind.None)]
    [InlineData(DemoCertificates.LoggedOut, CertDialogKind.LoggedOut)]
    [InlineData(DemoCertificates.Expired, CertDialogKind.Expired)]
    public async Task The_other_states(DemoCertificates certs, CertDialogKind kind)
    {
        var s = await new Scenario(certs).ResultsAsync();
        await s.Vm.OpenCertificatesCommand.RunAsync();
        var d = s.Vm.CertDialog!;
        Assert.Equal(kind, d.Kind);
        Assert.Equal(kind == CertDialogKind.Expired, d.ShowList);
        Assert.Equal(kind != CertDialogKind.Expired, d.ShowRefreshPrimary);
        Assert.False(d.UseCommand.CanExecute(null));
        Assert.False(s.Vm.HasCertificate);
    }

    [Fact]
    public async Task Refresh_finds_again_and_says_so_while_busy()
    {
        var s = await new Scenario().ResultsAsync();
        await s.Vm.OpenCertificatesCommand.RunAsync();
        int before = s.Demo.Finder.Calls;
        Assert.Equal("Refresh", s.Vm.CertDialog!.RefreshLabel);
        await s.Vm.CertDialog.RefreshCommand.RunAsync();
        Assert.Equal(before + 1, s.Demo.Finder.Calls);
    }

    [Fact]
    public void Words_never_ask_for_secrets_and_never_name_the_CA()
    {
        var fields = typeof(Text).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Contains("We never ask for passwords, PINs or codes.", fields);
        Assert.DoesNotContain(fields, f => f.Contains("Certum", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, f => f.Contains("price", StringComparison.OrdinalIgnoreCase) || f.Contains("\u20ac") || f.Contains("EUR"));
    }
}

public class SettingsTests
{
    [Fact]
    public async Task Lists_trusted_projects_with_publisher_and_source()
    {
        var s = await new Scenario(trustThisProject: true).InitAsync();
        s.Vm.OpenSettingsCommand.Execute(null);
        Assert.True(s.Vm.IsSettings);
        var rows = s.Vm.Settings.TrustRows;
        Assert.Equal([DemoData.Project, DemoData.OtherProject], rows.Select(r => r.Folder));
        Assert.Equal(["Example Software Ltd \u00b7 SimplySign", "Example Software Ltd \u00b7 Certificate store"], rows.Select(r => r.Line));
        s.Vm.BackCommand.Execute(null);
        Assert.True(s.Vm.IsFirstRun);
    }

    [Fact]
    public async Task Forget_asks_first_and_Keep_keeps()
    {
        var s = await new Scenario(trustThisProject: true).InitAsync();
        s.Vm.OpenSettingsCommand.Execute(null);
        var row = s.Vm.Settings.TrustRows[1];
        row.AskCommand.Execute(null);
        Assert.True(row.IsConfirming);
        Assert.Equal(@"Forget C:\work\Tools", row.GroupLabel);
        row.KeepCommand.Execute(null);
        Assert.False(row.IsConfirming);
        Assert.Equal(2, s.Demo.Trust.List().Count);

        row.AskCommand.Execute(null);
        row.ForgetCommand.Execute(null);
        Assert.Single(s.Vm.Settings.TrustRows);
        Assert.False(s.Demo.Trust.IsTrusted(DemoData.OtherProject, DemoData.Store.Thumbprint));
    }

    [Fact]
    public async Task Timestamp_server_is_validated_saved_and_reset()
    {
        var s = await new Scenario().InitAsync();
        var set = s.Vm.Settings;
        Assert.Equal("http://time.certum.pl", set.TimestampUrl);
        set.TimestampUrl = "ftp://192.168.1.4";
        Assert.True(set.IsTimestampInvalid);
        Assert.Equal(0, s.Demo.SettingsStore.Saves);
        set.TimestampUrl = "https://timestamp.digicert.com";
        Assert.False(set.IsTimestampInvalid);
        Assert.Equal("https://timestamp.digicert.com", s.Demo.SettingsStore.Current.TimestampUrl);
        set.UseDefaultCommand.Execute(null);
        Assert.Equal("http://time.certum.pl", s.Demo.SettingsStore.Current.TimestampUrl);
    }

    [Fact]
    public async Task Language_is_saved()
    {
        var s = await new Scenario().InitAsync();
        var set = s.Vm.Settings;
        Assert.Equal(["English", "Deutsch", "Norsk bokm\u00e5l"], set.Languages.Select(l => l.Name));
        set.Language = set.Languages[1];
        Assert.Equal("de", s.Demo.SettingsStore.Current.Language);
        Assert.Equal("Sign my app 1.0.0 \u00b7 Klarseal \u00b7 No account, no telemetry", set.VersionLine);
    }

    [Fact]
    public async Task Signing_uses_the_saved_timestamp_server()
    {
        var s = await new Scenario().ResultsAsync();
        s.Vm.Settings.TimestampUrl = "https://timestamp.digicert.com";
        s.Row("MyAppSetup.msi").IsChecked = false;
        await s.Vm.PrimaryCommand.RunAsync();
        Assert.Equal("https://timestamp.digicert.com", s.Vm.SignDialog!.TimestampUrl);
        await s.Vm.SignDialog.SignOnceCommand.RunAsync();
        Assert.Equal("https://timestamp.digicert.com", s.Demo.Signer.Plans.Single().TimestampUrl);
    }
}

public class FileSetTests
{
    [Fact]
    public void Common_folder_and_names()
    {
        Assert.Equal(@"C:\build\MyApp\release", FileSet.CommonFolder(DemoData.Paths));
        Assert.Equal(@"C:\build", FileSet.CommonFolder([@"C:\build\a\x.exe", @"C:\build\b\y.dll"]));
        Assert.Equal(@"C:\", FileSet.CommonFolder([@"C:\a\x.exe", @"C:\b\y.dll"]));
        Assert.Equal("MyApp", FileSet.FolderName(@"C:\build\MyApp"));
    }

    [Fact]
    public void Project_folder_walks_up_to_a_project_marker()
    {
        Assert.Equal(@"C:\build\MyApp", FileSet.ProjectFolder(@"C:\build\MyApp\bin\Release", d => d == @"C:\build\MyApp"));
        Assert.Equal(@"C:\loose", FileSet.ProjectFolder(@"C:\loose", _ => false));
    }

    [Fact]
    public void Only_exe_dll_msi_and_installers_last()
    {
        var (files, unsupported) = FileSet.Expand([@"C:\a\Setup.msi", @"C:\a\app.exe", @"C:\a\notes.txt", @"C:\a\lib.DLL"], _ => false);
        Assert.Equal([@"C:\a\app.exe", @"C:\a\lib.DLL", @"C:\a\Setup.msi"], files);
        Assert.True(unsupported);
    }
}

public class FormattingTests
{
    [Fact]
    public void Dates_hashes_and_plurals_match_the_sample_data()
    {
        Assert.Equal("2 Oct 2026 \u2013 1 Jan 2028", Text.Range(DemoData.ValidFrom, DemoData.ValidTo));
        Assert.Equal("3 Feb 2026, 09:14 UTC", Text.Stamp(new DateTimeOffset(2026, 2, 3, 9, 14, 0, TimeSpan.Zero)));
        Assert.Equal("a3f19c0e5b7d\u2026e0f1a2b3c4".Length, Text.ShortSha12(DemoData.Sha["MyApp.exe"]).Length - 2);
        Assert.Equal("a3f19c0e5b7d\u2026d6e0f1a2b3c4", Text.ShortSha12(DemoData.Sha["MyApp.exe"]));
        Assert.Equal("a3f19c0e\u2026f1a2b3c4", Text.ShortSha8(DemoData.Sha["MyApp.exe"]));
        Assert.Equal("1 file signed", Text.SignedTitle(1));
        Assert.Equal("Sign 3 files", Text.SignWindowTitle(3));
        Assert.Equal("SimplySign \u00b7 until 1 Jan 2028", Text.CertificateLine(DemoData.SimplySign));
    }
}
