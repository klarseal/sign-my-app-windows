using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SignMyApp.Core;
using Xunit;

namespace SignMyApp.Core.Tests;

public sealed class InfrastructureTests
{
    [Theory]
    [InlineData("SimplySign KSP", CertSource.SimplySign)]
    [InlineData("SimplySign Cryptographic Service Provider", CertSource.SimplySign)]
    [InlineData("Microsoft Smart Card Key Storage Provider", CertSource.Token)]
    [InlineData("Microsoft Base Smart Card Crypto Provider", CertSource.Token)]
    [InlineData("SafeNet Key Storage Provider", CertSource.Token)]
    [InlineData("Microsoft Software Key Storage Provider", CertSource.Store)]
    [InlineData(null, CertSource.Store)]
    public void SourcesUsePersistedProviderName(string? provider, CertSource expected) => Assert.Equal(expected, CertificateFinder.Source(provider));

    [Fact]
    public void FinderRequiresCodeSigningEkuAndPrivateKeyAndKeepsExpiredCertificatesVisible()
    {
        using var codeSigning = Fixtures.CertificateInMemory();
        using var server = Fixtures.CertificateInMemory(codeSigning: false);
        using var expired = Fixtures.CertificateInMemory(end: DateTimeOffset.UtcNow.AddDays(-1));
        using var publicOnly = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(codeSigning.RawData);
        Assert.True(CertificateFinder.IsCodeSigning(codeSigning));
        Assert.False(CertificateFinder.IsCodeSigning(server));
        Assert.False(CertificateFinder.IsCodeSigning(publicOnly));
        Assert.True(CertificateFinder.IsCodeSigning(expired));
        Assert.True(CertificateFinder.Describe(expired, null).IsExpired);
        Assert.Equal(CertSource.SimplySign, CertificateFinder.Describe(codeSigning, "SimplySign KSP").Source);
    }

    [Fact]
    public void LocatorUsesNewestNumericX64SdkThenPath()
    {
        using var temp = new TempFolder();
        foreach (var version in new[] { "10.0.9.0", "10.0.10.0" })
        {
            var folder = Path.Combine(temp.Path, version, "x64");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "signtool.exe"), "fake");
        }
        var x86 = Path.Combine(temp.Path, "10.0.99.0", "x86");
        Directory.CreateDirectory(x86);
        File.WriteAllText(Path.Combine(x86, "signtool.exe"), "fake");
        var pathTool = temp.File("signtool.exe");
        File.WriteAllText(pathTool, "fake");
        Assert.Equal(Path.Combine(temp.Path, "10.0.10.0", "x64", "signtool.exe"), SignToolLocator.FindIn([temp.Path], [temp.Path]));
        Assert.Equal(pathTool, SignToolLocator.FindIn([], [temp.File("missing"), temp.Path]));
        Assert.Null(SignToolLocator.FindIn([], []));
        Assert.Equal(@"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe", new SignToolLocator().Find());
    }

    [Fact]
    public void SettingsAndTrustShareAtomicJsonWithoutOverwritingEachOther()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json");
        var settings = new SettingsStore(path);
        var trust = new ProjectTrustStore(path);
        var cert = new string('A', 40);
        Assert.Equal("http://time.certum.pl", settings.Load().TimestampUrl);
        Assert.Empty(trust.List());
        Assert.False(File.Exists(path));
        trust.Trust(temp.Path, cert);
        settings.Save(new AppSettings { Language = "nb", TimestampUrl = "https://timestamp.digicert.com" });
        Assert.True(new ProjectTrustStore(path).IsTrusted(temp.Path.ToUpperInvariant() + "\\", cert.ToLowerInvariant()));
        Assert.False(trust.IsTrusted(temp.Path, new string('B', 40)));
        Assert.False(trust.IsTrusted(Path.Combine(temp.Path, "child"), cert));
        Assert.Equal("nb", new SettingsStore(path).Load().Language);
        trust.Trust(temp.Path, cert.ToLowerInvariant());
        Assert.Single(trust.List());
        trust.Trust(temp.Path, new string('B', 40));
        Assert.Equal(2, trust.List().Count);
        trust.Forget(temp.Path);
        Assert.Empty(new ProjectTrustStore(path).List());
        Assert.Equal("nb", settings.Load().Language);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void CorruptSettingsArePreservedAndNotSilentlyTrusted()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "broken json");
        Assert.Throws<IOException>(() => new SettingsStore(path).Load());
        Assert.Throws<IOException>(() => new ProjectTrustStore(path).Trust(temp.Path, new string('A', 40)));
        Assert.Equal("broken json", File.ReadAllText(path));
        File.WriteAllText(path, "{\"Settings\":null,\"TrustedProjects\":[]}");
        Assert.Throws<IOException>(() => new SettingsStore(path).Load());
    }

    [Theory]
    [InlineData("ftp://time.certum.pl")]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("https://user:password@time.certum.pl")]
    [InlineData("https://time.certum.pl/path?secret=x")]
    [InlineData("https://time.certum.pl/#fragment")]
    [InlineData("https://time.certum.pl/ a")]
    [InlineData("http://server.local")]
    public void UnsafeTimestampUrlsAreRefusedWithoutNetwork(string url) => Assert.Throws<ArgumentException>(() => SigningSafety.Timestamp(url));

    [Theory]
    [InlineData(@"\\server\share\app.exe")]
    [InlineData(@"\\?\C:\app.exe")]
    [InlineData(@"C:app.exe")]
    [InlineData(@"C:\app.exe:alternate")]
    [InlineData(@"C:\app;code.exe")]
    public void UnsafePathsAreRefusedBeforeFileAccess(string path) => Assert.Throws<ArgumentException>(() => SigningSafety.LocalPath(path));

    [Fact]
    public void BuiltProductAssembliesCannotReferenceRuntimeCompilersOrPowerShell()
    {
        foreach (var assembly in new[] { typeof(Signer).Assembly.Location, typeof(SignMyApp.Cli.Program).Assembly.Location })
        {
            using var file = File.OpenRead(assembly);
            using var pe = new PEReader(file);
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                Assert.False(new[] { "Microsoft.CodeAnalysis", "System.Management.Automation", "System.CodeDom", "System.Reflection.Emit" }
                    .Any(name.StartsWith), $"Forbidden assembly reference: {name}");
            }
            foreach (var handle in metadata.TypeReferences)
            {
                var type = metadata.GetTypeReference(handle);
                var name = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
                Assert.False(new[] { "System.Reflection.Emit.", "Microsoft.CodeAnalysis.", "System.Management.Automation.",
                    "Microsoft.CSharp.CSharpCodeProvider", "System.CodeDom.Compiler." }.Any(name.StartsWith), $"Forbidden type reference: {name}");
            }
        }
    }
}
