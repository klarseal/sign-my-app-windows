using SignMyApp.App.Services;
using SignMyApp.Core;

namespace SignMyApp.App.ViewModels;

/// <summary>The status hue carried by a 6 px dot. Never shown without words.</summary>
public enum Tone { None, Action, Success, Warn, Danger, Neutral }

/// <summary>What a row shows. Mirrors the detail-panel kinds in the design.</summary>
public enum RowKind { Queued, Checking, Unsigned, NoTimestamp, Good, Changed, JustSigned, HeldInstaller, Error }

public sealed class FileRowViewModel : ObservableObject
{
    readonly Action<FileRowViewModel>? _onTick;
    RowKind _kind = RowKind.Queued;
    FileCheck? _check;
    bool _isChecked;
    bool _isCheckEnabled;

    public FileRowViewModel(string path, Action<FileRowViewModel>? onTick = null)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        IsInstaller = FileSet.IsInstaller(path);
        _onTick = onTick;
    }

    public string Path { get; }
    public string FileName { get; }
    public bool IsInstaller { get; }
    public string CheckBoxLabel => Text.SignFileLabel(FileName);
    public string RowAutomationName => $"{FileName}, {StatusText}, {(IsChecked ? "ticked for signing" : "not ticked for signing")}";

    public FileCheck? Check
    {
        get => _check;
        set { if (Set(ref _check, value)) OnAllChanged(); }
    }

    public RowKind Kind
    {
        get => _kind;
        set { if (Set(ref _kind, value)) OnAllChanged(); }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
            {
                OnPropertyChanged(nameof(RowAutomationName));
                _onTick?.Invoke(this);
            }
        }
    }

    /// <summary>Sets the tick without telling the owner (used when the owner sets many at once).</summary>
    internal void SetCheckedSilently(bool value)
    {
        if (Set(ref _isChecked, value, nameof(IsChecked))) OnPropertyChanged(nameof(RowAutomationName));
    }

    public bool IsCheckEnabled
    {
        get => _isCheckEnabled;
        set => Set(ref _isCheckEnabled, value);
    }

    public bool ShowSpinner => Kind == RowKind.Checking;
    public bool ShowDot => !ShowSpinner;

    public string StatusText => Kind switch
    {
        RowKind.Queued => Text.StatusWaiting,
        RowKind.Checking => Text.StatusChecking,
        RowKind.Unsigned => Text.StatusNotSigned,
        RowKind.NoTimestamp => Text.StatusNoTimestamp,
        RowKind.Good or RowKind.JustSigned => Text.StatusSigned,
        RowKind.Changed => Text.StatusChanged,
        RowKind.HeldInstaller => Text.StatusRebuildThenCheck,
        _ => Text.StatusCouldNotCheck,
    };

    public Tone Tone => ToneOf(Kind);

    public static Tone ToneOf(RowKind kind) => kind switch
    {
        RowKind.Unsigned or RowKind.HeldInstaller => Tone.Action,
        RowKind.NoTimestamp => Tone.Warn,
        RowKind.Good or RowKind.JustSigned => Tone.Success,
        RowKind.Changed => Tone.Danger,
        RowKind.Checking => Tone.None,
        _ => Tone.Neutral,
    };

    /// <summary>Empty while the file is not checked yet; "None" when it carries no signature.</summary>
    public string PublisherText => Kind is RowKind.Queued or RowKind.Checking
        ? ""
        : string.IsNullOrWhiteSpace(Check?.Publisher) || Kind == RowKind.HeldInstaller || Kind == RowKind.Unsigned ? Text.PublisherNone : Check!.Publisher!;

    public bool PublisherIsNone => PublisherText == Text.PublisherNone;

    public static RowKind KindOf(FileCheck c) => c.State switch
    {
        FileState.NotSigned => RowKind.Unsigned,
        FileState.NoTimestamp => RowKind.NoTimestamp,
        FileState.Signed or FileState.SignedByOther => RowKind.Good,
        FileState.ChangedSinceSigning => RowKind.Changed,
        FileState.Unsupported or FileState.Error => RowKind.Error,
        _ => RowKind.Error,
    };
}
