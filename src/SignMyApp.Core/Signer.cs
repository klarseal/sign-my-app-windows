using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SignMyApp.Core;

public sealed class Signer : ISigner
{
    private readonly ISignatureChecker checker;
    private readonly ISignToolLocator locator;
    private readonly ISignToolProcess process;
    public Signer() : this(new SignatureChecker(), new SignToolLocator()) { }
    public Signer(ISignatureChecker checker, ISignToolLocator locator) : this(checker, locator, new SignToolProcess()) { }
    internal Signer(ISignatureChecker checker, ISignToolLocator locator, ISignToolProcess process)
    { this.checker = checker; this.locator = locator; this.process = process; }

    public SignPlan CreatePlan(string projectFolder, SigningCertificate cert, string timestampUrl, IReadOnlyList<FileCheck> files)
        => CreatePlanCore(projectFolder, cert, timestampUrl, files, mustExist: true);

    private static SignPlan CreatePlanCore(string projectFolder, SigningCertificate cert, string timestampUrl,
        IReadOnlyList<FileCheck> files, bool mustExist)
    {
        ArgumentNullException.ThrowIfNull(cert);
        ArgumentNullException.ThrowIfNull(files);
        var folder = Path.TrimEndingDirectorySeparator(SigningSafety.LocalPath(projectFolder, mustExist));
        if (mustExist && !Directory.Exists(folder)) throw new ArgumentException("Choose a project folder.", nameof(projectFolder));
        var thumbprint = SigningSafety.Thumbprint(cert.Thumbprint);
        SigningSafety.Timestamp(timestampUrl);
        if (!cert.HasPrivateKey || cert.IsExpired || cert.NotBefore > DateTimeOffset.UtcNow || cert.NotAfter <= DateTimeOffset.UtcNow)
            throw new ArgumentException("This certificate is not ready for signing. Check its dates and signing-app login.", nameof(cert));
        if (files.Count == 0) throw new ArgumentException("Choose at least one file.", nameof(files));
        var reviewed = files.Select(f =>
        {
            var full = SigningSafety.LocalPath(f.Path, mustExist);
            if (!SigningSafety.IsSupported(full) || f.State == FileState.Unsupported
                || !Regex.IsMatch(f.Sha256, "\\A[0-9a-fA-F]{64}\\z"))
                throw new ArgumentException("Check each supported file before creating a plan.", nameof(files));
            if (!full.StartsWith(Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Every file must be inside the reviewed project folder.", nameof(files));
            return f with { Path = full, Sha256 = f.Sha256.ToUpperInvariant(),
                State = IsOtherPublisher(f, cert) ? FileState.SignedByOther : f.State };
        }).ToArray();
        if (reviewed.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != reviewed.Length)
            throw new ArgumentException("The plan contains a duplicate file.", nameof(files));
        var certificate = cert with { Thumbprint = thumbprint };
        return new(folder, certificate, timestampUrl, Array.AsReadOnly(reviewed), PlanHash(reviewed, thumbprint, timestampUrl));
    }

    internal static bool IsOtherPublisher(FileCheck file, SigningCertificate certificate) => file.SignerThumbprint is not null
        && !StringComparer.OrdinalIgnoreCase.Equals(file.SignerThumbprint, certificate.Thumbprint)
        && (string.IsNullOrWhiteSpace(file.Publisher) || !StringComparer.OrdinalIgnoreCase.Equals(file.Publisher, certificate.Publisher));

    internal static string PlanHash(IReadOnlyList<FileCheck> files, string thumbprint, string timestampUrl)
    {
        // Length-prefixed UTF-8 fields prevent separators in paths from colliding.
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(files.Count);
            foreach (var file in files) { writer.Write(file.Path); writer.Write(file.Sha256); }
            writer.Write(thumbprint);
            writer.Write(timestampUrl);
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }

    public async IAsyncEnumerable<SignResult> SignAsync(SignPlan plan,
        IProgress<(string Path, int Done, int Total)>? progress, [EnumeratorCancellation] CancellationToken ct)
    {
        // Rebuild defensively: callers can construct records or supply mutable lists themselves.
        var frozen = CreatePlanCore(plan.ProjectFolder, plan.Certificate, plan.TimestampUrl, plan.Files, mustExist: false);
        if (!StringComparer.Ordinal.Equals(frozen.PlanHash, plan.PlanHash))
            throw new InvalidOperationException("The signing plan changed after review. Review it again.");
        var tool = locator.Find();
        int done = 0;
        foreach (var file in frozen.Files)
        {
            ct.ThrowIfCancellationRequested();
            var result = tool is null ? new SignResult(file.Path, SignOutcome.Failed, null, "Install Microsoft SignTool from the Windows SDK.")
                : await SignOneAsync(tool, frozen, file, ct);
            progress?.Report((file.Path, ++done, frozen.Files.Count));
            yield return result;
        }
    }

    private async Task<SignResult> SignOneAsync(string tool, SignPlan plan, FileCheck file, CancellationToken ct)
    {
        string? stageDirectory = null;
        try
        {
            SigningSafety.LocalPath(file.Path);
            stageDirectory = Path.Combine(Path.GetTempPath(), $"signmyapp-{Guid.NewGuid():N}");
            SigningSafety.LocalPath(stageDirectory, mustExist: false);
            Directory.CreateDirectory(stageDirectory);
            var snapshot = Path.Combine(stageDirectory, "reviewed" + Path.GetExtension(file.Path));
            var backup = Path.Combine(stageDirectory, "original" + Path.GetExtension(file.Path));
            using (var original = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var copy = new FileStream(backup, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await original.CopyToAsync(copy, ct);
                copy.Position = 0;
                if (await SigningSafety.HashAsync(copy, ct) != file.Sha256) return Changed();
            }
            File.Copy(backup, snapshot);
            // The production path is deliberately store/thumbprint-only. No PFX/password hook.
            string[] arguments = ["sign", "/s", "My", "/sha1", plan.Certificate.Thumbprint,
                "/fd", "sha256", "/tr", plan.TimestampUrl, "/td", "sha256", snapshot];
            var run = await process.RunAsync(tool, arguments, ct);
            if (run.ExitCode != 0)
                return new(file.Path, MapFailure(run, plan.Certificate.Source), null, FailureMessage(run));
            var after = await checker.CheckAsync(snapshot, ct);
            if (after.State != FileState.Signed || after.TimestampUtc is null
                || !StringComparer.OrdinalIgnoreCase.Equals(after.SignerThumbprint, plan.Certificate.Thumbprint))
                return new(file.Path, SignOutcome.Failed, after with { Path = file.Path, FileName = file.FileName },
                    "The signed snapshot did not pass signature, publisher and timestamp verification. " + after.Detail);
            ct.ThrowIfCancellationRequested();
            SigningSafety.LocalPath(file.Path);
            // Hash and replace through the SAME handle, denying writes and deletion throughout.
            // A build may replace the file during SignTool; it cannot race this final check/write.
            using (var target = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                if (await SigningSafety.HashAsync(target, ct) != file.Sha256) return Changed();
                await using var signed = new FileStream(snapshot, FileMode.Open, FileAccess.Read, FileShare.Read);
                target.Position = 0;
                try
                {
                    // Once replacement starts, finish it even if cancellation arrives.
                    await signed.CopyToAsync(target, CancellationToken.None);
                    target.SetLength(signed.Length);
                    target.Flush(true);
                    target.Position = 0;
                    if (await SigningSafety.HashAsync(target, CancellationToken.None) != after.Sha256)
                        throw new IOException("The replaced file did not match the verified signed snapshot.");
                }
                catch (IOException)
                {
                    using var reviewed = File.OpenRead(backup);
                    target.Position = 0;
                    reviewed.CopyTo(target);
                    target.SetLength(reviewed.Length);
                    target.Flush(true);
                    throw;
                }
            }
            return new(file.Path, SignOutcome.Signed, after with { Path = file.Path, FileName = file.FileName }, null);
        }
        catch (FileNotFoundException) { return Changed(); }
        catch (DirectoryNotFoundException) { return Changed(); }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        { return new(file.Path, SignOutcome.FileInUse, null, "Close the app or build process using this file, then try again."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        { return new(file.Path, SignOutcome.Failed, null, ex.Message); }
        finally
        {
            if (stageDirectory is not null && Directory.Exists(stageDirectory)) Directory.Delete(stageDirectory, recursive: true);
        }
        SignResult Changed() => new(file.Path, SignOutcome.FileChangedAfterReview, null, "File changed after review");
    }

    internal static SignOutcome MapFailure(ToolResult run, CertSource source)
    {
        var text = (run.Error + "\n" + run.Output).ToLowerInvariant();
        if (text.Contains("0x80070020") || text.Contains("0x80070021") || text.Contains("used by another process")
            || text.Contains("sharing violation")) return SignOutcome.FileInUse;
        if (text.Contains("timestamp") || text.Contains("time stamp") || text.Contains("0x80072")
            || text.Contains("0x80096005")) return SignOutcome.TimestampUnreachable;
        if (source == CertSource.SimplySign && new[] { "private key", "provider", "keyset", "key container", "session",
            "0x800900", "0x80092004", "0x801000", "no certificates were found" }.Any(text.Contains))
            return SignOutcome.SessionClosed;
        return SignOutcome.Failed;
    }
    private static string FailureMessage(ToolResult run) =>
        $"SignTool exited with {run.ExitCode}. {run.Error.Trim()} {run.Output.Trim()}".Trim();
}
