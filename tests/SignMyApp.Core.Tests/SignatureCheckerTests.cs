using SignMyApp.Core;
using Xunit;

namespace SignMyApp.Core.Tests;

public sealed class SignatureCheckerTests
{
    [Fact]
    public async Task UnsignedPeHasHashAndNoSigner()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var result = await new SignatureChecker().CheckAsync(path, default);
        Assert.Equal(FileState.NotSigned, result.State);
        Assert.Equal(Fixtures.Check(path).Sha256, result.Sha256);
        Assert.Null(result.Publisher);
        Assert.Equal(new FileInfo(path).Length, result.Size);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PfxSignedPeAndMsiExposeSignerAndMissingTimestamp(bool msi)
    {
        using var temp = new TempFolder();
        var path = msi ? await Fixtures.UnsignedMsiAsync(temp) : Fixtures.Pe(temp);
        Assert.Equal(FileState.NotSigned, (await new SignatureChecker().CheckAsync(path, default)).State);
        using var signer = new TestPfxSigner();
        await signer.SignAsync(path);
        var native = NativeSignature.Verify(path);
        var result = await Fixtures.TestTrustChecker().CheckAsync(path, default);
        Assert.True(result.State == FileState.NoTimestamp, $"Expected NoTimestamp; got {result.State}; unlocked native=0x{native:X8}; {result.Detail}");
        Assert.Equal("SignMyApp Test Publisher", result.Publisher);
        Assert.Equal(signer.Certificate.Thumbprint, result.SignerThumbprint);
        Assert.NotNull(result.Issuer);
        Assert.NotNull(result.ValidFrom);
        Assert.NotNull(result.ValidTo);
        Assert.Null(result.TimestampUtc);
        // The public checker must not trust the in-memory self-signed test certificate.
        var untrusted = await new SignatureChecker().CheckAsync(path, default);
        Assert.Equal(FileState.Error, untrusted.State);
        Assert.Contains("800B0109", untrusted.Detail);
    }

    [Fact]
    public async Task CorruptingSignedPeIsChangedSinceSigning()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        using var signer = new TestPfxSigner();
        await signer.SignAsync(path);
        // A DOS stub byte is hashed, but does not affect parsing of the PE/signature.
        using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        { file.Position = 0x50; int original = file.ReadByte(); file.Position--; file.WriteByte((byte)(original ^ 1)); }
        var result = await new SignatureChecker().CheckAsync(path, default);
        Assert.Equal(FileState.ChangedSinceSigning, result.State);
        Assert.Equal(signer.Certificate.Thumbprint, result.SignerThumbprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedMicrosoftFilesExposeSignerAndTimestampReadOnly(bool msi)
    {
        var path = msi ? @"C:\Windows\Installer\103f2bbb.msi" : @"C:\Program Files\dotnet\dotnet.exe";
        Assert.True(File.Exists(path), $"Read-only fixture not found: {path}");
        var result = await new SignatureChecker().CheckAsync(path, default);
        Assert.Equal(FileState.Signed, result.State);
        Assert.NotNull(result.TimestampUtc);
        Assert.NotNull(result.SignerThumbprint);
        Assert.Equal(msi ? "Microsoft Corporation" : ".NET", result.Publisher);
    }

    [Theory]
    [InlineData("text.txt", FileState.Unsupported)]
    [InlineData("missing.exe", FileState.Error)]
    public async Task UnsupportedAndMissingFilesAreResults(string name, FileState expected)
    {
        using var temp = new TempFolder();
        if (name == "text.txt") File.WriteAllText(temp.File(name), "hello");
        Assert.Equal(expected, (await new SignatureChecker().CheckAsync(temp.File(name), default)).State);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SignatureChecker().CheckAsync("never.exe", cancel.Token));
    }

    [NetworkFact]
    public async Task TimestampedPeIsSignedWithTestRootHook()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        using var signer = new TestPfxSigner();
        await signer.SignAsync(path, timestamp: true);
        var result = await Fixtures.TestTrustChecker().CheckAsync(path, default);
        Assert.Equal(FileState.Signed, result.State);
        Assert.NotNull(result.TimestampUtc);
        Assert.Equal(signer.Certificate.Thumbprint, result.SignerThumbprint);
    }
}
