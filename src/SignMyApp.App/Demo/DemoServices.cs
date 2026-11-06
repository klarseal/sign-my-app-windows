using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using SignMyApp.App.Services;
using SignMyApp.Core;

namespace SignMyApp.App.Demo;

/// <summary>Sample data: Example Software Ltd and the six files. Names, hashes and dates are samples.</summary>
public static class DemoData
{
    public const string Publisher = "Example Software Ltd";
    public const string Issuer = "Example Code Signing CA";
    public const string Folder = @"C:\build\MyApp\release";
    public const string Project = @"C:\build\MyApp";
    public const string OtherProject = @"C:\work\Tools";

    public static readonly DateTimeOffset ValidFrom = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset ValidTo = new(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset SignedAt = new(2026, 11, 5, 14, 32, 0, TimeSpan.Zero);

    public static readonly string[] FileNames = ["MyApp.exe", "MyApp.Core.dll", "Updater.exe", "Contoso.Json.dll", "Legacy.Plugin.dll", "MyAppSetup.msi"];
    public static IReadOnlyList<string> Paths => FileNames.Select(n => System.IO.Path.Combine(Folder, n)).ToList();

    public static readonly IReadOnlyDictionary<string, string> Sha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MyApp.exe"] = "a3f19c0e5b7d24e86c1f0a9b3d5e7f21c48b6a0d9e3f5172b8c4d6e0f1a2b3c4",
        ["MyApp.Core.dll"] = "7e04b1d9c2a85f36e1d07b4a9c3f2e5810d6a7b2c9e4f13a5b8d0c7e6f2a9b14",
        ["Updater.exe"] = "2c9d5e0a7f3b81c64d2e9a0f5b7c3d18e6a4f2b0c9d7e5a3f1b8c6d4e2a0f9b7",
        ["Contoso.Json.dll"] = "5f2a8d0c3e7b91a64f8d2c0e6b9a3f5172d4c8e0b6a2f9d1c7e3b5a0f8d6c4e2",
        ["Legacy.Plugin.dll"] = "9b3e7a1c5d0f82e46a9c1b7d3e5f0a28c6d4b2e9f1a7c5d3b0e8f6a4c2d1e9b5",
        ["MyAppSetup.msi"] = "d41e8b2f9c6a03e75b1d8f4a2c9e6b30a7f5d2c8e1b4a96f3d0c7e5b2a8f1d64",
    };

    public static readonly IReadOnlyDictionary<string, string> SignedSha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MyApp.exe"] = "e5c27a90d4b13f68a2e0c9d57b4f1a36c8d2e0b9f7a5c3e1d6b4a2f0c8e9d7b5",
        ["MyApp.Core.dll"] = "b2d8f4a0c6e19b37d5a3f0c8e2b6d4a19f7c5e3b1d0a8f6c4e2b9d7a5c3f1e08",
        ["Updater.exe"] = "0c6e2a8f4d1b97c35e0a6d2f8b4c1e93a7d5f3b0c8e6a4d2f1b9c7e5a3d0f8b6",
        ["Legacy.Plugin.dll"] = "3d7b1e9f5a2c08d64e1b7f3a9c5d2e80b6f4a1c7e3d9b5f2a8c0e6d4b1f7a3c9",
        ["MyAppSetup.msi"] = "f8a2c6e0b4d91f37a5c3e1b9d7f5a3c10e8b6d4f2a9c7e5b3d1f0a8c6e4b2d97",
    };

    public static readonly SigningCertificate SimplySign = new("3A7F0B9CE412D866AA015FB39E74C2D0B81E5A6F", "CN=" + Publisher, Publisher, Issuer,
        ValidFrom, ValidTo, CertSource.SimplySign, true, false);

    public static readonly SigningCertificate Store = new("9D21C4E07B3F58A6D0E9124C7B5A3F80E6D1C2B4", "CN=" + Publisher, Publisher, Issuer,
        new DateTimeOffset(2025, 8, 19, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 8, 18, 0, 0, 0, TimeSpan.Zero), CertSource.Store, true, false);

    public static readonly SigningCertificate ExpiredToken = new("51E8A0C3F7D24B69E1A5C08F3D7B2E946A0C5D18", "CN=" + Publisher, Publisher, Issuer,
        new DateTimeOffset(2023, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), CertSource.Token, true, true);

    public static string ShaOf(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return Sha.TryGetValue(name, out var s) ? s : Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)));
    }

    public static FileCheck CheckOf(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var sha = ShaOf(path);
        return name switch
        {
            "Updater.exe" => new FileCheck(path, name, 412_160, sha, FileState.NoTimestamp, Publisher, Issuer, ValidFrom, ValidTo, null, SimplySign.Thumbprint, null),
            "Contoso.Json.dll" => new FileCheck(path, name, 688_640, sha, FileState.Signed, "Contoso Ltd", "Example Issuing CA 2",
                new DateTimeOffset(2025, 6, 12, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 3, 9, 14, 0, TimeSpan.Zero), "C0FFEE00C0FFEE00C0FFEE00C0FFEE00C0FFEE00", null),
            "Legacy.Plugin.dll" => new FileCheck(path, name, 96_256, sha, FileState.ChangedSinceSigning, Publisher, Issuer, ValidFrom, ValidTo,
                new DateTimeOffset(2026, 10, 14, 11, 2, 0, TimeSpan.Zero), SimplySign.Thumbprint, null),
            _ => new FileCheck(path, name, 1_204_224, sha, FileState.NotSigned, null, null, null, null, null, null, null),
        };
    }

    public static FileCheck SignedCheckOf(FileCheck before, SigningCertificate cert)
    {
        var sha = SignedSha.TryGetValue(before.FileName, out var s) ? s : Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(before.Sha256 + "signed")));
        return before with
        {
            Sha256 = sha, State = FileState.Signed, Publisher = cert.Publisher, Issuer = cert.Issuer,
            ValidFrom = cert.NotBefore, ValidTo = cert.NotAfter, TimestampUtc = SignedAt, SignerThumbprint = cert.Thumbprint, Detail = null,
        };
    }
}

