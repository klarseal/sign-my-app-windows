using System.IO;
using System.Collections.ObjectModel;
using SignMyApp.App.Services;
using SignMyApp.Core;

namespace SignMyApp.App.ViewModels;

public enum Screen { First, List, Settings }

public enum ListMode { Checking, Results, AfterSigning }

public enum Overlay { None, Certificates, Sign }

public sealed record ChipViewModel(Tone Tone, string Label);

/// <summary>The one window: first run and drop zone, the file list with its detail panel, settings, and the two dialogs.</summary>
public sealed class MainViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly AppSettings _settings;
    IReadOnlyList<SigningCertificate> _found = [];
    SigningCertificate? _certificate;
    Screen _screen = Screen.First;
    Screen _beforeSettings = Screen.First;
    ListMode _mode = ListMode.Results;
    Overlay _overlay = Overlay.None;
    FileRowViewModel? _selectedRow;
    DetailViewModel? _detail;
    CertificateDialogViewModel? _certDialog;
    SignDialogViewModel? _signDialog;
    bool _isDragOver;
    bool _isWrongType;
    bool _isCompact;
    bool _bulkTicking;
    int _generation;
    string _projectPath = "";
    string _projectFolder = "";

    public MainViewModel(AppServices services)
    {
        _services = services;
        _settings = LoadSettings(services);
        Settings = new SettingsViewModel(services, _settings, () => _found);

        ChooseFilesCommand = new AsyncCommand(Guard(() => PickFilesAsync(replace: true)), CanPick);
        ChooseFolderCommand = new AsyncCommand(Guard(PickFolderAsync), CanPick);
        AddFilesCommand = new AsyncCommand(Guard(() => PickFilesAsync(replace: Screen == Screen.First)), CanPick);
        OpenSettingsCommand = new RelayCommand(() => { if (!HasOverlay) OpenSettings(); });
        BackCommand = new RelayCommand(() => Screen = _beforeSettings);
        CheckAgainCommand = new AsyncCommand(Guard(CheckAgainAsync), () => IsList && Mode != ListMode.Checking && Rows.Count > 0);
        OpenCertificatesCommand = new AsyncCommand(Guard(OpenCertificatesAsync));
        PrimaryCommand = new AsyncCommand(Guard(PrimaryAsync), () => IsPrimaryEnabled);
        SignSelectedRowCommand = new AsyncCommand(Guard(SignSelectedRowAsync), () => SelectedRow?.Check is not null && HasCertificate);
        GetCertificateCommand = new RelayCommand(() => _services.Shell.OpenUrl(Text.GetCertificateUrl));
        ToggleSelectedCommand = new RelayCommand(ToggleSelected);
    }

    // ------------------------------------------------------------------ state

    public ObservableCollection<FileRowViewModel> Rows { get; } = [];
    public ObservableCollection<ChipViewModel> Chips { get; } = [];
    public SettingsViewModel Settings { get; }
    public IReadOnlyList<SigningCertificate> FoundCertificates => _found;

    public Screen Screen
    {
        get => _screen;
        set { if (Set(ref _screen, value)) Changed(); }
    }

    public ListMode Mode
    {
        get => _mode;
        private set { if (Set(ref _mode, value)) Changed(); }
    }

    public Overlay Overlay
    {
        get => _overlay;
        private set { if (Set(ref _overlay, value)) Changed(); }
    }

    public bool IsCompact { get => _isCompact; set => Set(ref _isCompact, value); }

    public bool IsDragOver
    {
        get => _isDragOver;
        private set { if (Set(ref _isDragOver, value)) Changed(); }
    }

    public bool IsWrongType
    {
        get => _isWrongType;
        private set { if (Set(ref _isWrongType, value)) Changed(); }
    }

    public SigningCertificate? Certificate
    {
        get => _certificate;
        private set { if (Set(ref _certificate, value)) Changed(); }
    }

    public FileRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value)) RebuildDetail();
        }
    }

    public DetailViewModel? Detail { get => _detail; private set => Set(ref _detail, value); }
    public CertificateDialogViewModel? CertDialog { get => _certDialog; private set => Set(ref _certDialog, value); }
    public SignDialogViewModel? SignDialog { get => _signDialog; private set => Set(ref _signDialog, value); }

    public string ProjectPath { get => _projectPath; private set => Set(ref _projectPath, value); }
    public string ProjectFolder { get => _projectFolder; private set { if (Set(ref _projectFolder, value)) OnPropertyChanged(nameof(ProjectName)); } }
    public string ProjectName => FileSet.FolderName(ProjectFolder);

    // ------------------------------------------------------------------ derived

    public bool IsFirstRun => Screen == Screen.First;
    public bool IsList => Screen == Screen.List;
    public bool IsSettings => Screen == Screen.Settings;
    public bool HasOverlay => Overlay != Overlay.None;
    public bool ShowCertificates => Overlay == Overlay.Certificates;
    public bool ShowSign => Overlay == Overlay.Sign;

    public string DropText => IsDragOver ? Text.DropRelease : IsWrongType ? Text.DropWrongType : Text.DropPrompt;
    public bool ShowDropButtons => !IsDragOver;

    public bool HasCertificate => Certificate is not null;
    public string CertName => Certificate?.Publisher ?? Text.NoCertificate;
    public Tone CertTone => HasCertificate ? Tone.Success : Tone.Neutral;
    public string CertSourceText => Certificate is null ? "" : Text.Source(Certificate.Source);
    public bool HasCertSource => HasCertificate;
    public string CertAria => Text.CertAria(Certificate);

    IEnumerable<FileRowViewModel> Picked => Rows.Where(r => r.IsChecked && r.IsCheckEnabled && r.Check is not null);
    public int PickedCount => Picked.Count();

    bool AfterSigningIdle => Mode == ListMode.AfterSigning && PickedCount == 0;

    public string FootNote => HasCertificate && Mode != ListMode.Checking && !AfterSigningIdle ? Text.NOfTotalSelected(PickedCount, Rows.Count) : "";
    public bool ShowGhostCheckAgain => Mode != ListMode.Checking && !AfterSigningIdle;
    public bool ShowFind => !HasCertificate && Mode != ListMode.Checking;
    public bool ShowPrimary => !ShowFind;

    public string PrimaryLabel => Mode == ListMode.Checking ? Text.SignFilesDisabled
        : AfterSigningIdle ? Text.CheckAgain
        : Text.SignNFiles(PickedCount);

    public bool IsPrimaryEnabled => Mode != ListMode.Checking && (AfterSigningIdle || (HasCertificate && PickedCount > 0));

    bool Eligible(FileRowViewModel r) => r.IsCheckEnabled && r.Kind is RowKind.Unsigned or RowKind.NoTimestamp;

    public bool IsSelectAllEnabled => HasCertificate && Mode != ListMode.Checking && Rows.Any(Eligible);

    /// <summary>The "Select all files to sign" box: ticked when every file that needs signing is ticked.</summary>
    public bool? AllChecked
    {
        get
        {
            var eligible = Rows.Where(Eligible).ToList();
            if (PickedCount == 0) return false;
            return eligible.Count > 0 && eligible.All(r => r.IsChecked) && PickedCount == eligible.Count ? true : null;
        }
        set
        {
            _bulkTicking = true;
            foreach (var r in Rows)
                r.SetCheckedSilently(value == true && Eligible(r));
            _bulkTicking = false;
            Changed();
        }
    }

    // ------------------------------------------------------------------ commands

    public AsyncCommand ChooseFilesCommand { get; }
    public AsyncCommand ChooseFolderCommand { get; }
    public AsyncCommand AddFilesCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand BackCommand { get; }
    public AsyncCommand CheckAgainCommand { get; }
    public AsyncCommand OpenCertificatesCommand { get; }
    public AsyncCommand PrimaryCommand { get; }
    public AsyncCommand SignSelectedRowCommand { get; }
    public RelayCommand GetCertificateCommand { get; }
    public RelayCommand ToggleSelectedCommand { get; }

    bool CanPick() => Screen != Screen.Settings;

    /// <summary>
    /// While a dialog is open the page behind it does nothing (shortcuts included), but keeps its look under the backdrop,
    /// as drawn: the guard is in the action, not in CanExecute.
    /// </summary>
    Func<Task> Guard(Func<Task> action) => () => HasOverlay ? Task.CompletedTask : action();

    // ------------------------------------------------------------------ start-up

    static AppSettings LoadSettings(AppServices s)
    {
        try { return s.Settings.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return new AppSettings(); }
    }

    public async Task InitializeAsync()
    {
        try { _found = await _services.Finder.FindAsync(CancellationToken.None); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _found = []; }
        Certificate = CertificateDialogViewModel.DefaultChoice(_found);
        Settings.Reload();
        ApplyDefaultTicks();
    }

    // ------------------------------------------------------------------ files in

    public void DragEnter(bool hasFiles) => IsDragOver = hasFiles;
    public void DragLeave() => IsDragOver = false;

    public Task DropAsync(IReadOnlyList<string> paths) => AddPathsAsync(paths, replace: Screen == Screen.First);

    async Task PickFilesAsync(bool replace)
    {
        var files = _services.Picker.PickFiles();
        if (files.Count > 0) await AddPathsAsync(files, replace);
    }

    async Task PickFolderAsync()
    {
        var folder = _services.Picker.PickFolder();
        if (folder is not null) await AddPathsAsync([folder], replace: true);
    }

    public async Task AddPathsAsync(IReadOnlyList<string> paths, bool replace)
    {
        IsDragOver = false;
        var (files, unsupported) = FileSet.Expand(paths, _services.IsDirectory, _services.EnumerateFolder);
        if (files.Count == 0)
        {
            if (Screen == Screen.First) IsWrongType = unsupported || paths.Count > 0;
            return;
        }
        IsWrongType = false;
        if (replace) Rows.Clear();
        var added = new List<FileRowViewModel>();
        foreach (var f in files)
        {
            if (Rows.Any(r => string.Equals(r.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
            var row = new FileRowViewModel(f, OnRowTicked);
            added.Add(row);
        }
        // Installers stay last in the list.
        var apps = Rows.Where(r => !r.IsInstaller).Concat(added.Where(r => !r.IsInstaller)).ToList();
        var msis = Rows.Where(r => r.IsInstaller).Concat(added.Where(r => r.IsInstaller)).ToList();
        Rows.Clear();
        foreach (var r in apps.Concat(msis)) Rows.Add(r);

        ProjectPath = FileSet.CommonFolder(Rows.Select(r => r.Path).ToList());
        ProjectFolder = _services.ProjectFolderOf(ProjectPath);
        Screen = Screen.List;
        if (SelectedRow is null || !Rows.Contains(SelectedRow)) SelectedRow = Rows.FirstOrDefault();
        await CheckRowsAsync(added.Count > 0 ? added : Rows.ToList());
    }

    async Task CheckAgainAsync()
    {
        foreach (var r in Rows) r.SetCheckedSilently(false);
        SelectedRow = Rows.FirstOrDefault();
        await CheckRowsAsync(Rows.ToList());
    }

    async Task CheckRowsAsync(IReadOnlyList<FileRowViewModel> rows)
    {
        int generation = ++_generation;
        foreach (var r in rows)
        {
            r.Kind = RowKind.Queued;
            r.Check = null;
        }
        Mode = ListMode.Checking;
        ApplyDefaultTicks();
        RebuildDetail();

        using var gate = new SemaphoreSlim(Math.Max(1, _services.CheckConcurrency));
        var tasks = rows.Select(async row =>
        {
            await gate.WaitAsync();
            try
            {
                if (generation != _generation) return;
                row.Kind = RowKind.Checking;
                if (ReferenceEquals(row, SelectedRow)) RebuildDetail();
                FileCheck check;
                try
                {
                    check = _services.OffloadChecks
                        ? await Task.Run(() => _services.Checker.CheckAsync(row.Path, CancellationToken.None))
                        : await _services.Checker.CheckAsync(row.Path, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    check = new FileCheck(row.Path, row.FileName, 0, "", FileState.Error, null, null, null, null, null, null, ex.Message);
                }
                if (generation != _generation) return;
                row.Check = check;
                row.Kind = FileRowViewModel.KindOf(check);
                if (ReferenceEquals(row, SelectedRow)) RebuildDetail();
                UpdateChips();
            }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);
        if (generation != _generation) return;
        Mode = ListMode.Results;
        ApplyDefaultTicks();
        RebuildDetail();
    }

    // ------------------------------------------------------------------ ticks

    /// <summary>
    /// Ticks what needs signing: files not signed, and files without a timestamp signed by the chosen publisher.
    /// Files signed by another publisher are never pre-ticked. Nothing is tickable without a certificate or while checking.
    /// </summary>
    void ApplyDefaultTicks()
    {
        bool can = HasCertificate && Mode != ListMode.Checking;
        var publisher = Certificate?.Publisher;
        foreach (var r in Rows)
        {
            r.IsCheckEnabled = can && r.Check is not null && r.Kind is not (RowKind.Queued or RowKind.Checking or RowKind.Error);
            bool tick = r.IsCheckEnabled && Mode == ListMode.Results && (r.Kind == RowKind.Unsigned
                || (r.Kind == RowKind.NoTimestamp && string.Equals(r.Check?.Publisher, publisher, StringComparison.OrdinalIgnoreCase)));
            r.SetCheckedSilently(tick);
        }
        Changed();
    }

    void OnRowTicked(FileRowViewModel row)
    {
        if (!_bulkTicking) Changed();
    }

    void ToggleSelected()
    {
        if (SelectedRow is { IsCheckEnabled: true } r) r.IsChecked = !r.IsChecked;
    }

    // ------------------------------------------------------------------ chips and detail

    void UpdateChips()
    {
        Chips.Clear();
        if (Mode == ListMode.Checking)
        {
            int done = Rows.Count(r => r.Kind is not (RowKind.Queued or RowKind.Checking));
            Chips.Add(new ChipViewModel(Tone.Neutral, Text.NOfTotalChecked(done, Rows.Count)));
            return;
        }
        int notSigned = Rows.Count(r => r.Kind == RowKind.Unsigned);
        int noTs = Rows.Count(r => r.Kind == RowKind.NoTimestamp);
        int changed = Rows.Count(r => r.Kind == RowKind.Changed);
        int signed = Rows.Count(r => r.Kind is RowKind.Good or RowKind.JustSigned);
        int rebuild = Rows.Count(r => r.Kind == RowKind.HeldInstaller);
        int errors = Rows.Count(r => r.Kind == RowKind.Error);
        var notSignedChip = (Tone.Action, notSigned, Text.ChipNotSigned);
        var noTsChip = (Tone.Warn, noTs, Text.ChipNoTimestamp);
        var changedChip = (Tone.Danger, changed, Text.ChipChanged);
        var signedChip = (Tone.Success, signed, Text.ChipSigned);
        var rebuildChip = (Tone.Action, rebuild, Text.ChipToRebuild);
        var errorChip = (Tone.Neutral, errors, Text.ChipCouldNotCheck);
        var order = Mode == ListMode.AfterSigning
            ? new[] { signedChip, changedChip, rebuildChip, notSignedChip, noTsChip, errorChip }
            : new[] { notSignedChip, noTsChip, changedChip, signedChip, rebuildChip, errorChip };
        foreach (var (tone, n, word) in order)
            if (n > 0) Chips.Add(new ChipViewModel(tone, Text.Chip(n, word)));
    }

    void RebuildDetail()
    {
        var row = SelectedRow;
        Detail = row is null ? null : new DetailViewModel(row,
            canSign: HasCertificate && Mode != ListMode.Checking && row.Check is not null,
            sign: () => SignSelectedRowCommand.Execute(null),
            shell: _services.Shell);
    }

    void Changed()
    {
        UpdateChips();
        OnAllChanged();
        ChooseFilesCommand.Refresh();
        ChooseFolderCommand.Refresh();
        AddFilesCommand.Refresh();
        OpenSettingsCommand.Refresh();
        CheckAgainCommand.Refresh();
        OpenCertificatesCommand.Refresh();
        PrimaryCommand.Refresh();
        SignSelectedRowCommand.Refresh();
    }

    // ------------------------------------------------------------------ settings

    void OpenSettings()
    {
        if (Screen != Screen.Settings) _beforeSettings = Screen;
        Settings.Reload();
        Screen = Screen.Settings;
    }

    // ------------------------------------------------------------------ certificate dialog

    async Task OpenCertificatesAsync()
    {
        CertDialog = new CertificateDialogViewModel(_services, _found, Certificate, OnCertificateDialogClosed);
        Overlay = Overlay.Certificates;
        await CertDialog.RefreshAsync();
    }

    void OnCertificateDialogClosed(SigningCertificate? chosen)
    {
        if (CertDialog is { } d) _found = d.Found;
        Overlay = Overlay.None;
        CertDialog = null;
        if (chosen is not null && !string.Equals(chosen.Thumbprint, Certificate?.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            Certificate = chosen;
            ApplyDefaultTicks();
        }
        else if (chosen is not null)
        {
            Certificate = chosen;
        }
        RebuildDetail();
        Changed();
    }

    // ------------------------------------------------------------------ Sign window

    async Task PrimaryAsync()
    {
        if (AfterSigningIdle)
        {
            await CheckAgainAsync();
            return;
        }
        await OpenSignAsync(Picked.Select(r => r.Check!).ToList());
    }

    Task SignSelectedRowAsync() =>
        SelectedRow?.Check is { } c ? OpenSignAsync([c]) : Task.CompletedTask;

    public async Task OpenSignAsync(IReadOnlyList<FileCheck> files)
    {
        if (files.Count == 0) return;
        if (Certificate is not { } cert)
        {
            await OpenCertificatesAsync();
            return;
        }
        var dialog = new SignDialogViewModel(_services, ProjectFolder, cert, Settings.EffectiveTimestampUrl, files, OnSignClosed);
        SignDialog = dialog;
        Overlay = Overlay.Sign;
        if (_services.Trust.IsTrusted(ProjectFolder, cert.Thumbprint))
            await dialog.StartTrustedAsync();
    }

    void OnSignClosed(SignSessionOutcome outcome)
    {
        Overlay = Overlay.None;
        SignDialog = null;
        if (outcome.Signed.Count > 0)
        {
            foreach (var result in outcome.Signed)
            {
                var row = Rows.FirstOrDefault(r => string.Equals(r.Path, result.Path, StringComparison.OrdinalIgnoreCase));
                if (row is null) continue;
                if (result.After is { } after) row.Check = after;
                row.Kind = result.After is null || result.After.State is FileState.Signed or FileState.SignedByOther
                    ? RowKind.JustSigned
                    : FileRowViewModel.KindOf(result.After);
            }
            foreach (var name in outcome.HeldInstallers)
                foreach (var row in Rows.Where(r => r.IsInstaller && string.Equals(r.FileName, name, StringComparison.OrdinalIgnoreCase)))
                    row.Kind = RowKind.HeldInstaller;
            Mode = ListMode.AfterSigning;
            foreach (var r in Rows) r.SetCheckedSilently(false);
            ApplyDefaultTicks();
            SelectedRow = Rows.FirstOrDefault();
        }
        Settings.Reload();
        RebuildDetail();
        Changed();
    }
}
