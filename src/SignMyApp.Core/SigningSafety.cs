using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SignMyApp.Core;

internal static class SigningSafety
{
    internal static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".msi";

    internal static string LocalPath(string path, bool mustExist = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Any(c => char.IsControl(c) || "\"'`$;&|<>\u2018\u2019\u201c\u201d".Contains(c))
            || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)
            || Regex.IsMatch(path, @"^[a-zA-Z]:[^\\/]")
            || path.IndexOf(':', path.Length > 1 && path[1] == ':' ? 2 : 0) >= 0)
            throw new ArgumentException("Use a local drive path without device names, streams, control or shell characters.", nameof(path));
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        if (!Regex.IsMatch(root, @"^[a-zA-Z]:\\$")) throw new ArgumentException("Use a local drive path.", nameof(path));
        if (new DriveInfo(root).DriveType == DriveType.Network) throw new ArgumentException("Use a local drive path.", nameof(path));
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!mustExist && !Path.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Use a local path without symbolic links or directory junctions.");
        }
        return full;
    }

    internal static string Thumbprint(string thumbprint)
    {
        if (!Regex.IsMatch(thumbprint ?? "", "\\A[0-9a-fA-F]{40}\\z"))
            throw new ArgumentException("Choose a certificate by its 40-character SHA-1 thumbprint.", nameof(thumbprint));
        return thumbprint!.ToUpperInvariant();
    }

    internal static void Timestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "\"'`\\<>".Contains(c))
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !uri.Host.Contains('.') || uri.Host.EndsWith('.')
            || Regex.IsMatch(uri.Host, @"\.(localhost|local|internal|lan|home|test|invalid|example)$", RegexOptions.IgnoreCase)
            || (IPAddress.TryParse(uri.DnsSafeHost, out var ip) && !IsPublicAddress(ip)))
            throw new ArgumentException("Use an HTTP(S) timestamp URL with a public host, without credentials, spaces, query or fragment.", nameof(value));
    }

    private static bool IsPublicAddress(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length == 16)
            return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && (b[1] == 2 ||
                (b[1] == 1 && (b[2] == 0 || (b[2] == 0x0d && b[3] == 0xb8)))));
        return !(b[0] is 0 or 10 or 127 || b[0] >= 224 || (b[0] == 100 && b[1] is >= 64 and <= 127)
            || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && (b[1] is 0 or 168 || (b[1] == 88 && b[2] == 99)))
            || (b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100)))
            || (b[0] == 203 && b[1] == 0 && b[2] == 113));
    }

    internal static async Task<string> HashAsync(Stream stream, CancellationToken ct) =>
        Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
}
