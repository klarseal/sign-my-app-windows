using SignMyApp.Core;

namespace SignMyApp.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        return await RunAsync(args, Console.In, Console.Out, new SignatureChecker(), new CertificateFinder(),
            new Signer(), () => new SettingsStore().Load(), cancellation.Token);
    }

    internal static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output,
        ISignatureChecker checker, ICertificateFinder finder, ISigner signer, Func<AppSettings> settings, CancellationToken ct)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                await output.WriteLineAsync("signmyapp check <paths>\nsignmyapp sign --thumbprint <sha1> [--project <folder>] [--timestamp <url>] [--yes-i-reviewed <planhash>] [--include-installer] [--include-other-publishers] <paths>");
                return args.Length == 0 ? 2 : 0;
            }
            var command = Parse(args);
            var files = Expand(command.Paths).ToArray();
            if (files.Length == 0) throw new ArgumentException("No supported files were found.");
            var checks = new List<FileCheck>();
            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                var check = await checker.CheckAsync(path, ct);
                checks.Add(check);
                await output.WriteLineAsync($"{check.Path}\n  {check.State}; publisher: {check.Publisher ?? "—"}\n  SHA-256: {check.Sha256}");
                if (check.TimestampUtc is not null) await output.WriteLineAsync($"  Timestamp (UTC): {check.TimestampUtc:O}");
                if (check.Detail is not null) await output.WriteLineAsync("  " + check.Detail);
            }
            if (command.Verb == "check") return checks.Any(c => c.State is FileState.Error or FileState.Unsupported or FileState.ChangedSinceSigning) ? 1 : 0;
            if (checks.Any(c => c.State is FileState.Error or FileState.Unsupported))
                throw new ArgumentException("Resolve the file check errors before signing.");
            var certs = await finder.FindAsync(ct);
            var certificate = certs.SingleOrDefault(c => c.Thumbprint.Equals(command.Thumbprint, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("The selected code-signing certificate with a private key is not in CurrentUser\\My.");
            bool hasAppFiles = checks.Any(c => Path.GetExtension(c.Path).ToLowerInvariant() is ".exe" or ".dll");
            var selected = new List<FileCheck>();
            foreach (var check in checks)
            {
                if (Signer.IsOtherPublisher(check, certificate) && !command.IncludeOtherPublishers)
                {
                    await output.WriteLineAsync($"Held back (another publisher): {check.Path}. Use --include-other-publishers to select it explicitly.");
                    continue;
                }
                if (hasAppFiles && Path.GetExtension(check.Path).Equals(".msi", StringComparison.OrdinalIgnoreCase) && !command.IncludeInstaller)
                {
                    await output.WriteLineAsync($"Held back: {check.Path}. Sign the app files, rebuild the installer, then sign it. Use --include-installer to sign it anyway.");
                    continue;
                }
                selected.Add(check);
            }
            if (selected.Count == 0) throw new ArgumentException("No files selected for signing.");
            var folder = command.Project ?? CommonFolder(selected.Select(c => c.Path));
            var plan = signer.CreatePlan(folder, certificate, command.Timestamp ?? settings().TimestampUrl, selected);
            await output.WriteLineAsync($"Project: {plan.ProjectFolder}\nPublisher: {certificate.Publisher}\nCertificate: {certificate.Thumbprint}\nTimestamp server: {plan.TimestampUrl}\nPlan SHA-256: {plan.PlanHash}");
            foreach (var file in plan.Files) await output.WriteLineAsync($"Sign: {file.Path}\n  SHA-256: {file.Sha256}");
            if (command.Approval is not null)
            {
                if (!string.Equals(command.Approval, plan.PlanHash, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The reviewed plan hash does not match. Nothing signed.");
            }
            else
            {
                await output.WriteLineAsync("Type SIGN to sign these exact files, or press Enter to cancel:");
                if (await input.ReadLineAsync(ct) != "SIGN") { await output.WriteLineAsync("Nothing signed."); return 2; }
            }
            var failed = false;
            await foreach (var result in signer.SignAsync(plan, null, ct))
            {
                await output.WriteLineAsync($"{result.Path}: {result.Outcome}{(result.Message is null ? "" : " — " + result.Message)}");
                if (result.After?.TimestampUtc is not null) await output.WriteLineAsync($"  Timestamp (UTC): {result.After.TimestampUtc:O}");
                failed |= result.Outcome != SignOutcome.Signed;
            }
            return failed ? 1 : 0;
        }
        catch (OperationCanceledException) { await output.WriteLineAsync("Cancelled."); return 130; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Security.Cryptography.CryptographicException)
        { await output.WriteLineAsync(ex.Message); return 2; }
    }

    private sealed record Command(string Verb, string? Thumbprint, string? Project, string? Timestamp, string? Approval,
        bool IncludeInstaller, bool IncludeOtherPublishers, IReadOnlyList<string> Paths);

    private static Command Parse(string[] args)
    {
        if (args[0] is not ("check" or "sign")) throw new ArgumentException("Use check or sign. See --help.");
        string? thumb = null, project = null, timestamp = null, approval = null;
        bool installer = false, others = false, literal = false;
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (!literal && arg == "--") { literal = true; continue; }
            if (!literal && (arg.StartsWith('-') || arg.StartsWith('/')))
            {
                if (args[0] == "check" || !seen.Add(arg)) throw new ArgumentException($"Invalid or duplicate option: {arg}");
                if (arg == "--include-installer") { installer = true; continue; }
                if (arg == "--include-other-publishers") { others = true; continue; }
                if (arg is not ("--thumbprint" or "--project" or "--timestamp" or "--yes-i-reviewed") || ++i >= args.Length)
                    throw new ArgumentException($"Invalid option or missing value: {arg}");
                switch (arg)
                {
                    case "--thumbprint": thumb = args[i]; break;
                    case "--project": project = args[i]; break;
                    case "--timestamp": timestamp = args[i]; break;
                    case "--yes-i-reviewed": approval = args[i]; break;
                }
            }
            else paths.Add(arg);
        }
        if (paths.Count == 0) throw new ArgumentException("Choose at least one file or folder.");
        if (args[0] == "sign" && (thumb is null || thumb.Length != 40 || !thumb.All(Uri.IsHexDigit)))
            throw new ArgumentException("Choose a certificate with --thumbprint <40 hex characters>.");
        if (approval is not null && (approval.Length != 64 || !approval.All(Uri.IsHexDigit)))
            throw new ArgumentException("Use --yes-i-reviewed with the full plan SHA-256.");
        return new(args[0], thumb, project, timestamp, approval, installer, others, paths);
    }

    private static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var full = SigningSafety.LocalPath(path, mustExist: false);
            if (Directory.Exists(full))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = false };
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Use a folder without symbolic links.");
                foreach (var file in Directory.EnumerateFiles(full, "*", options).Order(StringComparer.OrdinalIgnoreCase))
                    if (Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".dll" or ".msi" && seen.Add(file)) yield return file;
            }
            else if (seen.Add(full)) yield return full;
        }
    }

    private static string CommonFolder(IEnumerable<string> paths)
    {
        var directories = paths.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!).ToArray();
        var folder = directories[0];
        while (!directories.All(d => d.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || d.StartsWith(Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder))
                ?? throw new ArgumentException("Files on different drives need separate signing rounds.");
        return folder;
    }
}
