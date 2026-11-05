using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SignMyApp.Core;

public sealed class SettingsStore : ISettingsStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Klarseal", "SignMyApp", "settings.json");
    private readonly string path;
    private readonly string mutexName;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? path = null)
    {
        this.path = SigningSafety.LocalPath(path ?? DefaultPath, mustExist: false);
        mutexName = @"Local\Klarseal.SignMyApp." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.path.ToUpperInvariant())));
    }

    public AppSettings Load() => Access(false, d => new AppSettings { TimestampUrl = d.Settings.TimestampUrl, Language = d.Settings.Language });

    public static bool IsValidTimestampUrl(string? url)
    {
        try { SigningSafety.Timestamp(url ?? ""); return true; }
        catch (ArgumentException) { return false; }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SigningSafety.Timestamp(settings.TimestampUrl);
        if (settings.Language is not ("en" or "de" or "nb")) throw new ArgumentException("Choose en, de or nb.", nameof(settings));
        Access(true, d => { d.Settings = new AppSettings { TimestampUrl = settings.TimestampUrl, Language = settings.Language }; return 0; });
    }

    internal T Access<T>(bool write, Func<SettingsDocument, T> action)
    {
        using var mutex = new Mutex(false, mutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Settings are in use. Try again.");
        try
        {
            SigningSafety.LocalPath(path, mustExist: false);
            var document = File.Exists(path) ? Read() : new SettingsDocument();
            var result = action(document);
            if (write) Write(document);
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private SettingsDocument Read()
    {
        try
        {
            var document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(path))
                ?? throw new JsonException("Empty settings document.");
            if (document.Settings is null || document.TrustedProjects is null
                || document.TrustedProjects.Any(p => p is null || string.IsNullOrWhiteSpace(p.Folder) || string.IsNullOrWhiteSpace(p.Thumbprint)))
                throw new JsonException("Invalid settings document.");
            return document;
        }
        catch (JsonException ex) { throw new IOException("Settings could not be read. Keep a copy before repairing settings.json.", ex); }
    }

    private void Write(SettingsDocument document)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal sealed class SettingsDocument
    {
        public AppSettings Settings { get; set; } = new();
        public List<TrustedProject> TrustedProjects { get; set; } = [];
    }
    internal sealed record TrustedProject(string Folder, string Thumbprint);
}

public sealed class ProjectTrustStore : IProjectTrustStore
{
    private readonly SettingsStore store;
    public ProjectTrustStore(string? settingsPath = null) => store = new SettingsStore(settingsPath);
    public bool IsTrusted(string folder, string thumbprint)
    {
        var normalized = Normalize(folder);
        var thumb = SigningSafety.Thumbprint(thumbprint);
        return store.Access(false, d => d.TrustedProjects.Any(p => Same(p.Folder, normalized) && Same(p.Thumbprint, thumb)));
    }
    public void Trust(string folder, string thumbprint)
    {
        var normalized = Normalize(folder);
        var thumb = SigningSafety.Thumbprint(thumbprint);
        store.Access(true, d =>
        {
            d.TrustedProjects.RemoveAll(p => Same(p.Folder, normalized) && Same(p.Thumbprint, thumb));
            d.TrustedProjects.Add(new(normalized, thumb));
            return 0;
        });
    }
    public void Forget(string folder)
    {
        var normalized = Normalize(folder);
        store.Access(true, d => d.TrustedProjects.RemoveAll(p => Same(p.Folder, normalized)));
    }
    public IReadOnlyList<(string Folder, string Thumbprint)> List() => store.Access(false,
        d => d.TrustedProjects.Select(p => (p.Folder, p.Thumbprint)).ToArray());
    private static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(SigningSafety.LocalPath(folder, mustExist: false));
    private static bool Same(string a, string b) => StringComparer.OrdinalIgnoreCase.Equals(a, b);
}
