using SignMyApp.App.Demo;
using SignMyApp.App.Services;
using SignMyApp.App.ViewModels;
using SignMyApp.Core;

namespace SignMyApp.App.Tests;

public sealed class CoreStateMappingTests
{
    public static TheoryData<FileState, RowKind, string> States => new()
    {
        { FileState.NotSigned, RowKind.Unsigned, Text.StatusNotSigned },
        { FileState.NoTimestamp, RowKind.NoTimestamp, Text.StatusNoTimestamp },
        { FileState.Signed, RowKind.Good, Text.StatusSigned },
        { FileState.ChangedSinceSigning, RowKind.Changed, Text.StatusChanged },
        { FileState.SignedByOther, RowKind.Good, Text.StatusSigned },
        { FileState.Unsupported, RowKind.Error, Text.StatusCouldNotCheck },
        { FileState.Error, RowKind.Error, Text.StatusCouldNotCheck },
    };

    public static TheoryData<SignOutcome, SignError> Outcomes => new()
    {
        { SignOutcome.Signed, SignError.None },
        { SignOutcome.FileChangedAfterReview, SignError.ChangedAfterReview },
        { SignOutcome.SessionClosed, SignError.SessionClosed },
        { SignOutcome.TimestampUnreachable, SignError.TimestampUnreachable },
        { SignOutcome.FileInUse, SignError.FileInUse },
        { SignOutcome.Failed, SignError.Failed },
    };

    [Fact]
    public void Mapping_cases_cover_every_Core_enum_member()
    {
        Assert.Equal(Enum.GetValues<FileState>(), States.Select(r => (FileState)r[0]));
        Assert.Equal(Enum.GetValues<SignOutcome>(), Outcomes.Select(r => (SignOutcome)r[0]));
    }

    [Theory]
    [MemberData(nameof(States))]
    public async Task File_states_map_to_existing_row_detail_and_selection(FileState state, RowKind kind, string text)
    {
        var s = new Scenario();
        var check = DemoData.CheckOf(DemoData.Paths[0]) with { State = state, Detail = "check detail" };
        s.Demo.Checker.Override(check.FileName, check);
        await s.InitAsync();
        await s.Vm.AddPathsAsync([check.Path], replace: true);
        var row = Assert.Single(s.Vm.Rows);
        Assert.Equal(kind, row.Kind);
        Assert.Equal(text, row.StatusText);
        Assert.Equal(text, s.Vm.Detail!.Label);
        if (state is FileState.Unsupported or FileState.Error)
        {
            Assert.Equal(check.Detail, s.Vm.Detail.Line);
            Assert.False(row.IsCheckEnabled);
            Assert.False(s.Vm.Detail.HasAction);
        }
        if (state is FileState.SignedByOther or FileState.Unsupported or FileState.Error)
            Assert.False(row.IsChecked);
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Sign_outcomes_use_existing_success_or_error_screen(SignOutcome outcome, SignError error)
    {
        var s = new Scenario();
        var d = await s.ReviewAsync(withMsi: false);
        if (outcome != SignOutcome.Signed) s.Demo.Signer.FailOnce("MyApp.exe", outcome);
        await d.SignOnceCommand.RunAsync();
        Assert.Equal(error, d.Error);
        Assert.Equal(outcome == SignOutcome.Signed ? SignKind.Done : SignKind.Error, d.Kind);
        var title = outcome switch
        {
            SignOutcome.Signed => Text.SignedTitle(3),
            SignOutcome.FileChangedAfterReview => Text.ChangedAfterReview("MyApp.exe"),
            SignOutcome.SessionClosed => Text.ErrSessionTitle,
            SignOutcome.TimestampUnreachable => Text.ErrTimestampTitle,
            SignOutcome.FileInUse => Text.IsInUse("MyApp.exe"),
            SignOutcome.Failed => Text.NotSignedTitle("MyApp.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        Assert.Equal(title, d.Title);
    }
}
