using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SignMyApp.Core;
using Xunit;

namespace SignMyApp.Core.Tests;

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "signmyapp-tests-" + Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        var full = System.IO.Path.GetFullPath(Path);
        var root = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        if (!full.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(full).StartsWith("signmyapp-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(full, recursive: true);
    }
}

internal static class Fixtures
{
    public static string UnsignedPeSource
    {
        get
        {
            var packs = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "packs", "Microsoft.NETCore.App.Host.win-x64");
            return Directory.EnumerateDirectories(packs).OrderByDescending(p => Version.Parse(System.IO.Path.GetFileName(p)))
                .Select(p => System.IO.Path.Combine(p, "runtimes", "win-x64", "native", "apphost.exe")).First(System.IO.File.Exists);
        }
    }
    public static string Pe(TempFolder temp, string name = "app.exe")
    {
        var file = temp.File(name);
        System.IO.File.Copy(UnsignedPeSource, file);
        Assert.Equal(NativeSignature.NoSignature, NativeSignature.Verify(file));
        return file;
    }
    public static Task<string> UnsignedMsiAsync(TempFolder temp)
    {
        var file = temp.File("unsigned.msi");
        System.IO.File.Copy(MsiSource, file);
        MsiFixture.RemoveSignature(file);
        Assert.Equal(NativeSignature.NoSignature, NativeSignature.Verify(file));
        return Task.FromResult(file);
    }
    public const string MsiSource = @"C:\Windows\Installer\103f2bbb.msi";
    public static SigningCertificate Certificate(CertSource source = CertSource.Store) => new(new string('A', 40),
        "CN=Test Publisher", "Test Publisher", "Test Issuer", DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddDays(30), source, true, false);
    public static FileCheck Check(string path, string? thumbprint = null) => new(path, System.IO.Path.GetFileName(path),
        new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path))),
        FileState.NotSigned, null, null, null, null, null, thumbprint, null);

    public static X509Certificate2 CertificateInMemory(bool codeSigning = true, DateTimeOffset? end = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=SignMyApp Test Publisher", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(codeSigning ? "1.3.6.1.5.5.7.3.3" : "1.3.6.1.5.5.7.3.1") }, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-3), end ?? DateTimeOffset.UtcNow.AddDays(30));
    }

    public static SignatureChecker TestTrustChecker() => new(path =>
    {
        int verdict = NativeSignature.Verify(path);
        // A test-only trust hook replaces ONLY untrusted-root, never digest validation.
        // Production still reports the untrusted test certificate as Error; no store is modified.
        return verdict == unchecked((int)0x800B0109) ? 0 : verdict;
    });
}

// This hook lives only in the test assembly. The product never accepts a PFX or password.
internal sealed class TestPfxSigner : IDisposable
{
    private readonly TempFolder temp = new();
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public X509Certificate2 Certificate { get; } = Fixtures.CertificateInMemory();
    public async Task SignAsync(string path, bool timestamp = false)
    {
        var tool = new SignToolLocator().Find();
        Assert.NotNull(tool);
        var pfx = temp.File("test.pfx");
        System.IO.File.WriteAllBytes(pfx, Certificate.Export(X509ContentType.Pfx, password));
        Assert.True(System.IO.File.Exists(pfx));
        var arguments = new List<string> { "sign", "/f", pfx, "/p", password, "/fd", "sha256" };
        if (timestamp) arguments.AddRange(["/tr", "http://time.certum.pl", "/td", "sha256"]);
        arguments.Add(path);
        try
        {
            var result = await new SignToolProcess().RunAsync(tool, arguments, CancellationToken.None);
            Assert.True(result.ExitCode == 0, $"Test signing failed (exit {result.ExitCode}): {result.Error} {result.Output}".Replace(password, "[redacted]"));
        }
        finally { System.IO.File.Delete(pfx); }
    }
    public void Dispose() { Certificate.Dispose(); temp.Dispose(); }
}

public class SignToolFactAttribute : FactAttribute
{
    public SignToolFactAttribute()
    {
        if (new SignToolLocator().Find() is null) Skip = "Microsoft SignTool is not installed.";
    }
}

public sealed class MsiFactAttribute : FactAttribute
{
    public MsiFactAttribute()
    {
        if (!File.Exists(Fixtures.MsiSource)) Skip = "The cached Microsoft MSI fixture is not installed.";
    }
}

// Skip only the affected theory row: missing MSI must not suppress PE coverage.
public sealed class FixtureInlineDataAttribute : Xunit.Sdk.DataAttribute
{
    private readonly bool msi;
    public FixtureInlineDataAttribute(bool msi, bool requiresSignTool = false)
    {
        this.msi = msi;
        if (requiresSignTool && new SignToolLocator().Find() is null)
            Skip = "Microsoft SignTool is not installed.";
        else if (msi && !File.Exists(Fixtures.MsiSource))
            Skip = "The cached Microsoft MSI fixture is not installed.";
    }
    public override IEnumerable<object[]> GetData(System.Reflection.MethodInfo testMethod) => [[msi]];
}

public sealed class NetworkFactAttribute : SignToolFactAttribute
{
    public NetworkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SIGNMYAPP_NET_TESTS") != "1") Skip = "Set SIGNMYAPP_NET_TESTS=1 to contact the timestamp service.";
    }
}

internal static class MsiFixture
{
    internal static void RemoveSignature(string path)
    {
        Marshal.ThrowExceptionForHR(StgOpenStorage(path, IntPtr.Zero, 0x12, IntPtr.Zero, 0, out var storage));
        try
        {
            foreach (var name in new[] { "\u0005DigitalSignature", "\u0005MsiDigitalSignatureEx" })
            {
                int hr = storage.DestroyElement(name);
                if (hr != unchecked((int)0x80030002)) Marshal.ThrowExceptionForHR(hr);
            }
            Marshal.ThrowExceptionForHR(storage.Commit(0));
        }
        finally { Marshal.FinalReleaseComObject(storage); }
    }
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgOpenStorage(string path, IntPtr priority, uint mode, IntPtr exclude, uint reserved, out IStorage storage);
    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        [PreserveSig] int CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint r1, uint r2, out IntPtr stream);
        [PreserveSig] int OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved, uint mode, uint r2, out IntPtr stream);
        [PreserveSig] int CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint r1, uint r2, out IntPtr storage);
        [PreserveSig] int OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr priority, uint mode, IntPtr exclude, uint reserved, out IntPtr storage);
        [PreserveSig] int CopyTo(uint count, IntPtr interfaces, IntPtr exclude, IntPtr destination);
        [PreserveSig] int MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, uint flags);
        [PreserveSig] int Commit(uint flags);
        [PreserveSig] int Revert();
        [PreserveSig] int EnumElements(uint r1, IntPtr r2, uint r3, out IntPtr elements);
        [PreserveSig] int DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string name);
    }
}
