using System.Collections.ObjectModel;
using SignMyApp.App.Services;
using SignMyApp.Core;

namespace SignMyApp.App.ViewModels;

public enum SignKind { Review, Installer, Progress, Done, Error }

public enum SignError { None, SessionClosed, TimestampUnreachable, ChangedAfterReview, FileInUse, Failed }

public sealed record DetailLine(string Key, string Value, bool IsMono);

/// <summary>What the Sign window hands back when it closes.</summary>
public sealed record SignSessionOutcome(IReadOnlyList<SignResult> Signed, IReadOnlyList<string> HeldInstallers);

public sealed class SignFileRowViewModel : ObservableObject
{
    string _stateText = "";
    Tone _tone;
    bool _spin;
    bool _showFull;

    public SignFileRowViewModel(FileCheck check) { Check = check; }

    public FileCheck Check { get; }
    public string Name => Check.FileName;
    public string Path => Check.Path;
    public string Sha => Check.Sha256;
    public string ShaShort => Text.ShortSha8(Check.Sha256);

    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }
    public Tone Tone { get => _tone; private set => Set(ref _tone, value); }
    public bool Spin { get => _spin; private set => Set(ref _spin, value); }
    public bool HasState => StateText.Length > 0;
    public bool ShowDot => HasState && !Spin && Tone != Tone.None;

    public bool ShowFull
    {
        get => _showFull;
        set { if (Set(ref _showFull, value)) OnAllChanged(); }
    }

    public bool ShowShortSha => !HasState && !ShowFull;
    public bool ShowFullSha => !HasState && ShowFull;

    public void SetState(string text, Tone tone, bool spin)
    {
        StateText = text;
        Tone = tone;
        Spin = spin;
        OnAllChanged();
    }
}

