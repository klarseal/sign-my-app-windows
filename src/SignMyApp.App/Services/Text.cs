using System.Globalization;
using SignMyApp.Core;

namespace SignMyApp.App.Services;

/// <summary>
/// Every word the app shows, taken from the approved canvas. One place, so a translation
/// replaces this class and nothing else. Do not add words here without a design.
/// </summary>
public static class Text
{
    public const string AppTitle = "Sign my app";

    // First run
    public const string FirstRunTitle = "Check your app’s signature";
    public const string FirstRunLine = ".exe, .dll and .msi. No certificate needed.";
    public const string DropPrompt = "Drop files or a folder";
    public const string DropRelease = "Release to check";
    public const string DropWrongType = "Only .exe, .dll, .msi";
    public const string ChooseFiles = "Choose files";
    public const string ChooseFolder = "Choose folder";
    public const string NoAccountNoTelemetry = "No account. No telemetry.";
    public const string Settings = "Settings";

    // Toolbar and list
    public const string NoCertificate = "No certificate";
    public const string AddFiles = "Add files";
    public const string ColumnFile = "File";
    public const string ColumnStatus = "Status";
    public const string ColumnPublisher = "Publisher";
    public const string SelectAllFiles = "Select all files to sign";
    public const string PublisherNone = "None";
    public const string CheckAgain = "Check again";
    public const string GetCertificate = "Get a certificate ↗";
    public const string FindMyCertificate = "Find my certificate";
    public const string SignFilesDisabled = "Sign files…";
    public const string GetCertificateUrl = "https://klarseal.com/code-signing/";
    public const string ChipNotSigned = "not signed";
    public const string ChipNoTimestamp = "no timestamp";
    public const string ChipChanged = "changed";
    public const string ChipSigned = "signed";
    public const string ChipToRebuild = "to rebuild";
    public const string ChipCouldNotCheck = "could not check";

    // Status words
    public const string StatusNotSigned = "Not signed";
    public const string StatusNoTimestamp = "No timestamp";
    public const string StatusSigned = "Signed";
    public const string StatusChanged = "Changed since signing";
    public const string StatusChecking = "Checking…";
    public const string StatusWaiting = "Waiting";
    public const string StatusRebuildThenCheck = "Rebuild, then check";
    public const string StatusCouldNotCheck = "Could not check";

    // Detail panel
    public const string DetailUnknownPublisher = "Windows shows “Unknown publisher”.";
    public const string DetailNothingToDo = "Nothing to do.";
    public const string DetailTreatedAsNotSigned = "Windows treats it as not signed.";
    public const string DetailChangedWhy = "The file no longer matches its signature. If you built it, sign it again. If not, get a fresh copy.";
    public const string DetailChecked = "Signature and timestamp checked.";
    public const string DetailNotSignedYet = "Not signed yet";
    public const string DetailRebuild = "Rebuild it with the signed files.";
    public const string DetailRebuildWhy = "An installer built before signing still holds the unsigned copies of your app files.";
    public const string StepSignAppFiles = "Sign app files";
    public const string StepRebuildInstaller = "Rebuild the installer";
    public const string StepCheckAgain = "Check again, then sign it";
    public const string Why = "Why?";
    public const string FactPublisher = "Publisher";
    public const string FactIssuedBy = "Issued by";
    public const string FactValid = "Valid";
    public const string FactTimestamp = "Timestamp";
    public const string FactNone = "None";
    public const string Sha256 = "SHA-256";
    public const string CopySha256 = "Copy SHA-256";
    public const string Copy = "Copy";
    public const string SignIt = "Sign it";
    public const string SignAgain = "Sign again";

    // Certificate dialog
    public const string ChooseCertificate = "Choose a certificate";
    public const string Close = "Close";
    public const string CertificatesOnThisPc = "Certificates on this PC";
    public const string TimestampedFilesKeepWorking = "Timestamped files keep working.";
    public const string GetNewCertificate = "Get a new certificate ↗";
    public const string NoCertificateFound = "No certificate found";
    public const string SimplySign = "SimplySign";
    public const string LogInToSimplySignDesktop = "Log in to SimplySign Desktop";
    public const string UsbToken = "USB token";
    public const string PlugItIn = "Plug it in";
    public const string CertificateStore = "Certificate store";
    public const string ImportIt = "Import it";
    public const string LogInThenRefresh = "Log in to SimplySign Desktop, then refresh";
    public const string NeverAskSecrets = "We never ask for passwords, PINs or codes.";
    public const string Refresh = "Refresh";
    public const string Refreshing = "Refreshing…";
    public const string Cancel = "Cancel";
    public const string UseThisCertificate = "Use this certificate";