public enum DemoCertificates { Several, None, LoggedOut, Expired }

/// <summary>Knobs for the demo and for tests.</summary>
public sealed class DemoOptions
{
    public DemoCertificates Certificates { get; set; } = DemoCertificates.Several;
    public TimeSpan CheckDelay { get; set; }
    public TimeSpan SignDelay { get; set; }
    /// <summary>The trust store starts with these projects (folder, thumbprint).</summary>
    public List<(string Folder, string Thumbprint)> Trusted { get; } = [];
}

public sealed class FakeSignatureChecker(DemoOptions options) : ISignatureChecker
{
    readonly Dictionary<string, TaskCompletionSource> _gates = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, FileCheck> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public int Calls { get; private set; }

    /// <summary>Holds the check of one file until <see cref="Release"/> (to show "Checking…").</summary>
    public void Hold(string fileName) => _gates[fileName] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Release(string fileName) { if (_gates.Remove(fileName, out var g)) g.TrySetResult(); }

    /// <summary>The next checks of this file return this result.</summary>
    public void Override(string fileName, FileCheck check) => _overrides[fileName] = check;

    public async Task<FileCheck> CheckAsync(string path, CancellationToken ct)
    {
        Calls++;
        var name = System.IO.Path.GetFileName(path);
        if (_gates.TryGetValue(name, out var gate)) await gate.Task.WaitAsync(ct);
        if (options.CheckDelay > TimeSpan.Zero) await Task.Delay(options.CheckDelay, ct);
        return _overrides.TryGetValue(name, out var o) ? o with { Path = path } : DemoData.CheckOf(path);
    }
}

public sealed class FakeCertificateFinder(DemoOptions options) : ICertificateFinder
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<SigningCertificate>> FindAsync(CancellationToken ct)
    {
        Calls++;
        IReadOnlyList<SigningCertificate> list = options.Certificates switch
        {
            DemoCertificates.Several => [DemoData.SimplySign, DemoData.Store, DemoData.ExpiredToken],
            DemoCertificates.Expired => [DemoData.ExpiredToken],
            _ => [],
        };
        return Task.FromResult(list);
    }
}

public sealed class FakeSigner(DemoOptions options) : ISigner
{
    readonly Dictionary<string, SignOutcome> _failOnce = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TaskCompletionSource> _gates = new(StringComparer.OrdinalIgnoreCase);

    public List<SignPlan> Plans { get; } = [];
    public List<string> SignedPaths { get; } = [];

    /// <summary>The next attempt on this file ends with this outcome; the attempt after succeeds.</summary>
    public void FailOnce(string fileName, SignOutcome outcome) => _failOnce[fileName] = outcome;

    /// <summary>Signing this file waits until <see cref="Release"/> (to show progress).</summary>
    public void Hold(string fileName) => _gates[fileName] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Release(string fileName) { if (_gates.Remove(fileName, out var g)) g.TrySetResult(); }

    public SignPlan CreatePlan(string projectFolder, SigningCertificate cert, string timestampUrl, IReadOnlyList<FileCheck> files)
    {
        var text = string.Join("\n", files.Select(f => f.Path + "|" + f.Sha256)) + "\n" + cert.Thumbprint + "\n" + timestampUrl;
        var hash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        var plan = new SignPlan(projectFolder, cert, timestampUrl, files.ToList(), hash);
        Plans.Add(plan);
        return plan;
    }

