namespace SignMyApp.Core;
public enum FileState { NotSigned, NoTimestamp, Signed, ChangedSinceSigning, SignedByOther, Unsupported, Error }
public sealed record FileCheck(string Path, string FileName, long Size, string Sha256, FileState State,
    string? Publisher, string? Issuer, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo,
    DateTimeOffset? TimestampUtc, string? SignerThumbprint, string? Detail);
public enum CertSource { SimplySign, Token, Store }
public sealed record SigningCertificate(string Thumbprint, string Subject, string Publisher, string Issuer,
    DateTimeOffset NotBefore, DateTimeOffset NotAfter, CertSource Source, bool HasPrivateKey, bool IsExpired);
public sealed record SignPlan(string ProjectFolder, SigningCertificate Certificate, string TimestampUrl,
    IReadOnlyList<FileCheck> Files, string PlanHash);          // PlanHash = SHA-256 over ordered (path, sha256), thumbprint, timestamp url
public enum SignOutcome { Signed, FileChangedAfterReview, SessionClosed, TimestampUnreachable, FileInUse, Failed }
public sealed record SignResult(string Path, SignOutcome Outcome, FileCheck? After, string? Message);
public interface ISignatureChecker { Task<FileCheck> CheckAsync(string path, CancellationToken ct); }
public interface ICertificateFinder { Task<IReadOnlyList<SigningCertificate>> FindAsync(CancellationToken ct); }  // CurrentUser\My, code-signing EKU 1.3.6.1.5.5.7.3.3, private key present
public interface ISigner {
    SignPlan CreatePlan(string projectFolder, SigningCertificate cert, string timestampUrl, IReadOnlyList<FileCheck> files);
    IAsyncEnumerable<SignResult> SignAsync(SignPlan plan, IProgress<(string Path, int Done, int Total)>? progress, CancellationToken ct);
}
public interface ISignToolLocator { string? Find(); }   // Windows Kits 10 bin (newest x64), then PATH
public interface IProjectTrustStore { bool IsTrusted(string folder, string thumbprint); void Trust(string folder, string thumbprint);
    void Forget(string folder); IReadOnlyList<(string Folder, string Thumbprint)> List(); }
public sealed class AppSettings { public string TimestampUrl { get; set; } = "http://time.certum.pl"; public string Language { get; set; } = "en"; }
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