/// <summary>
/// The Sign window, same words as the plugin: Cancel / Sign once / Always for this project.
/// Cancel has focus when it opens; Esc is Cancel and never signs.
/// </summary>
public sealed class SignDialogViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly Action<SignSessionOutcome> _closed;
    readonly Dictionary<string, SignResult> _signed = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _held = [];
    List<FileCheck> _reviewed = [];
    List<FileCheck> _work = [];
    SignKind _kind;
    SignError _error;
    SignResult? _failure;
    bool _showFull;
    bool _alwaysOpen;
    bool _stopRequested;
    int _done;
    bool _closedOnce;

    public SignDialogViewModel(AppServices services, string projectFolder, SigningCertificate certificate, string timestampUrl,
        IReadOnlyList<FileCheck> files, Action<SignSessionOutcome> closed)
    {
        _services = services;
        _closed = closed;
        ProjectFolder = projectFolder;
        Certificate = certificate;
        TimestampUrl = timestampUrl;

        CancelCommand = new RelayCommand(Cancel, () => !IsBusy);
        SignOnceCommand = new AsyncCommand(() => SignAsync(always: false), () => IsReviewing);
        AlwaysCommand = new AsyncCommand(() => SignAsync(always: true), () => IsReviewing);
        StopCommand = new RelayCommand(() => { _stopRequested = true; StopCommand?.Refresh(); }, () => IsBusy && !_stopRequested);
        ShowInFolderCommand = new RelayCommand(() => _services.Shell.ShowInFolder(_work.FirstOrDefault()?.Path ?? ProjectFolder));
        BackToFilesCommand = new RelayCommand(Close);
        ErrorPrimaryCommand = new AsyncCommand(() => Error == SignError.ChangedAfterReview ? ReviewAgainAsync() : RetryAsync(), () => IsError);
        ToggleFullCommand = new RelayCommand(() => ShowFull = !ShowFull);
        ToggleAlwaysCommand = new RelayCommand(() => AlwaysOpen = !AlwaysOpen);

        SetReviewed(files);
    }

    public string ProjectFolder { get; }
    public SigningCertificate Certificate { get; }
    public string TimestampUrl { get; }

    public SignKind Kind
    {
        get => _kind;
        private set
        {
            if (Set(ref _kind, value)) RefreshAll();
        }
    }

    public SignError Error { get => _error; private set => Set(ref _error, value); }

    /// <summary>Every file in the window: the review list, or the files being signed.</summary>
    public ObservableCollection<SignFileRowViewModel> Files { get; } = [];

    /// <summary>Installer order, step 1: the app files signed now.</summary>
    public ObservableCollection<SignFileRowViewModel> AppRows { get; } = [];

    public IReadOnlyList<FileCheck> Reviewed => _reviewed;
    public IReadOnlyList<FileCheck> Work => _work;
    public IReadOnlyList<string> HeldInstallers => _held;
    public int SignedCount => _signed.Count;
    int SignedInWork => _work.Count(f => _signed.ContainsKey(f.Path));

    public bool IsReviewing => Kind is SignKind.Review or SignKind.Installer;
    public bool IsReview => Kind == SignKind.Review;
    public bool IsInstaller => Kind == SignKind.Installer;
    public bool IsProgress => Kind == SignKind.Progress;
    public bool IsDone => Kind == SignKind.Done;
    public bool IsError => Kind == SignKind.Error;
    public bool IsBusy => Kind == SignKind.Progress;

    public string Title => Kind switch
    {
        SignKind.Review or SignKind.Installer => Text.SignWindowTitle(_reviewed.Count),
        SignKind.Progress => Text.SigningTitle(_work.Count),
        SignKind.Done => Text.SignedTitle(_signed.Count),
        _ => Error switch
        {
            SignError.SessionClosed => Text.ErrSessionTitle,
            SignError.TimestampUnreachable => Text.ErrTimestampTitle,
            SignError.ChangedAfterReview => Text.ChangedAfterReview(FailedName),
            SignError.FileInUse => Text.IsInUse(FailedName),
            _ => Text.NotSignedTitle(FailedName),
        },
    };

    string FailedName => _failure is null ? "" : System.IO.Path.GetFileName(_failure.Path);

    public Tone HeadTone => Kind switch
    {
        SignKind.Done => Tone.Success,
        SignKind.Error => Error is SignError.TimestampUnreachable or SignError.ChangedAfterReview ? Tone.Warn : Tone.Action,
        _ => Tone.None,
    };

    public bool HasHeadDot => HeadTone != Tone.None;

    public string Line => Kind switch
    {
        SignKind.Progress => Certificate.Source == CertSource.SimplySign ? Text.KeepSimplySignOpen : "",
        SignKind.Done => Text.DetailChecked,
        _ => "",
    };

    public bool HasLine => Line.Length > 0;

    public string Fix => Kind != SignKind.Error ? "" : Error switch
    {
        SignError.SessionClosed => Text.ErrSessionFix,
        SignError.TimestampUnreachable => Text.ErrTimestampFix,
        SignError.ChangedAfterReview => Text.ErrChangedFix,
        SignError.FileInUse => Text.ErrInUseFix,
        _ => _failure?.Message ?? "",
    };

    public bool HasFix => Fix.Length > 0;

    public string Status => Kind switch
    {
        SignKind.Progress => Text.NOfTotal(_done, _work.Count),
        SignKind.Done => _held.Count > 0 ? Text.NextRebuild(_held[0]) : "",
        SignKind.Error => SignedInWork == 0 ? Text.NothingSigned : Text.NOfTotalSigned(SignedInWork, _work.Count),
        _ => "",
    };

    public string ErrorSecondaryLabel => Error is SignError.SessionClosed or SignError.TimestampUnreachable ? Text.Cancel : Text.Close;

    public string ErrorPrimaryLabel => Error switch
    {
        SignError.SessionClosed => Text.SignAgain,
        SignError.ChangedAfterReview => Text.ReviewAgain,
        _ => Text.TryAgain,
    };

    public double ProgressValue => _work.Count == 0 ? 0 : Math.Min(1.0, (_done + (IsBusy ? 0.4 : 0)) / _work.Count);
    public int ProgressTotal => _work.Count;
    public int ProgressDone => _done;

    public bool ShowFiles => Kind is SignKind.Review or SignKind.Progress or SignKind.Done;
    public bool ShowDetails => IsReviewing;
    public bool CanToggleFull => IsReviewing;

    public IReadOnlyList<DetailLine> Details =>
    [
        new(Text.DetailPublisher, Certificate.Publisher, false),
        new(Text.DetailCertificate, Text.CertificateLine(Certificate), false),
        new(Text.DetailTimestamp, TimestampUrl, false),
        new(Text.DetailProject, ProjectFolder, true),
    ];

    public string Step1 => Text.NowAppFiles(AppRows.Count);
    public string MsiName => _held.FirstOrDefault() ?? "";
    public string Step2 => Text.ThenRebuild(MsiName);
    public string Step3 => Text.LastSign(MsiName);

    public bool ShowFull
    {
        get => _showFull && IsReviewing;
        set
        {
            if (Set(ref _showFull, value))
            {
                foreach (var r in Files) r.ShowFull = value;
                foreach (var r in AppRows) r.ShowFull = value;
                OnPropertyChanged(nameof(FullLabel));
            }
        }
    }

    public string FullLabel => ShowFull ? Text.HideFullSha : Text.ShowFullSha;

    public bool AlwaysOpen { get => _alwaysOpen; set => Set(ref _alwaysOpen, value); }

    public bool IsClosed => _closedOnce;

    public RelayCommand CancelCommand { get; }
    public AsyncCommand SignOnceCommand { get; }
    public AsyncCommand AlwaysCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ShowInFolderCommand { get; }
    public RelayCommand BackToFilesCommand { get; }
    public AsyncCommand ErrorPrimaryCommand { get; }
    public RelayCommand ToggleFullCommand { get; }
    public RelayCommand ToggleAlwaysCommand { get; }

    /// <summary>Esc. Cancel when it can; while signing it does nothing (and never signs).</summary>
    public bool Escape()
    {
        if (IsBusy) return true;
        if (IsDone) Close();
        else Cancel();
        return true;
    }

    /// <summary>For a project the person already trusts with this certificate: the review is computed, the click is skipped.</summary>
    public Task StartTrustedAsync() => SignAsync(always: false);

    void SetReviewed(IReadOnlyList<FileCheck> files)
    {
        _reviewed = files.ToList();
        var apps = _reviewed.Where(f => !FileSet.IsInstaller(f.Path)).ToList();
        var msis = _reviewed.Where(f => FileSet.IsInstaller(f.Path)).ToList();
        bool mixed = apps.Count > 0 && msis.Count > 0;
        _work = mixed ? apps : _reviewed.ToList();
        _held.Clear();
        if (mixed) _held.AddRange(msis.Select(m => m.FileName));

        Files.Clear();
        AppRows.Clear();
        if (mixed)
            foreach (var f in apps) AppRows.Add(new SignFileRowViewModel(f) { ShowFull = _showFull });
        else
            foreach (var f in _reviewed) Files.Add(new SignFileRowViewModel(f) { ShowFull = _showFull });
        _kind = mixed ? SignKind.Installer : SignKind.Review;
        RefreshAll();
    }

    async Task SignAsync(bool always)
    {
        if (always) _services.Trust.Trust(ProjectFolder, Certificate.Thumbprint);
        await RunAsync();
    }

    Task RetryAsync() => RunAsync();

    async Task RunAsync()
    {
        _stopRequested = false;
        _failure = null;
        Error = SignError.None;
        var remaining = _work.Where(f => !_signed.ContainsKey(f.Path)).ToList();
        if (remaining.Count == 0) { Kind = SignKind.Done; return; }

        _done = SignedInWork;
        Files.Clear();
        foreach (var f in _work) Files.Add(new SignFileRowViewModel(f));
        MarkProgressRows(remaining[0].Path);
        Kind = SignKind.Progress;
        RefreshAll();

        bool stopped = false;
        try
        {
            var plan = _services.Signer.CreatePlan(ProjectFolder, Certificate, TimestampUrl, remaining);
            await foreach (var r in _services.Signer.SignAsync(plan, null, CancellationToken.None))
            {
                if (r.Outcome != SignOutcome.Signed)
                {
                    _failure = r;
                    break;
                }
                _signed[r.Path] = r;
                _done = SignedInWork;
                var next = _work.FirstOrDefault(f => !_signed.ContainsKey(f.Path))?.Path;
                MarkProgressRows(_stopRequested ? null : next);
                RefreshAll();
                if (_stopRequested && next is not null) { stopped = true; break; }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var path = _work.FirstOrDefault(f => !_signed.ContainsKey(f.Path))?.Path ?? "";
            _failure = new SignResult(path, SignOutcome.Failed, null, ex.Message);
        }

        if (_failure is not null)
        {
            Error = _failure.Outcome switch
            {
                SignOutcome.SessionClosed => SignError.SessionClosed,
                SignOutcome.TimestampUnreachable => SignError.TimestampUnreachable,
                SignOutcome.FileChangedAfterReview => SignError.ChangedAfterReview,
                SignOutcome.FileInUse => SignError.FileInUse,
                SignOutcome.Failed => SignError.Failed,
                _ => SignError.Failed,
            };
            Kind = SignKind.Error;
        }
        else
        {
            if (stopped || _signed.Count > 0)
            {
                Files.Clear();
                foreach (var f in _work.Where(f => _signed.ContainsKey(f.Path)))
                {
                    var row = new SignFileRowViewModel(f);
                    row.SetState(Text.StatusSigned, Tone.Success, false);
                    Files.Add(row);
                }
            }
            Kind = SignKind.Done;
        }
        RefreshAll();
    }

    void MarkProgressRows(string? current)
    {
        foreach (var row in Files)
        {
            if (_signed.ContainsKey(row.Path)) row.SetState(Text.StatusSigned, Tone.Success, false);
            else if (string.Equals(row.Path, current, StringComparison.OrdinalIgnoreCase)) row.SetState(Text.StateSigning, Tone.None, true);
            else row.SetState(Text.StatusWaiting, Tone.Neutral, false);
        }
    }

    async Task ReviewAgainAsync()
    {
        var again = new List<FileCheck>();
        foreach (var f in _reviewed.Where(f => !_signed.ContainsKey(f.Path)))
        {
            try { again.Add(await _services.Checker.CheckAsync(f.Path, CancellationToken.None)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { again.Add(f with { State = FileState.Error, Detail = ex.Message }); }
        }
        _failure = null;
        Error = SignError.None;
        SetReviewed(again);
    }

    void Cancel()
    {
        if (IsBusy) return;
        Close();
    }

    void Close()
    {
        if (_closedOnce) return;
        _closedOnce = true;
        _closed(new SignSessionOutcome(_signed.Values.ToList(), _signed.Count > 0 ? _held.ToList() : []));
    }

    void RefreshAll()
    {
        OnAllChanged();
        CancelCommand.Refresh();
        SignOnceCommand.Refresh();
        AlwaysCommand.Refresh();
        StopCommand.Refresh();
        ErrorPrimaryCommand.Refresh();
    }
}