    // Sign window
    public const string Sign = "Sign";
    public const string SignOnce = "Sign once";
    public const string AlwaysForThisProject = "Always for this project";
    public const string WhatDoesAlwaysDo = "What does Always do?";
    public const string AlwaysExplained = "Signs files in this project without asking, while your session is open. Forget it in Settings.";
    public const string ShowFullSha = "Show full SHA-256";
    public const string HideFullSha = "Hide full SHA-256";
    public const string DetailCertificate = "Certificate";
    public const string DetailTimestamp = "Timestamp";
    public const string DetailProject = "Project";
    public const string DetailPublisher = "Publisher";
    public const string KeepSimplySignOpen = "Keep SimplySign Desktop open.";
    public const string SigningProgress = "Signing progress";
    public const string StopAfterThisFile = "Stop after this file";
    public const string ShowInFolder = "Show in folder";
    public const string BackToFiles = "Back to files";
    public const string StateSigning = "Signing…";
    public const string NothingSigned = "Nothing signed";
    public const string ErrSessionTitle = "Certificate session closed";
    public const string ErrSessionFix = "Log in to SimplySign Desktop, then sign again.";
    public const string ErrTimestampTitle = "Timestamp server not reachable";
    public const string ErrTimestampFix = "Check your connection, then try again.";
    public const string ErrChangedFix = "Review the files again.";
    public const string ErrInUseFix = "Close the program using it, then try again.";
    public const string TryAgain = "Try again";
    public const string ReviewAgain = "Review again";

    // Settings
    public const string Files = "Files";
    public const string TrustHeading = "Always for this project";
    public const string TrustLine = "Signed without asking while your session is open.";
    public const string Forget = "Forget";
    public const string Keep = "Keep";
    public const string TimestampServer = "Timestamp server";
    public const string TimestampServerAddress = "Timestamp server address";
    public const string UseDefault = "Use default";
    public const string NotAValidAddress = "Not a valid address";
    public const string Language = "Language";

    // Patterns
    public static string Plural(int n, string word) => n + " " + word + (n == 1 ? "" : "s");
    public static string SignNFiles(int n) => n == 1 ? "Sign 1 file…" : "Sign " + n + " files…";
    public static string NOfTotalSelected(int n, int total) => n + " of " + total + " selected";
    public static string NOfTotalChecked(int n, int total) => n + " of " + total + " checked";
    public static string SignWindowTitle(int n) => "Sign " + Plural(n, "file");
    public static string SigningTitle(int n) => "Signing " + Plural(n, "file");
    public static string SignedTitle(int n) => Plural(n, "file") + " signed";
    public static string NOfTotal(int n, int total) => n + " of " + total;
    public static string NOfTotalSigned(int n, int total) => n + " of " + total + " signed";
    public static string NextRebuild(string msi) => "Next: rebuild " + msi;
    public static string NowAppFiles(int n) => "Now: " + Plural(n, "app file");
    public static string ThenRebuild(string msi) => "Then: rebuild " + msi;
    public static string LastSign(string msi) => "Last: sign " + msi;
    public static string ChangedAfterReview(string file) => file + " changed after review";
    public static string IsInUse(string file) => file + " is in use";
    public static string NotSignedTitle(string file) => file + " not signed";
    public static string SignFileLabel(string file) => "Sign " + file;
    public static string ForgetQuestionPrefix => "Forget ";
    public static string ForgetGroupLabel(string folder) => "Forget " + folder;
    public static string SignatureValidUntil(DateTimeOffset d) => "Signature valid until " + Date(d) + ".";
    public static string NoTimestampWhy(DateTimeOffset d) =>
        "The certificate expires on " + Date(d) + ". A timestamp proves the file was signed before that, so the signature stays valid.";
    public static string Until(DateTimeOffset d) => "Until " + Date(d);
    public static string Expired(DateTimeOffset d) => "Expired " + Date(d);
    public static string CertificateLine(SigningCertificate c) => Source(c.Source) + " · until " + Date(c.NotAfter);
    public static string Thumbprint(string full) => "Thumbprint " + full;
    public static string CertAria(SigningCertificate? c) =>
        c is null ? "No certificate. Choose a certificate" : c.Publisher + ", " + Source(c.Source) + ". Change certificate";
    public static string Chip(int n, string what) => n + " " + what;
    public static string VersionLine(string version) => "Sign my app " + version + " · Klarseal · No account, no telemetry";

    public static string Source(CertSource s) => s switch
    {
        CertSource.SimplySign => SimplySign,
        CertSource.Token => UsbToken,
        _ => CertificateStore,
    };

    public static string Date(DateTimeOffset d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    public static string Range(DateTimeOffset from, DateTimeOffset to) => Date(from) + " – " + Date(to);
    public static string Stamp(DateTimeOffset d) => d.ToUniversalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>12 + … + 12, as the detail panel draws it.</summary>
    public static string ShortSha12(string sha) => sha.Length <= 26 ? sha : sha[..12] + "…" + sha[^12..];
    /// <summary>8 + … + 8, as the Sign window draws it.</summary>
    public static string ShortSha8(string sha) => sha.Length <= 18 ? sha : sha[..8] + "…" + sha[^8..];
    /// <summary>4 + … + 4, as the certificate list draws a thumbprint.</summary>
    public static string ShortThumb(string t) => t.Length <= 10 ? t : t[..4] + "…" + t[^4..];

    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
        [("en", "English"), ("de", "Deutsch"), ("nb", "Norsk bokmål")];
}