    public async IAsyncEnumerable<SignResult> SignAsync(SignPlan plan, IProgress<(string Path, int Done, int Total)>? progress,
        [EnumeratorCancellation] CancellationToken ct)
    {
        int done = 0;
        foreach (var f in plan.Files)
        {
            if (_gates.TryGetValue(f.FileName, out var gate)) await gate.Task.WaitAsync(ct);
            if (options.SignDelay > TimeSpan.Zero) await Task.Delay(options.SignDelay, ct);
            if (_failOnce.Remove(f.FileName, out var outcome))
            {
                yield return new SignResult(f.Path, outcome, null, null);
                yield break;
            }
            SignedPaths.Add(f.Path);
            done++;
            progress?.Report((f.Path, done, plan.Files.Count));
            yield return new SignResult(f.Path, SignOutcome.Signed, DemoData.SignedCheckOf(f, plan.Certificate), null);
        }
    }
}

public sealed class InMemoryTrustStore : IProjectTrustStore
{
    readonly List<(string Folder, string Thumbprint)> _items = [];

    public InMemoryTrustStore(IEnumerable<(string Folder, string Thumbprint)>? seed = null)
    {
        if (seed is not null) _items.AddRange(seed);
    }

    public bool IsTrusted(string folder, string thumbprint) => _items.Any(i =>
        string.Equals(i.Folder, folder, StringComparison.OrdinalIgnoreCase) && string.Equals(i.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));

    public void Trust(string folder, string thumbprint)
    {
        _items.RemoveAll(i => string.Equals(i.Folder, folder, StringComparison.OrdinalIgnoreCase));
        _items.Add((folder, thumbprint));
    }

    public void Forget(string folder) => _items.RemoveAll(i => string.Equals(i.Folder, folder, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<(string Folder, string Thumbprint)> List() => _items.ToList();
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();
    public int Saves { get; private set; }
    public AppSettings Load() => new() { TimestampUrl = Current.TimestampUrl, Language = Current.Language };
    public void Save(AppSettings settings)
    {
        Current = new AppSettings { TimestampUrl = settings.TimestampUrl, Language = settings.Language };
        Saves++;
    }
}

public sealed class FakeFilePicker : IFilePicker
{
    public IReadOnlyList<string> Files { get; set; } = DemoData.Paths;
    public string? Folder { get; set; } = DemoData.Folder;
    public IReadOnlyList<string> PickFiles() => Files;
    public string? PickFolder() => Folder;
}

public sealed class FakeShell : IShell
{
    public List<string> Opened { get; } = [];
    public List<string> Shown { get; } = [];
    public List<string> Copied { get; } = [];
    public void OpenUrl(string url) => Opened.Add(url);
    public void ShowInFolder(string path) => Shown.Add(path);
    public void CopyText(string text) => Copied.Add(text);
}

public sealed class FakeSimplySignProbe(DemoOptions options) : ISimplySignProbe
{
    public bool IsDesktopRunning() => options.Certificates == DemoCertificates.LoggedOut;
}

/// <summary>Builds the whole fake service set. <c>--demo</c> uses it with short delays; tests with none.</summary>
public sealed class DemoServices
{
    public DemoServices(DemoOptions? options = null, IShell? shell = null)
    {
        Options = options ?? new DemoOptions();
        Checker = new FakeSignatureChecker(Options);
        Finder = new FakeCertificateFinder(Options);
        Signer = new FakeSigner(Options);
        Trust = new InMemoryTrustStore(Options.Trusted);
        SettingsStore = new InMemorySettingsStore();
        Picker = new FakeFilePicker();
        Shell = new FakeShell();
        Services = new AppServices
        {
            Checker = Checker,
            Finder = Finder,
            Signer = Signer,
            Trust = Trust,
            Settings = SettingsStore,
            Picker = Picker,
            Shell = shell ?? Shell,
            SimplySign = new FakeSimplySignProbe(Options),
            OffloadChecks = false,
            Version = "1.0.0",
            ProjectFolderOf = c => string.Equals(c, DemoData.Folder, StringComparison.OrdinalIgnoreCase) ? DemoData.Project : c,
            IsDirectory = p => string.Equals(p.TrimEnd('\\'), DemoData.Folder, StringComparison.OrdinalIgnoreCase),
            EnumerateFolder = _ => DemoData.Paths,
        };
    }

    public DemoOptions Options { get; }
    public FakeSignatureChecker Checker { get; }
    public FakeCertificateFinder Finder { get; }
    public FakeSigner Signer { get; }
    public InMemoryTrustStore Trust { get; }
    public InMemorySettingsStore SettingsStore { get; }
    public FakeFilePicker Picker { get; }
    public FakeShell Shell { get; }
    public AppServices Services { get; }
}
