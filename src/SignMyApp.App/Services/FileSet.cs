using System.IO;

namespace SignMyApp.App.Services;

/// <summary>Turns dropped or picked paths into the files we check, and finds the project folder.</summary>
public static class FileSet
{
    public static readonly string[] Extensions = [".exe", ".dll", ".msi"];

    static readonly string[] ProjectMarkers = ["*.sln", "*.slnx", "*.csproj", "*.vbproj", "*.fsproj", "*.vcxproj", "*.wixproj", "package.json", "Cargo.toml", "CMakeLists.txt"];

    public static bool IsSupported(string path) =>
        Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsInstaller(string path) =>
        string.Equals(System.IO.Path.GetExtension(path), ".msi", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Files are kept; folders are searched (sub-folders too) for .exe, .dll and .msi.
    /// Order is kept, installers go last. Returns the supported files and whether anything unsupported was given.
    /// </summary>
    public static (IReadOnlyList<string> Files, bool HadUnsupported) Expand(IEnumerable<string> paths, Func<string, bool>? isDirectory = null, Func<string, IEnumerable<string>>? enumerate = null)
    {
        isDirectory ??= Directory.Exists;
        enumerate ??= EnumerateSupported;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool unsupported = false;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (isDirectory(p))
            {
                foreach (var f in enumerate(p))
                    if (IsSupported(f) && seen.Add(f)) result.Add(f);
            }
            else if (IsSupported(p))
            {
                if (seen.Add(p)) result.Add(p);
            }
            else unsupported = true;
        }
        var ordered = result.Where(f => !IsInstaller(f)).Concat(result.Where(IsInstaller)).ToList();
        return (ordered, unsupported);
    }

    static IEnumerable<string> EnumerateSupported(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            return Directory.EnumerateFiles(folder, "*", options)
                .Where(IsSupported)
                .OrderBy(f => System.IO.Path.GetDirectoryName(f), StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    /// <summary>The deepest folder holding every file.</summary>
    public static string CommonFolder(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return "";
        var parts = files.Select(f => (System.IO.Path.GetDirectoryName(f) ?? "").TrimEnd('\\').Split('\\')).ToList();
        var first = parts[0];
        int n = first.Length;
        foreach (var p in parts.Skip(1))
        {
            int i = 0;
            while (i < n && i < p.Length && string.Equals(first[i], p[i], StringComparison.OrdinalIgnoreCase)) i++;
            n = i;
        }
        var common = string.Join('\\', first.Take(n));
        return common.EndsWith(':') ? common + "\\" : common;
    }

    /// <summary>
    /// The project a set of build outputs belongs to: walk up from the common folder (at most four levels)
    /// to the first folder with a solution, project file or .git; otherwise the common folder itself.
    /// </summary>
    public static string ProjectFolder(string commonFolder, Func<string, bool>? looksLikeProject = null)
    {
        looksLikeProject ??= LooksLikeProject;
        var dir = commonFolder;
        for (int i = 0; i <= 4 && !string.IsNullOrEmpty(dir); i++)
        {
            if (looksLikeProject(dir)) return dir;
            dir = System.IO.Path.GetDirectoryName(dir.TrimEnd('\\')) ?? "";
        }
        return commonFolder;
    }

    static bool LooksLikeProject(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            if (Directory.Exists(System.IO.Path.Combine(dir, ".git"))) return true;
            return ProjectMarkers.Any(m => Directory.EnumerateFiles(dir, m).Any());
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static string FolderName(string folder)
    {
        var name = System.IO.Path.GetFileName(folder.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? folder : name;
    }
}
