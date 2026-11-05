namespace SignMyApp.Core;

public sealed class SignToolLocator : ISignToolLocator
{
    public string? Find()
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
        return FindIn(roots.Select(r => Path.Combine(r, "Windows Kits", "10", "bin")),
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
    }

    internal static string? FindIn(IEnumerable<string> sdkRoots, IEnumerable<string> pathEntries)
    {
        var candidates = new List<(Version Version, string Path)>();
        foreach (var root in sdkRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root))
                if (Version.TryParse(Path.GetFileName(directory), out var version))
                {
                    var tool = Path.Combine(directory, "x64", "signtool.exe");
                    if (File.Exists(tool)) candidates.Add((version, tool));
                }
        }
        var sdk = candidates.OrderByDescending(c => c.Version).Select(c => c.Path).FirstOrDefault();
        if (sdk is not null) return sdk;
        foreach (var directory in pathEntries)
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var tool = Path.Combine(directory.Trim('"'), "signtool.exe");
            if (Path.IsPathFullyQualified(tool) && File.Exists(tool)) return Path.GetFullPath(tool);
        }
        return null;
    }
}
