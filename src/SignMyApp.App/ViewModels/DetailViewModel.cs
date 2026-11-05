using SignMyApp.App.Services;
using SignMyApp.Core;

namespace SignMyApp.App.ViewModels;

public sealed record FactRow(string Key, string Value, bool IsBar);

public sealed record StepRow(string Number, string Label, StepState State);

public enum StepState { Done, Current, Later }

/// <summary>The right-hand panel for the selected file. One status line; the explanation waits behind Why?.</summary>
public sealed class DetailViewModel : ObservableObject
{
    readonly Action? _sign;
    readonly IShell? _shell;
    bool _isWhyOpen;

    public DetailViewModel(FileRowViewModel row, bool canSign, Action? sign, IShell? shell)
    {
        Row = row;
        _sign = sign;
        _shell = shell;
        var c = row.Check;
        Name = row.FileName;
        Kind = row.Kind;
        Sha256 = c?.Sha256 ?? "";

        switch (row.Kind)
        {
            case RowKind.Queued:
            case RowKind.Checking:
                Label = Text.StatusChecking;
                ShowSpinner = true;
                Facts = [Bar(Text.FactPublisher), Bar(Text.FactIssuedBy), Bar(Text.FactValid), Bar(Text.FactTimestamp)];
                break;
            case RowKind.Unsigned:
                Label = Text.StatusNotSigned;
                Line = Text.DetailUnknownPublisher;
                ActionLabel = canSign ? Text.SignIt : "";
                break;
            case RowKind.NoTimestamp:
                Label = Text.StatusNoTimestamp;
                if (c?.ValidTo is { } until)
                {
                    Line = Text.SignatureValidUntil(until);
                    WhyText = Text.NoTimestampWhy(until);
                }
                Facts = [.. Signer(c), new FactRow(Text.FactTimestamp, Text.FactNone, false)];
                ActionLabel = canSign ? Text.SignAgain : "";
                break;
            case RowKind.Good:
                Label = Text.StatusSigned;
                Line = Text.DetailNothingToDo;
                Facts = [.. Signer(c), Stamp(c)];
                break;
            case RowKind.JustSigned:
                Label = Text.StatusSigned;
                Line = Text.DetailChecked;
                Facts = [.. Signer(c), Stamp(c)];
                break;
            case RowKind.Changed:
                Label = Text.StatusChanged;
                Line = Text.DetailTreatedAsNotSigned;
                WhyText = Text.DetailChangedWhy;
                Facts = [.. Signer(c, withValid: false), Stamp(c)];
                ActionLabel = canSign ? Text.SignAgain : "";
                break;
            case RowKind.HeldInstaller:
                Label = Text.DetailNotSignedYet;
                Line = Text.DetailRebuild;
                WhyText = Text.DetailRebuildWhy;
                Steps =
                [
                    new StepRow("✓", Text.StepSignAppFiles, StepState.Done),
                    new StepRow("2", Text.StepRebuildInstaller, StepState.Current),
                    new StepRow("3", Text.StepCheckAgain, StepState.Later),
                ];
                break;
            default:
                Label = Text.StatusCouldNotCheck;
                Line = c?.Detail ?? "";
                break;
        }

        Tone = FileRowViewModel.ToneOf(row.Kind == RowKind.Queued ? RowKind.Checking : row.Kind);
        ToggleWhyCommand = new RelayCommand(() => IsWhyOpen = !IsWhyOpen);
        CopyShaCommand = new RelayCommand(() => _shell?.CopyText(Sha256), () => Sha256.Length > 0);
        ActionCommand = new RelayCommand(() => _sign?.Invoke(), () => HasAction);
    }

    public FileRowViewModel Row { get; }
    public RowKind Kind { get; }
    public string Name { get; }
    public string Label { get; } = "";
    public Tone Tone { get; }
    public bool ShowSpinner { get; }
    public bool ShowDot => !ShowSpinner;
    public string Line { get; } = "";
    public bool HasLine => Line.Length > 0;
    public string WhyText { get; } = "";
    public bool HasWhy => WhyText.Length > 0;
    public IReadOnlyList<FactRow> Facts { get; } = [];
    public IReadOnlyList<StepRow> Steps { get; } = [];
    public bool HasSteps => Steps.Count > 0;
    public string Sha256 { get; }
    public string ShaShort => Text.ShortSha12(Sha256);
    public string ActionLabel { get; } = "";
    public bool HasAction => ActionLabel.Length > 0;

    public bool IsWhyOpen
    {
        get => _isWhyOpen && HasWhy;
        set => Set(ref _isWhyOpen, value);
    }

    public RelayCommand ToggleWhyCommand { get; }
    public RelayCommand CopyShaCommand { get; }
    public RelayCommand ActionCommand { get; }

    static FactRow Bar(string key) => new(key, "", true);

    static IEnumerable<FactRow> Signer(FileCheck? c, bool withValid = true)
    {
        if (c is null) yield break;
        if (!string.IsNullOrEmpty(c.Publisher)) yield return new FactRow(Text.FactPublisher, c.Publisher, false);
        if (!string.IsNullOrEmpty(c.Issuer)) yield return new FactRow(Text.FactIssuedBy, c.Issuer, false);
        if (withValid && c.ValidFrom is { } from && c.ValidTo is { } to) yield return new FactRow(Text.FactValid, Text.Range(from, to), false);
    }

    static FactRow Stamp(FileCheck? c) =>
        new(Text.FactTimestamp, c?.TimestampUtc is { } t ? Text.Stamp(t) : Text.FactNone, false);
}
