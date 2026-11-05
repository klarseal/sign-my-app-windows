# Sign my app for Windows

.NET 10 Windows WPF app, engine and CLI for checking and signing `.exe`, `.dll` and `.msi` files. Signing uses Microsoft SignTool, a certificate thumbprint in `CurrentUser\My`, SHA-256 and an RFC 3161 timestamp. No account or telemetry.

From the repository root, with the .NET 10 SDK and Windows SDK SignTool installed:

```text
dotnet build apps/signapp/SignMyApp.slnx -warnaserror
dotnet test apps/signapp/SignMyApp.slnx
dotnet run --project apps/signapp/src/SignMyApp.App -- --demo
dotnet run --project apps/signapp/src/SignMyApp.Cli -- check C:\build\MyApp.exe
dotnet run --project apps/signapp/src/SignMyApp.Cli -- sign --thumbprint <40-hex-thumbprint> C:\build\MyApp.exe
```

The CLI requires exact `SIGN` input or `--yes-i-reviewed <planhash>`. See `--help` for project, timestamp and explicit installer/other-publisher selection. Exit codes: 0 success, 1 check/signing failure, 2 invalid input or declined review, 130 cancellation.

Core entry points: `SignatureChecker`, `CertificateFinder`, `SignToolLocator`, `Signer`, `ProjectTrustStore`, `SettingsStore`. Shared contracts are in `Contracts.cs`. Settings and exact folder/thumbprint trust pairs share `%LOCALAPPDATA%\Klarseal\SignMyApp\settings.json`. Trust skips only confirmation; callers still compute a review.

Checking uses offline WinVerifyTrust and embedded PKCS#7 metadata. Untrusted signatures are `Error`. Signing verifies a reviewed snapshot, then hashes and writes the original through a handle denying concurrent writes/deletion. Cancellation discards snapshots; a final write already in progress finishes.

Tests open no windows, add no certificates to Windows stores, and clean temporary files/PFXs. PE fixture: a copy of the installed .NET host pack's unsigned `apphost.exe` (10.0.11 on this PC). MSI fixture: a temporary copy of `C:\Windows\Installer\103f2bbb.msi`, with signature streams removed through Windows structured storage. The original MSI and `C:\Program Files\dotnet\dotnet.exe` are checked read-only for trusted signatures/timestamps. These integration tests require those installed fixtures. A test-only untrusted-root override permits self-signed verification. Network tests skip unless `SIGNMYAPP_NET_TESTS=1`. SignTool PFX tests may need to run outside a restricted sandbox for Windows cryptography access.

The solution includes both the WPF app and its tests. `--demo` opens the approved screens with in-memory sample files, certificates, signing and settings. Omit `--demo` to use the real engine. These run commands open the app; build and test commands do not. App render tests measure and render views off-screen at 1280×800 and 1024×640, writing PNGs to `tests/artifacts/`. Assembly scans cover App, Core and CLI for run-time compilation references.

The title bar and branding follow [approved canvas v43](https://claude.ai/artifact/DGSJiqDNzFxyLXhhbHj6KX), especially `Main.dc.html`, `SignDialog.dc.html` and board 4.6 (`TitleBar.dc.html`). `WindowChrome` keeps native drag, double-click, system menu and snap behaviour. The custom maximize/restore button returns `HTMAXBUTTON` in screen pixels for Windows 11 Snap layouts; its non-client mouse handling preserves hover, press, release-outside cancellation and mouse capture. Maximized content uses `SystemParameters.WindowResizeBorderThickness` to avoid the invisible frame overhang. The project header has no mark; the Sign header uses the original ink vector path.

`Assets/klarseal.ico` is committed with PNG frames at 16, 24, 32, 48 and 256 px, embedded both as the executable icon and as the WPF window icon. Re-author it off-screen from `D:\Archi\Klarseal\marketing\brand-assets\klarseal-mark-square-white-on-black.svg` using `powershell -NoProfile -NonInteractive -STA -File apps/signapp/build/New-AppIcon.ps1` (optional `-SourceSvg` and `-Output`). This uses installed WPF geometry/bitmap APIs only; the app performs no SVG conversion or run-time compilation.

Full-window render tests now include the production `TitleBar` control. `4.6-title-bar-states.png` reproduces the six approved caption rows; `4.6-maximize-restore-states.png` also checks both maximize/restore glyphs in active, hover and pressed states. Tests exercise styling, accessibility, insets, signed-coordinate hit mapping and each embedded icon frame without creating an HWND. Native shell gestures and the OS flyout are implemented but are deliberately not exercised by opening a window.

App preferences (timestamp address and language), CLI preferences and project trust use Core's single settings document; there is no `preferences.json` or migration. Saving preferences preserves trusted projects, and forgetting a project preserves preferences. The App integration tests reuse Core's test-only PFX helper because the production Signer deliberately accepts only a store thumbprint. Offline tests verify “Not signed”, untrusted-root errors and “No timestamp”. Set `$env:SIGNMYAPP_NET_TESTS = "1"` in PowerShell before `dotnet test` to also verify the unsigned-apphost → timestamped signature → “Signed” view-model flow against the timestamp service. No certificate is installed in a store.

## Release

Publish an unsigned, self-contained single-file Windows x64 app from the repository root:

```text
dotnet publish apps/signapp/src/SignMyApp.App/SignMyApp.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o apps/signapp/publish/win-x64
```

The output is `apps/signapp/publish/win-x64/SignMyApp.exe`. The founder signs releases separately; publishing does not sign the app. Microsoft SignTool and the signing certificate/provider must still be installed on the PC used to sign files.
