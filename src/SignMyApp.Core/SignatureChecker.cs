using System.ComponentModel;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace SignMyApp.Core;

public sealed class SignatureChecker : ISignatureChecker
{
    private readonly Func<string, int> verify;
    public SignatureChecker() : this(NativeSignature.Verify) { }
    internal SignatureChecker(Func<string, int> verify) => this.verify = verify;

    public Task<FileCheck> CheckAsync(string path, CancellationToken ct) => Task.Run(() => Check(path, ct), ct);

    private FileCheck Check(string path, CancellationToken ct)
    {
        var full = path;
        long size = 0;
        var hash = "";
        int? verdict = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            full = SigningSafety.LocalPath(path);
            if (!SigningSafety.IsSupported(full))
                return Empty(FileState.Unsupported, "Choose an .exe, .dll or .msi file.");
            // Keep all reads (hash, trust, metadata) on bytes that cannot be rewritten or renamed.
            using var locked = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            size = locked.Length;
            hash = Convert.ToHexString(SHA256.HashData(locked));
            ct.ThrowIfCancellationRequested();
            verdict = verify(full);
            byte[]? encoded = NativeSignature.ReadMessage(full);
            if (encoded is null)
                return Empty(verdict == NativeSignature.NoSignature ? FileState.NotSigned
                    : verdict == NativeSignature.BadDigest ? FileState.ChangedSinceSigning : FileState.Error,
                    verdict == NativeSignature.NoSignature ? null : TrustDetail(verdict.Value));
            var cms = new SignedCms();
            cms.Decode(encoded);
            if (cms.SignerInfos.Count == 0) return Empty(FileState.Error, "The signature has no signer.");
            var signer = cms.SignerInfos[0];
            var cert = signer.Certificate;
            if (cert is null) return Empty(FileState.Error, "The signer certificate is missing.");
            var timestamp = ReadTimestamp(signer);
            var state = verdict == NativeSignature.BadDigest ? FileState.ChangedSinceSigning
                : verdict != 0 ? FileState.Error
                : timestamp is null ? FileState.NoTimestamp : FileState.Signed;
            return new(full, Path.GetFileName(full), size, hash, state,
                cert.GetNameInfo(X509NameType.SimpleName, false), cert.Issuer,
                cert.NotBefore.ToUniversalTime(), cert.NotAfter.ToUniversalTime(), timestamp,
                cert.Thumbprint, verdict != 0 ? TrustDetail(verdict.Value)
                    : timestamp is null ? "The signature has no verifiable timestamp." : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or CryptographicException or Win32Exception or NotSupportedException)
        { return Empty(verdict == NativeSignature.BadDigest ? FileState.ChangedSinceSigning : FileState.Error, ex.Message); }

        FileCheck Empty(FileState state, string? detail) => new(full, Path.GetFileName(full), size, hash,
            state, null, null, null, null, null, null, detail);
    }

    private static string TrustDetail(int verdict) =>
        $"Windows signature verification failed (0x{unchecked((uint)verdict):X8}): {new Win32Exception(verdict).Message}";

    internal static DateTimeOffset? ReadTimestamp(SignerInfo signer)
    {
        foreach (CryptographicAttributeObject attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid.Value is not ("1.3.6.1.4.1.311.3.3.1" or "1.2.840.113549.1.9.16.2.14")) continue;
            foreach (AsnEncodedData value in attribute.Values)
            {
                if (Rfc3161TimestampToken.TryDecode(value.RawData, out var token, out var consumed)
                    && consumed == value.RawData.Length && token.VerifySignatureForSignerInfo(signer, out _))
                    return token.TokenInfo.Timestamp.ToUniversalTime();
            }
        }
        foreach (SignerInfo counter in signer.CounterSignerInfos)
        {
            try { counter.CheckSignature(true); }
            catch (CryptographicException) { continue; }
            foreach (CryptographicAttributeObject attribute in counter.SignedAttributes)
                if (attribute.Oid.Value == "1.2.840.113549.1.9.5" && attribute.Values.Count > 0)
                    return new DateTimeOffset(new Pkcs9SigningTime(attribute.Values[0].RawData).SigningTime.ToUniversalTime());
        }
        return null;
    }
}
