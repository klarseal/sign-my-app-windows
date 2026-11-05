using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SignMyApp.Core;

namespace SignMyApp.App.Services;

public sealed class WpfFilePicker : IFilePicker
{
    public IReadOnlyList<string> PickFiles()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            Filter = ".exe, .dll, .msi|*.exe;*.dll;*.msi",
        };
        return dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FileNames : [];
    }

    public string? PickFolder()
    {
        var dlg = new OpenFolderDialog { Multiselect = false };
        return dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FolderName : null;
    }
}

public sealed class WindowsShell : IShell
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return;
        Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    public void ShowInFolder(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + path }, UseShellExecute = false });
        else if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
    }

    public void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy: nothing to do */ }
    }
}

public sealed class ProcessSimplySignProbe : ISimplySignProbe
{
    public bool IsDesktopRunning()
    {
        try
        {
            return Process.GetProcesses().Any(p =>
            {
                try { return p.ProcessName.StartsWith("SimplySign", StringComparison.OrdinalIgnoreCase); }
                catch (InvalidOperationException) { return false; }
                finally { p.Dispose(); }
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
}

/// <summary>Constructs the engine and the shared settings/trust stores directly.</summary>
public static class CoreComposition
{
    public static AppServices CreateReal() => CreateReal(SettingsStore.DefaultPath);

    internal static AppServices CreateReal(string settingsPath)
    {
        var checker = new SignatureChecker();
        var locator = new SignToolLocator();
        return new AppServices
        {
            Checker = checker,
            Finder = new CertificateFinder(),
            Signer = new Signer(checker, locator),
            Trust = new ProjectTrustStore(settingsPath),
            Settings = new SettingsStore(settingsPath),
            Picker = new WpfFilePicker(),
            Shell = new WindowsShell(),
            SimplySign = new ProcessSimplySignProbe(),
            OffloadChecks = true,
            Version = AppVersion(),
        };
    }

    public static string AppVersion()
    {
        var v = typeof(CoreComposition).Assembly.GetName().Version;
        return v is null ? "1.0.0" : v.Major + "." + v.Minor + "." + v.Build;
    }
}
