using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SignMyApp.Core;

public sealed class CertificateFinder : ICertificateFinder
{
    public Task<IReadOnlyList<SigningCertificate>> FindAsync(CancellationToken ct) => Task.Run<IReadOnlyList<SigningCertificate>>(() =>
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = new List<SigningCertificate>();
        foreach (var cert in store.Certificates)
        {
            using (cert)
            {
                ct.ThrowIfCancellationRequested();
                if (IsCodeSigning(cert)) found.Add(Describe(cert, ProviderName(cert)));
            }
        }
        return found.OrderBy(c => c.Publisher, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Thumbprint).ToArray();
    }, ct);

    internal static bool IsCodeSigning(X509Certificate2 cert) => cert.HasPrivateKey &&
        cert.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .Any(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.3"));

    internal static SigningCertificate Describe(X509Certificate2 cert, string? provider) => new(
        cert.Thumbprint, cert.Subject, cert.GetNameInfo(X509NameType.SimpleName, false), cert.Issuer,
        cert.NotBefore.ToUniversalTime(), cert.NotAfter.ToUniversalTime(), Source(provider), cert.HasPrivateKey,
        cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow);

    internal static CertSource Source(string? provider)
    {
        if (provider?.Contains("SimplySign", StringComparison.OrdinalIgnoreCase) == true) return CertSource.SimplySign;
        string[] tokenNames = ["smart card", "smartcard", "etoken", "safenet", "yubikey", "athena", "bit4id", "epass"];
        return provider is not null && tokenNames.Any(n => provider.Contains(n, StringComparison.OrdinalIgnoreCase))
            ? CertSource.Token : CertSource.Store;
    }

    private static string? ProviderName(X509Certificate2 cert)
    {
        // Inspect persisted provider metadata only. Acquiring a key can prompt for a PIN/login.
        uint size = 0;
        if (!CertGetCertificateContextProperty(cert.Handle, 2, IntPtr.Zero, ref size)) return null;
        var pointer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            return CertGetCertificateContextProperty(cert.Handle, 2, pointer, ref size)
                ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, IntPtr.Size)) : null;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertGetCertificateContextProperty(IntPtr cert, uint property, IntPtr data, ref uint size);
}
