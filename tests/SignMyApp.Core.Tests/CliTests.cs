using System.Runtime.CompilerServices;
using SignMyApp.Core;
using Xunit;
using CliProgram = SignMyApp.Cli.Program;

namespace SignMyApp.Core.Tests;

public sealed class CliTests
{
    private sealed class Finder : ICertificateFinder
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<SigningCertificate>> FindAsync(CancellationToken ct)
        { Calls++; return Task.FromResult<IReadOnlyList<SigningCertificate>>([Fixtures.Certificate()]); }
    }
    private sealed class RecordingSigner : ISigner
    {
        public int Calls { get; private set; }
        public SignPlan? Plan { get; private set; }
        public SignPlan CreatePlan(string folder, SigningCertificate cert, string timestamp, IReadOnlyList<FileCheck> files) =>
            new Signer().CreatePlan(folder, cert, timestamp, files);
        public async IAsyncEnumerable<SignResult> SignAsync(SignPlan plan,
            IProgress<(string Path, int Done, int Total)>? progress, [EnumeratorCancellation] CancellationToken ct)
        {
            Calls++;
            Plan = plan;
            await Task.CompletedTask;
            foreach (var file in plan.Files) yield return new(file.Path, SignOutcome.Signed, file, null);
        }
    }
    private static Task<int> Run(string[] args, StringWriter output, RecordingSigner signer, Finder finder,
        string input = "", ISignatureChecker? checker = null) => CliProgram.RunAsync(args, new StringReader(input), output,
        checker ?? new SignerTests.Checker(p => Fixtures.Check(p)), finder, signer, () => new AppSettings(), default);

    [Fact]
    public async Task CheckNeedsNoCertificateAndReportsFullHash()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        using var output = new StringWriter();
        var finder = new Finder();
        var signer = new RecordingSigner();
        Assert.Equal(0, await Run(["check", path], output, signer, finder, checker: new SignatureChecker()));
        Assert.Contains(Fixtures.Check(path).Sha256, output.ToString());
        Assert.Contains("NotSigned", output.ToString());
        Assert.Equal(0, finder.Calls);
        Assert.Equal(0, signer.Calls);
    }

    [Theory]
    [InlineData("SIGN\n", 0, 1)]
    [InlineData("sign\n", 2, 0)]
    [InlineData("\n", 2, 0)]
    public async Task SigningRequiresExactTypedConfirmation(string input, int exit, int calls)
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        using var output = new StringWriter();
        var signer = new RecordingSigner();
        Assert.Equal(exit, await Run(["sign", "--thumbprint", Fixtures.Certificate().Thumbprint, path], output, signer, new Finder(), input));
        Assert.Equal(calls, signer.Calls);
        Assert.Contains("Project: " + temp.Path, output.ToString());
        Assert.Contains("Publisher: Test Publisher", output.ToString());
        Assert.Contains("Certificate: " + Fixtures.Certificate().Thumbprint, output.ToString());
        Assert.Contains("Plan SHA-256:", output.ToString());
        Assert.Contains("SHA-256: " + Fixtures.Check(path).Sha256, output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedPlanHashMustMatchBeforeSigning(bool correct)
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        using var output = new StringWriter();
        var signer = new RecordingSigner();
        var plan = signer.CreatePlan(temp.Path, Fixtures.Certificate(), new AppSettings().TimestampUrl, [Fixtures.Check(path)]);
        int exit = await Run(["sign", "--thumbprint", Fixtures.Certificate().Thumbprint,
            "--yes-i-reviewed", correct ? plan.PlanHash.ToLowerInvariant() : new string('F', 64), path], output, signer, new Finder());
        Assert.Equal(correct ? 0 : 2, exit);
        Assert.Equal(correct ? 1 : 0, signer.Calls);
    }

    [Fact]
    public async Task MixedRoundHoldsInstallerAndOtherPublisherUntilExplicitlySelected()
    {
        using var temp = new TempFolder();
        var app = Fixtures.Pe(temp);
        var other = Fixtures.Pe(temp, "other.dll");
        var msi = await Fixtures.UnsignedMsiAsync(temp);
        var signer = new RecordingSigner();
        using var output = new StringWriter();
        var checker = new SignerTests.Checker(p => Fixtures.Check(p, p == other ? new string('B', 40) : null));
        Assert.Equal(0, await Run(["sign", "--thumbprint", Fixtures.Certificate().Thumbprint, app, other, msi], output, signer, new Finder(), "SIGN\n", checker));
        Assert.Equal(app, Assert.Single(signer.Plan!.Files).Path);
        Assert.Contains("Held back (another publisher)", output.ToString());
        Assert.Contains("rebuild the installer", output.ToString());
        var explicitSigner = new RecordingSigner();
        Assert.Equal(0, await Run(["sign", "--thumbprint", Fixtures.Certificate().Thumbprint,
            "--include-installer", "--include-other-publishers", app, other, msi], output, explicitSigner, new Finder(), "SIGN\n", checker));
        Assert.Equal(3, explicitSigner.Plan!.Files.Count);
        Assert.Equal(FileState.SignedByOther, explicitSigner.Plan.Files[1].State);
    }

    [Theory]
    [InlineData("/f")]
    [InlineData("/p")]
    [InlineData("--password")]
    [InlineData("--yes")]
    public async Task PasswordPfxAndUnboundApprovalOptionsAreRejected(string option)
    {
        using var output = new StringWriter();
        var signer = new RecordingSigner();
        var finder = new Finder();
        Assert.Equal(2, await Run(["sign", "--thumbprint", Fixtures.Certificate().Thumbprint, option, "unused"], output, signer, finder));
        Assert.Equal(0, signer.Calls);
        Assert.Equal(0, finder.Calls);
    }
}
