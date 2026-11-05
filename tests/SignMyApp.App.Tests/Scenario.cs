using SignMyApp.App.Demo;
using SignMyApp.App.ViewModels;
using SignMyApp.Core;

namespace SignMyApp.App.Tests;

/// <summary>A main view model on the fake services, driven to a canvas state.</summary>
public sealed class Scenario
{
    public Scenario(DemoCertificates certs = DemoCertificates.Several, bool trustOtherProject = true, bool trustThisProject = false)
    {
        var options = new DemoOptions { Certificates = certs };
        if (trustThisProject) options.Trusted.Add((DemoData.Project, DemoData.SimplySign.Thumbprint));
        if (trustOtherProject) options.Trusted.Add((DemoData.OtherProject, DemoData.Store.Thumbprint));
        Demo = new DemoServices(options);
        Vm = new MainViewModel(Demo.Services);
    }

    public DemoServices Demo { get; }
    public MainViewModel Vm { get; }

    public FileRowViewModel Row(string name) => Vm.Rows.Single(r => r.FileName == name);

    public async Task<Scenario> InitAsync()
    {
        await Vm.InitializeAsync();
        return this;
    }

    /// <summary>1.3: the six files checked, certificate found.</summary>
    public async Task<Scenario> ResultsAsync()
    {
        await InitAsync();
        await Vm.ChooseFilesCommand.RunAsync();
        return this;
    }

    /// <summary>3.1 / 3.2: the Sign window on the ticked files; withMsi=false unticks the installer first (3.1).</summary>
    public async Task<SignDialogViewModel> ReviewAsync(bool withMsi)
    {
        await ResultsAsync();
        if (!withMsi) Row("MyAppSetup.msi").IsChecked = false;
        await Vm.PrimaryCommand.RunAsync();
        return Vm.SignDialog!;
    }

    public static IReadOnlyList<FileCheck> Checks(params string[] names) => names.Select(n => DemoData.CheckOf(System.IO.Path.Combine(DemoData.Folder, n))).ToList();
}
