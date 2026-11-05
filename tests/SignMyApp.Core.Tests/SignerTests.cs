using System.Security.Cryptography;
using SignMyApp.Core;
using Xunit;

namespace SignMyApp.Core.Tests;

public sealed class SignerTests
{
    private static readonly string Timestamp = new AppSettings().TimestampUrl;
    private sealed class Locator(string? path = @"C:\fake\signtool.exe") : ISignToolLocator { public string? Find() => path; }
    internal sealed class Checker(Func<string, FileCheck> check) : ISignatureChecker
    { public Task<FileCheck> CheckAsync(string path, CancellationToken ct) => Task.FromResult(check(path)); }
    private sealed class Runner(Func<IReadOnlyList<string>, CancellationToken, Task<ToolResult>> run) : ISignToolProcess
    {
        public int Calls { get; private set; }
        public string? Snapshot { get; private set; }
        public async Task<ToolResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken ct)
        { Calls++; Snapshot = arguments[^1]; return await run(arguments, ct); }
    }
    private static FileCheck Signed(string path) => Fixtures.Check(path) with
    { State = FileState.Signed, TimestampUtc = DateTimeOffset.UtcNow, SignerThumbprint = Fixtures.Certificate().Thumbprint };
    private static Runner Successful(Action<IReadOnlyList<string>>? during = null) => new((args, _) =>
    {
        during?.Invoke(args);
        File.AppendAllText(args[^1], "signed snapshot");
        return Task.FromResult(new ToolResult(0, "", ""));
    });
    private static Signer Engine(Runner runner, ISignatureChecker? checker = null) => new(checker ?? new Checker(Signed), new Locator(), runner);
    private static async Task<SignResult[]> Results(ISigner signer, SignPlan plan, CancellationToken ct = default)
    {
        var results = new List<SignResult>();
        await foreach (var result in signer.SignAsync(plan, null, ct)) results.Add(result);
        return results.ToArray();
    }

    [Fact]
    public async Task ProductionArgumentsAreStoreOnlyAndSnapshotIsVerifiedBeforeReplacement()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var reviewed = Fixtures.Check(path);
        var runner = Successful(args =>
        {
            Assert.Equal(new[] { "sign", "/s", "My", "/sha1", Fixtures.Certificate().Thumbprint,
                "/fd", "sha256", "/tr", Timestamp, "/td", "sha256" }, args.Take(args.Count - 1));
            Assert.DoesNotContain("/f", args);
            Assert.DoesNotContain("/p", args);
            Assert.NotEqual(path, args[^1]);
            Assert.Equal(reviewed.Sha256, Fixtures.Check(args[^1]).Sha256);
        });
        var signer = Engine(runner);
        var result = Assert.Single(await Results(signer, signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [reviewed])));
        Assert.Equal(SignOutcome.Signed, result.Outcome);
        Assert.Equal(path, result.After!.Path);
        Assert.Equal(Fixtures.Check(path).Sha256, result.After.Sha256);
        Assert.NotEqual(reviewed.Sha256, result.After.Sha256);
        Assert.False(Directory.Exists(Path.GetDirectoryName(runner.Snapshot)));
    }

    [Fact]
    public async Task ChangeBeforeSigningNeverInvokesSignTool()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var runner = Successful();
        var signer = Engine(runner);
        var plan = signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [Fixtures.Check(path)]);
        File.WriteAllText(path, "new build");
        var result = Assert.Single(await Results(signer, plan));
        Assert.Equal(SignOutcome.FileChangedAfterReview, result.Outcome);
        Assert.Equal(0, runner.Calls);
        Assert.Equal("new build", File.ReadAllText(path));
    }

    [Fact]
    public async Task DeletedFileIsReportedAndOtherReviewedFilesStillRunWithProgress()
    {
        using var temp = new TempFolder();
        var first = Fixtures.Pe(temp);
        var second = Fixtures.Pe(temp, "second.exe");
        var runner = Successful();
        var signer = Engine(runner);
        var plan = signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [Fixtures.Check(first), Fixtures.Check(second)]);
        File.Delete(first);
        var results = new List<SignResult>();
        var updates = new List<(string Path, int Done, int Total)>();
        await foreach (var result in signer.SignAsync(plan, new InlineProgress(updates), default)) results.Add(result);
        Assert.Equal(new[] { SignOutcome.FileChangedAfterReview, SignOutcome.Signed }, results.Select(r => r.Outcome));
        Assert.Equal(new[] { (first, 1, 2), (second, 2, 2) }, updates);
        Assert.Equal(1, runner.Calls);
        Assert.False(File.Exists(first));
    }
    private sealed class InlineProgress(List<(string Path, int Done, int Total)> updates) : IProgress<(string Path, int Done, int Total)>
    { public void Report((string Path, int Done, int Total) value) => updates.Add(value); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangeOrReplacementDuringSignToolIsNeverOverwritten(bool replace)
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var runner = Successful(_ =>
        {
            if (replace) { File.Move(path, temp.File("old.exe")); File.WriteAllText(path, "replacement build"); }
            else File.WriteAllText(path, "replacement build");
        });
        var signer = Engine(runner);
        var plan = signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [Fixtures.Check(path)]);
        var result = Assert.Single(await Results(signer, plan));
        Assert.Equal(SignOutcome.FileChangedAfterReview, result.Outcome);
        Assert.Equal("replacement build", File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(runner.Snapshot)));
    }

    [Fact]
    public async Task OpenFileProducesFileInUseAndNoSigning()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var runner = Successful();
        var signer = Engine(runner);
        var plan = signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [Fixtures.Check(path)]);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(SignOutcome.FileInUse, Assert.Single(await Results(signer, plan)).Outcome);
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData(FileState.NoTimestamp, false)]
    [InlineData(FileState.Error, false)]
    [InlineData(FileState.Signed, true)]
    public async Task BadVerificationNeverChangesOriginal(FileState state, bool wrongPublisher)
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var before = Fixtures.Check(path);
        var runner = Successful();
        var checker = new Checker(p => Signed(p) with { State = state,
            SignerThumbprint = wrongPublisher ? new string('B', 40) : Fixtures.Certificate().Thumbprint });
        var signer = Engine(runner, checker);
        var result = Assert.Single(await Results(signer, signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [before])));
        Assert.Equal(SignOutcome.Failed, result.Outcome);
        Assert.Equal(before.Sha256, Fixtures.Check(path).Sha256);
        Assert.False(Directory.Exists(Path.GetDirectoryName(runner.Snapshot)));
    }

    [Theory]
    [InlineData(1, "Error: The timestamp server could not be reached.", CertSource.Store, SignOutcome.TimestampUnreachable)]
    [InlineData(2, "Warning: timestamp failed", CertSource.Store, SignOutcome.TimestampUnreachable)]
    [InlineData(1, "Error: SignerSign() failed. 0x80090016", CertSource.SimplySign, SignOutcome.SessionClosed)]
    [InlineData(1, "Error: private key provider not available", CertSource.SimplySign, SignOutcome.SessionClosed)]
    [InlineData(1, "Error: No certificates were found that met all the given criteria.", CertSource.SimplySign, SignOutcome.SessionClosed)]
    [InlineData(1, "Error: private key provider not available", CertSource.Store, SignOutcome.Failed)]
    [InlineData(1, "Error: 0x80070020 sharing violation", CertSource.Token, SignOutcome.FileInUse)]
    [InlineData(2, "Warning: unknown issue", CertSource.Store, SignOutcome.Failed)]
    public async Task SignToolFailuresAreMappedAndDiscarded(int exit, string stderr, CertSource source, SignOutcome outcome)
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var before = Fixtures.Check(path);
        var runner = new Runner((args, _) => { File.AppendAllText(args[^1], "partial signature"); return Task.FromResult(new ToolResult(exit, "", stderr)); });
        var signer = Engine(runner);
        var result = Assert.Single(await Results(signer, signer.CreatePlan(temp.Path, Fixtures.Certificate(source), Timestamp, [before])));
        Assert.Equal(outcome, result.Outcome);
        Assert.Contains(exit.ToString(), result.Message);
        Assert.Equal(before.Sha256, Fixtures.Check(path).Sha256);
        Assert.False(Directory.Exists(Path.GetDirectoryName(runner.Snapshot)));
    }

    [Fact]
    public async Task CancellationDiscardsSnapshotAndStopsFurtherFiles()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var second = Fixtures.Pe(temp, "second.exe");
        var before = Fixtures.Check(path);
        using var cancel = new CancellationTokenSource();
        var runner = new Runner((args, ct) =>
        { File.AppendAllText(args[^1], "partial"); cancel.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(new ToolResult(0, "", "")); });
        var signer = Engine(runner);
        var plan = signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [before, Fixtures.Check(second)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Results(signer, plan, cancel.Token));
        Assert.Equal(1, runner.Calls);
        Assert.Equal(before.Sha256, Fixtures.Check(path).Sha256);
        Assert.False(Directory.Exists(Path.GetDirectoryName(runner.Snapshot)));
    }

    [Fact]
    public async Task TamperedPlanIsRejectedBeforeSignTool()
    {
        using var temp = new TempFolder();
        var path = Fixtures.Pe(temp);
        var runner = Successful();
        var signer = Engine(runner);
        var plan = signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [Fixtures.Check(path)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Results(signer, plan with { TimestampUrl = "https://timestamp.digicert.com" }));
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public void PlanFreezesInputsAndHashesOrderCertificateTimestampAndBytes()
    {
        using var temp = new TempFolder();
        var first = Fixtures.Check(Fixtures.Pe(temp));
        var second = Fixtures.Check(Fixtures.Pe(temp, "second.dll"));
        var signer = Engine(Successful());
        var list = new List<FileCheck> { first, second };
        var cert = Fixtures.Certificate();
        var plan = signer.CreatePlan(temp.Path, cert, Timestamp, list);
        list.Clear();
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(plan.PlanHash, signer.CreatePlan(temp.Path, cert, Timestamp, [first, second]).PlanHash);
        Assert.NotEqual(plan.PlanHash, signer.CreatePlan(temp.Path, cert, Timestamp, [second, first]).PlanHash);
        Assert.NotEqual(plan.PlanHash, signer.CreatePlan(temp.Path, cert with { Thumbprint = new string('B', 40) }, Timestamp, [first, second]).PlanHash);
        Assert.NotEqual(plan.PlanHash, signer.CreatePlan(temp.Path, cert, "https://timestamp.digicert.com", [first, second]).PlanHash);
        Assert.NotEqual(plan.PlanHash, signer.CreatePlan(temp.Path, cert, Timestamp, [first with { Sha256 = new string('F', 64) }, second]).PlanHash);
    }

    [Fact]
    public void OtherPublisherIsMarkedOnlyInPlanAndInvalidInputsAreRefused()
    {
        using var temp = new TempFolder();
        var check = Fixtures.Check(Fixtures.Pe(temp), new string('B', 40));
        var signer = Engine(Successful());
        Assert.Equal(FileState.SignedByOther, signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [check]).Files[0].State);
        Assert.Equal(FileState.NotSigned, check.State);
        var samePublisher = check with { State = FileState.Signed, Publisher = Fixtures.Certificate().Publisher };
        Assert.Equal(FileState.Signed, signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [samePublisher]).Files[0].State);
        Assert.Throws<ArgumentException>(() => signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [check, check]));
        Assert.Throws<ArgumentException>(() => signer.CreatePlan(temp.Path, Fixtures.Certificate() with { IsExpired = true }, Timestamp, [check]));
        Assert.Throws<ArgumentException>(() => signer.CreatePlan(temp.Path, Fixtures.Certificate() with { HasPrivateKey = false }, Timestamp, [check]));
        Assert.Throws<ArgumentException>(() => signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, []));
    }

    [Fact]
    public async Task MissingSignToolGivesActionableFailure()
    {
        using var temp = new TempFolder();
        var check = Fixtures.Check(Fixtures.Pe(temp));
        var signer = new Signer(new Checker(Signed), new Locator(null), Successful());
        var result = Assert.Single(await Results(signer, signer.CreatePlan(temp.Path, Fixtures.Certificate(), Timestamp, [check])));
        Assert.Equal(SignOutcome.Failed, result.Outcome);
        Assert.Contains("Windows SDK", result.Message);
    }
}
