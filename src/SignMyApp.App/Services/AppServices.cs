using SignMyApp.Core;

namespace SignMyApp.App.Services;

/// <summary>Opens the system file and folder pickers. Fakes return fixed paths.</summary>
public interface IFilePicker
{
    IReadOnlyList<string> PickFiles();
    string? PickFolder();
}

/// <summary>Hands things to Windows: a link to the browser, a folder to Explorer, text to the clipboard.</summary>
public interface IShell
{
    void OpenUrl(string url);
    void ShowInFolder(string path);
    void CopyText(string text);
}

/// <summary>Is SimplySign Desktop running? Used only to tell "logged out" from "none found".</summary>
public interface ISimplySignProbe
{
    bool IsDesktopRunning();
}

/// <summary>Everything the view models depend on. Real or fake, chosen at start-up.</summary>
public sealed class AppServices
{
    public required ISignatureChecker Checker { get; init; }
    public required ICertificateFinder Finder { get; init; }
    public required ISigner Signer { get; init; }
    public required IProjectTrustStore Trust { get; init; }
    public required ISettingsStore Settings { get; init; }
    public required IFilePicker Picker { get; init; }
    public required IShell Shell { get; init; }
    public required ISimplySignProbe SimplySign { get; init; }

    /// <summary>
    /// True in the real app: checks run on the thread pool so a slow WinVerifyTrust never blocks the window.
    /// Fakes complete synchronously and tests keep this false.
    /// </summary>
    public bool OffloadChecks { get; init; }

    /// <summary>How many files are checked at the same time.</summary>
    public int CheckConcurrency { get; init; } = 2;

    public string Version { get; init; } = "1.0.0";

    /// <summary>The project a folder of build outputs belongs to. Default: walk up to a solution, project file or .git.</summary>
    public Func<string, string> ProjectFolderOf { get; init; } = c => FileSet.ProjectFolder(c);

    /// <summary>Is this path a folder? Default: the file system. The demo answers for its sample folder.</summary>
    public Func<string, bool>? IsDirectory { get; init; }

    /// <summary>The .exe, .dll and .msi files in a folder. Default: the file system, sub-folders included.</summary>
    public Func<string, IEnumerable<string>>? EnumerateFolder { get; init; }
}
