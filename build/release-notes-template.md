# Sign my app for Windows <version>

Release date: <YYYY-MM-DD>
Source commit: <full commit SHA>

## Changes

- <Describe changes included in this release.>

## Download

- Windows x64: `SignMyApp-<version>-win-x64.exe`
- Checksums: `SHA256SUMS.txt`
- Size: <bytes or MiB>

Requires Windows x64. The executable includes its .NET runtime; signing files
requires Microsoft SignTool and an available code-signing certificate/provider.

## Release verification

Complete these entries only after the founder signs and verifies the release:

- Publisher: <publisher shown by signature verification>
- Signing certificate thumbprint: <SHA-1 thumbprint>
- Signature verification: <result of signtool verify /pa /v>
- RFC 3161 timestamp: <verified timestamp>
- SHA-256 of the **signed** executable: <hash matching SHA256SUMS.txt>

To compare the downloaded executable with `SHA256SUMS.txt` in PowerShell:

```powershell
Get-FileHash -LiteralPath '.\SignMyApp-<version>-win-x64.exe' -Algorithm SHA256
```

## Known issues

- <Known issues or "None known".>
