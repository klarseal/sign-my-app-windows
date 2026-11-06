# Windows release packaging

`Publish-SignMyApp.ps1` builds an **unsigned**, compressed, self-contained, single-file
Windows x64 executable by default. Use `-FrameworkDependent` for a smaller download
that requires the **Windows x64 .NET 10 Desktop Runtime** on the destination PC
(the base .NET Runtime alone is insufficient). It never launches the app or runs
SignTool. No certificate, private
key, `.env` file, account or signing credentials are read by the script.

Use Windows PowerShell 5.1 or later, Git and the .NET 10 SDK. The script resolves
paths from its own location, so it can be called from any working directory.
Examples below assume the repository root.

First commit the release inputs. Both normal publishing and `-WhatIf` refuse any
staged, unstaged or untracked changes in the repository, printing the affected
paths. Git-ignored
build outputs are not release inputs and are not reported by this check.

## Preview and package

Supply a version such as `1.0.0` or `1.0.0-rc.1` (three numeric components, optional
prerelease suffix) and the signing certificate's 40 hexadecimal character SHA-1
thumbprint without spaces. The thumbprint is only used in printed instructions.
Replace the example thumbprint before signing.

```powershell
# Prints the publish, rename, checksum and manual signing commands; writes nothing.
.\build\Publish-SignMyApp.ps1 -Version 1.0.0 -Thumbprint AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA -WhatIf

# Publishes and hashes the unsigned executable; prints signing commands only.
.\build\Publish-SignMyApp.ps1 -Version 1.0.0 -Thumbprint AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA

# Smaller package for PCs with the Windows x64 .NET 10 Desktop Runtime installed.
.\build\Publish-SignMyApp.ps1 -Version 1.0.0 -Thumbprint AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA -FrameworkDependent
```

The publish command uses `-c Release -r win-x64 --self-contained true`,
`-p:PublishSingleFile=true`, `-p:IncludeNativeLibrariesForSelfExtract=true`,
`-p:EnableCompressionInSingleFile=true`, `-p:DebugType=none`,
`-p:SatelliteResourceLanguages=en`, `-p:PublishTrimmed=false` and
`-p:Version=<version>`. The app ships English text only; no German or Norwegian
satellite resources are present. WPF publishing is not trimmed, and no PDBs are
produced. `-FrameworkDependent` changes `--self-contained` and
`EnableCompressionInSingleFile` to `false`: the SDK supports bundle compression
only for self-contained applications.

Default output goes to `out/<version>/`; framework-dependent output
goes to `out/<version>-framework-dependent/`, so both can coexist. An
additional
`--artifacts-path` places build intermediates in that directory's `obj/` subtree
so publishing does not write into `src` or `tests`. SDK restore may download NuGet
packages and runtime packs. There is no run-time compilation or `Add-Type`.

Release files:

- `SignMyApp-<version>-win-x64.exe`, or
  `SignMyApp-<version>-win-x64-framework-dependent.exe` with `-FrameworkDependent`
  (renamed from the published `SignMyApp.exe`).
- `SHA256SUMS.txt` (lowercase SHA-256, two spaces, executable filename; ASCII).

Only distribute these two files. Other SDK outputs, including `obj/` and debug
symbols, are build artifacts. The initial checksum describes the **unsigned**
executable. Publishing does not require a signing certificate or SignTool.

An existing version output directory is never overwritten. Failed publishing
may leave partial output; move it aside before retrying. Release outputs are ignored under `out/`.
Do not commit binaries to clear the clean-tree check.

The normal test suite checks the icon on the app project's built executable.
To also check the default self-contained single-file executable's large and small
icons, commit the release inputs and run from `apps/signapp`:

```powershell
.\build\Publish-SignMyApp.ps1 -Version 0.0.0-icon-test -Thumbprint AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
$env:SIGNMYAPP_PUBLISH_TESTS = '1'
dotnet test SignMyApp.slnx
Remove-Item Env:SIGNMYAPP_PUBLISH_TESTS
```

The opt-in test inspects `out/0.0.0-icon-test/SignMyApp-0.0.0-icon-test-win-x64.exe`
without launching it and fails with packaging instructions if it is missing.
Package before running the suite: render tests can update tracked artifacts,
and the publish script requires a clean tree. No signing is performed.

## Measured download sizes

Unsigned version `1.0.0`, Release/win-x64, measured on 2026-11-05 with .NET SDK
10.0.400 (self-contained runtime 10.0.11), using one successful publish per option
into separate temporary folders:

| Package | Executable bytes | Size (MiB) | Destination PC requirement |
| --- | ---: | ---: | --- |
| Self-contained, compressed (default) | 61,571,247 | 58.72 | No separate .NET installation |
| Framework-dependent | 583,261 | 0.56 | Windows x64 .NET 10 Desktop Runtime |

The previous self-contained download was reported as 133 MiB. Sizes above measure
the executable only (1 MiB = 1,048,576 bytes), before signing; SDK/runtime updates
and signing can change them. Both publish outputs were checked and contained no
PDB files. Neither executable was launched.

## Founder signs in a separate terminal

Install Windows SDK SignTool and make it available on `PATH`. Use your own
PowerShell terminal with the certificate in `CurrentUser\My` and its provider
available. The packaging script prints the exact absolute paths and supplied
thumbprint for this sequence:

```powershell
signtool sign /sha1 AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA /fd sha256 /tr '<timestamp-server-url>' /td sha256 'C:\path\to\repo\out\1.0.0\SignMyApp-1.0.0-win-x64.exe'
if ($LASTEXITCODE -ne 0) { throw 'Signing failed; stop here.' }
signtool verify /pa /v 'C:\path\to\repo\out\1.0.0\SignMyApp-1.0.0-win-x64.exe'
if ($LASTEXITCODE -ne 0) { throw 'Signature verification failed; stop here.' }
Get-FileHash -LiteralPath 'C:\path\to\repo\out\1.0.0\SignMyApp-1.0.0-win-x64.exe' -Algorithm SHA256 | ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), 'SignMyApp-1.0.0-win-x64.exe' } | Set-Content -LiteralPath 'C:\path\to\repo\out\1.0.0\SHA256SUMS.txt' -Encoding Ascii
```

Run the printed commands only after a real publish. Signing changes the file;
regenerate `SHA256SUMS.txt` **after successful signature verification**, then attach
the signed executable and that checksum file to the release. Fill in
`release-notes-template.md` with the release's actual details.
