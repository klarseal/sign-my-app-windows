using System.IO;
using SignMyApp.App.Demo;
using SignMyApp.App.Services;
using SignMyApp.App.ViewModels;
using SignMyApp.Core;
using SignMyApp.Core.Tests;

namespace SignMyApp.App.Tests;

public sealed class CoreIntegrationTests
{
    [Fact]
    public void Real_composition_uses_Core_and_preferences_preserve_trust()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json");
        var services = CoreComposition.CreateReal(path);
        Assert.IsType<SignatureChecker>(services.Checker);
        Assert.IsType<CertificateFinder>(services.Finder);
        Assert.IsType<Signer>(services.Signer);
        Assert.IsType<ProjectTrustStore>(services.Trust);
        Assert.IsType<SettingsStore>(services.Settings);
        Assert.True(services.OffloadChecks);
        Assert.False(File.Exists(path));

        var thumb = new string('A', 40);
        services.Trust.Trust(temp.Path, thumb);
        var vm = new MainViewModel(services);
        vm.Settings.TimestampUrl = "https://timestamp.digicert.com";
        vm.Settings.Language = vm.Settings.Languages.Single(l => l.Code == "nb");
        var reopened = CoreComposition.CreateReal(path);
        Assert.True(reopened.Trust.IsTrusted(temp.Path, thumb));
        Assert.Equal("nb", reopened.Settings.Load().Language);
        Assert.Equal("https://timestamp.digicert.com", reopened.Settings.Load().TimestampUrl);

        vm.Settings.TrustRows.Single().ForgetCommand.Execute(null);
        Assert.Empty(reopened.Trust.List());
        Assert.Equal("nb", reopened.Settings.Load().Language);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("https://user:password@timestamp.example.com")]
    [InlineData("https://timestamp.example.com/?secret=x")]
    public void Settings_editor_uses_Core_timestamp_validation(string address)
    {
        using var temp = new TempFolder();
        var services = CoreComposition.CreateReal(temp.File("settings.json"));
        var vm = new MainViewModel(services);
        vm.Settings.TimestampUrl = address;
        Assert.True(vm.Settings.IsTimestampInvalid);
        Assert.Equal(new AppSettings().TimestampUrl, vm.Settings.EffectiveTimestampUrl);
        Assert.False(File.Exists(temp.File("settings.json")));
    }

    static MainViewModel ViewModel(ISignatureChecker checker)
    {
        var demo = new DemoServices(new DemoOptions { Certificates = DemoCertificates.None });
        return new MainViewModel(new AppServices
        {
            Checker = checker,
            Signer = new Signer(checker, new SignToolLocator()),
            Finder = demo.Finder,
            Trust = demo.Trust,
            Settings = demo.SettingsStore,
            Picker = demo.Picker,
            Shell = demo.Shell,
            SimplySign = demo.Services.SimplySign,
            ProjectFolderOf = p => p,
        });
    }

    [SignToolFact]
    public async Task Unsigned_apphost_then_PFX_signed_without_timestamp_is_shown_honestly()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var vm = ViewModel(new SignatureChecker());
        await vm.AddPathsAsync([temp.Path], replace: true);
        Assert.Equal(Text.StatusNotSigned, Assert.Single(vm.Rows).StatusText);

        // The public Signer intentionally has no PFX/password hook. Reuse Core's test-only helper.
        using var pfx = new TestPfxSigner();
        await pfx.SignAsync(path);
        await vm.CheckAgainCommand.RunAsync();
        Assert.Equal(Text.StatusCouldNotCheck, Assert.Single(vm.Rows).StatusText);
        Assert.Equal(FileState.Error, vm.Rows[0].Check!.State); // self-signed, never installed in a store

        var testTrustVm = ViewModel(Fixtures.TestTrustChecker());
        await testTrustVm.AddPathsAsync([temp.Path], replace: true);
        Assert.Equal(Text.StatusNoTimestamp, Assert.Single(testTrustVm.Rows).StatusText);
        Assert.Equal(pfx.Certificate.Thumbprint, testTrustVm.Rows[0].Check!.SignerThumbprint);
    }

    [NetworkFact]
    public async Task Unsigned_apphost_then_timestamped_PFX_signature_shows_Signed()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var vm = ViewModel(Fixtures.TestTrustChecker());
        await vm.AddPathsAsync([temp.Path], replace: true);
        Assert.Equal(Text.StatusNotSigned, Assert.Single(vm.Rows).StatusText);
        using var pfx = new TestPfxSigner();
        await pfx.SignAsync(path, timestamp: true);
        await vm.CheckAgainCommand.RunAsync();
        Assert.Equal(Text.StatusSigned, Assert.Single(vm.Rows).StatusText);
        Assert.Equal(Text.StatusSigned, vm.Detail!.Label);
        Assert.NotNull(vm.Rows[0].Check!.TimestampUtc);
        Assert.Equal(pfx.Certificate.Thumbprint, vm.Rows[0].Check!.SignerThumbprint);
    }
}
