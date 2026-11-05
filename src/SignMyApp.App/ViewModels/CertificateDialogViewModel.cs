using SignMyApp.App.Services;
using SignMyApp.Core;

namespace SignMyApp.App.ViewModels;

public enum CertDialogKind { Several, None, LoggedOut, Expired }

public sealed class CertificateRowViewModel : ObservableObject
{
    bool _isSelected;

    public CertificateRowViewModel(SigningCertificate cert)
    {
        Certificate = cert;
        IsUsable = IsUsableCert(cert);
    }

    public SigningCertificate Certificate { get; }
    public bool IsUsable { get; }
    public string Name => Certificate.Publisher;
    public string ValidText => IsUsable ? Text.Until(Certificate.NotAfter) : Text.Expired(Certificate.NotAfter);
    public Tone Tone => IsUsable ? Tone.Success : Tone.Neutral;
    public string SourceText => "· " + Text.Source(Certificate.Source);
    public string ThumbShort => Text.ShortThumb(Certificate.Thumbprint);
    public string ThumbTooltip => Text.Thumbprint(Certificate.Thumbprint);

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value && IsUsable);
    }

    public static bool IsUsableCert(SigningCertificate c) => !c.IsExpired && c.HasPrivateKey;
}

/// <summary>Choose a certificate: several, none found, SimplySign logged out, or only an expired one.</summary>
public sealed class CertificateDialogViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly Action<SigningCertificate?> _close;
    CertDialogKind _kind;
    bool _isRefreshing;
    IReadOnlyList<CertificateRowViewModel> _rows = [];

    public CertificateDialogViewModel(AppServices services, IReadOnlyList<SigningCertificate> found, SigningCertificate? current, Action<SigningCertificate?> close)
    {
        _services = services;
        _close = close;
        UseCommand = new RelayCommand(Use, () => SelectedRow is not null);
        CancelCommand = new RelayCommand(() => _close(null));
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsRefreshing);
        OpenGetCertificateCommand = new RelayCommand(() => _services.Shell.OpenUrl(Text.GetCertificateUrl));
        SelectRowCommand = new RelayCommand(p => { if (p is CertificateRowViewModel r) Select(r); });
        Load(found, current);
    }

    public IReadOnlyList<SigningCertificate> Found { get; private set; } = [];

    public CertDialogKind Kind
    {
        get => _kind;
        private set { if (Set(ref _kind, value)) OnAllChanged(); }
    }

    public IReadOnlyList<CertificateRowViewModel> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    public CertificateRowViewModel? SelectedRow => Rows.FirstOrDefault(r => r.IsSelected);

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (Set(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(RefreshLabel));
                RefreshCommand.Refresh();
            }
        }
    }

    public string RefreshLabel => IsRefreshing ? Text.Refreshing : Text.Refresh;

    public bool ShowList => Kind is CertDialogKind.Several or CertDialogKind.Expired;
    public bool IsExpired => Kind == CertDialogKind.Expired;
    public bool IsNone => Kind == CertDialogKind.None;
    public bool IsLoggedOut => Kind == CertDialogKind.LoggedOut;
    public bool ShowRefreshGhost => ShowList;
    public bool ShowRefreshPrimary => !ShowList;
    public bool ShowUse => ShowList;
    public bool CanUse => SelectedRow is not null;

    public RelayCommand UseCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand OpenGetCertificateCommand { get; }
    public RelayCommand SelectRowCommand { get; }

    public void Select(CertificateRowViewModel row)
    {
        if (!row.IsUsable) return;
        foreach (var r in Rows) r.IsSelected = ReferenceEquals(r, row);
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(CanUse));
        UseCommand.Refresh();
    }

    void Use()
    {
        if (SelectedRow is { } r) _close(r.Certificate);
    }

    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var current = SelectedRow?.Certificate;
            var found = await _services.Finder.FindAsync(CancellationToken.None);
            Load(found, current);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Load([], null);
        }
        finally { IsRefreshing = false; }
    }

    void Load(IReadOnlyList<SigningCertificate> found, SigningCertificate? current)
    {
        Found = found;
        var usable = found.Where(CertificateRowViewModel.IsUsableCert).ToList();
        var unusable = found.Where(c => !CertificateRowViewModel.IsUsableCert(c)).ToList();
        var rows = usable.Concat(unusable).Select(c => new CertificateRowViewModel(c)).ToList();
        var pick = rows.FirstOrDefault(r => r.IsUsable && current is not null && string.Equals(r.Certificate.Thumbprint, current.Thumbprint, StringComparison.OrdinalIgnoreCase))
                   ?? rows.FirstOrDefault(r => r.IsUsable);
        if (pick is not null) pick.IsSelected = true;
        Rows = rows;
        Kind = usable.Count > 0 ? CertDialogKind.Several
            : unusable.Count > 0 ? CertDialogKind.Expired
            : _services.SimplySign.IsDesktopRunning() ? CertDialogKind.LoggedOut
            : CertDialogKind.None;
        OnAllChanged();
        UseCommand.Refresh();
    }

    /// <summary>The certificate the app picks by itself: the first usable one, SimplySign first.</summary>
    public static SigningCertificate? DefaultChoice(IReadOnlyList<SigningCertificate> found) =>
        found.Where(CertificateRowViewModel.IsUsableCert)
             .OrderBy(c => c.Source == CertSource.SimplySign ? 0 : c.Source == CertSource.Token ? 1 : 2)
             .FirstOrDefault();
}
