using System.IO;
using System.Collections.ObjectModel;
using SignMyApp.App.Services;
using SignMyApp.Core;

namespace SignMyApp.App.ViewModels;

public sealed class TrustRowViewModel : ObservableObject
{
    readonly SettingsViewModel _owner;
    bool _isConfirming;

    public TrustRowViewModel(SettingsViewModel owner, string folder, string thumbprint, string line)
    {
        _owner = owner;
        Folder = folder;
        Thumbprint = thumbprint;
        Line = line;
        AskCommand = new RelayCommand(() => _owner.BeginForget(this));
        KeepCommand = new RelayCommand(() => _owner.Keep(this));
        ForgetCommand = new RelayCommand(() => _owner.Forget(this));
    }

    public string Folder { get; }
    public string Thumbprint { get; }
    public string Line { get; }
    public string GroupLabel => Text.ForgetGroupLabel(Folder);
    public string KeepAutomationName => $"{Text.Keep} {Folder}";
    public string ForgetAutomationName => $"{Text.Forget} {Folder}";

    public bool IsConfirming
    {
        get => _isConfirming;
        set { if (Set(ref _isConfirming, value)) OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !IsConfirming;

    public RelayCommand AskCommand { get; }
    public RelayCommand KeepCommand { get; }
    public RelayCommand ForgetCommand { get; }
}

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Settings: projects signed without asking (Forget with a confirm), the timestamp server, the language.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    readonly AppServices _services;
    readonly AppSettings _settings;
    readonly Func<IReadOnlyList<SigningCertificate>> _knownCertificates;
    string _timestampUrl;
    LanguageOption _language;

    public SettingsViewModel(AppServices services, AppSettings settings, Func<IReadOnlyList<SigningCertificate>> knownCertificates)
    {
        _services = services;
        _settings = settings;
        _knownCertificates = knownCertificates;
        _timestampUrl = settings.TimestampUrl;
        Languages = Text.Languages.Select(l => new LanguageOption(l.Code, l.Name)).ToList();
        _language = Languages.FirstOrDefault(l => l.Code == settings.Language) ?? Languages[0];
        UseDefaultCommand = new RelayCommand(UseDefault);
        CommitTimestampCommand = new RelayCommand(CommitTimestamp);
        Reload();
    }

    public static string DefaultTimestampUrl => new AppSettings().TimestampUrl;

    public ObservableCollection<TrustRowViewModel> TrustRows { get; } = [];
    public event Action<TrustRowViewModel?>? FocusRequested;
    public event Action<TrustRowViewModel>? ConfirmFocusRequested;

    public IReadOnlyList<LanguageOption> Languages { get; }

    public string VersionLine => Text.VersionLine(_services.Version);

    /// <summary>The address being edited. Saved only when it is a valid http or https address.</summary>
    public string TimestampUrl
    {
        get => _timestampUrl;
        set
        {
            if (Set(ref _timestampUrl, value))
            {
                OnPropertyChanged(nameof(IsTimestampInvalid));
                OnPropertyChanged(nameof(TimestampError));
                CommitTimestamp();
            }
        }
    }

    public bool IsTimestampInvalid => !IsValidAddress(TimestampUrl);
    public string TimestampError => IsTimestampInvalid ? Text.NotAValidAddress : "";

    /// <summary>The address signing uses: the saved one.</summary>
    public string EffectiveTimestampUrl => _settings.TimestampUrl;

    public LanguageOption Language
    {
        get => _language;
        set
        {
            if (value is null || !Set(ref _language, value)) return;
            _settings.Language = value.Code;
            Save();
        }
    }

    public RelayCommand UseDefaultCommand { get; }
    public RelayCommand CommitTimestampCommand { get; }

    public static bool IsValidAddress(string? url) => SettingsStore.IsValidTimestampUrl(url?.Trim());

    public void Reload()
    {
        TrustRows.Clear();
        var certs = _knownCertificates();
        foreach (var (folder, thumb) in _services.Trust.List())
        {
            var cert = certs.FirstOrDefault(c => string.Equals(c.Thumbprint, thumb, StringComparison.OrdinalIgnoreCase));
            var line = cert is null ? Text.ShortThumb(thumb) : cert.Publisher + " · " + Text.Source(cert.Source);
            TrustRows.Add(new TrustRowViewModel(this, folder, thumb, line));
        }
    }

    internal void BeginForget(TrustRowViewModel row)
    {
        foreach (var r in TrustRows) r.IsConfirming = ReferenceEquals(r, row);
        ConfirmFocusRequested?.Invoke(row);
    }

    internal void Forget(TrustRowViewModel row)
    {
        var index = TrustRows.IndexOf(row);
        _services.Trust.Forget(row.Folder);
        TrustRows.Remove(row);
        FocusRequested?.Invoke(index >= 0 && index < TrustRows.Count ? TrustRows[index] : null);
    }

    internal void Keep(TrustRowViewModel row)
    {
        row.IsConfirming = false;
        FocusRequested?.Invoke(row);
    }

    void UseDefault() => TimestampUrl = DefaultTimestampUrl;

    void CommitTimestamp()
    {
        if (!IsValidAddress(TimestampUrl)) return;
        var trimmed = TimestampUrl.Trim();
        if (_settings.TimestampUrl == trimmed) return;
        _settings.TimestampUrl = trimmed;
        Save();
        OnPropertyChanged(nameof(EffectiveTimestampUrl));
    }

    void Save()
    {
        try { _services.Settings.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}
