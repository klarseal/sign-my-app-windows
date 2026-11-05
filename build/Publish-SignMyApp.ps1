#Requires -Version 5.1
<#
.SYNOPSIS
Packages an unsigned Windows x64 release and prints the founder's signing commands.
.EXAMPLE
.\Publish-SignMyApp.ps1 -Version 1.0.0 -Thumbprint ('A' * 40) -WhatIf
.EXAMPLE
.\Publish-SignMyApp.ps1 -Version 1.0.0 -Thumbprint ('A' * 40) -FrameworkDependent
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string] $Thumbprint,

    # Smaller download; requires the Windows x64 .NET 10 Desktop Runtime.
    [switch] $FrameworkDependent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-PowerShellLiteral {
    param([string] $Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\SignMyApp.App'
$variantSuffix = if ($FrameworkDependent) { '-framework-dependent' } else { '' }
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$outputDirectory = Join-Path $repoRoot ('out\' + $Version + $variantSuffix)
# Keep all compiler/restore intermediates outside src and tests, including WPF builds.
$artifactsDirectory = Join-Path $outputDirectory 'obj'
$fileName = 'SignMyApp-{0}-win-x64{1}.exe' -f $Version, $variantSuffix
$publishedExe = Join-Path $outputDirectory 'SignMyApp.exe'
$releaseExe = Join-Path $outputDirectory $fileName
$checksumPath = Join-Path $outputDirectory 'SHA256SUMS.txt'

$git = Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1
$changes = @(& $git.Source -C $repoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Cannot check Git status. Run this script from its Git checkout.'
}
if ($changes.Count -gt 0) {
    Write-Host 'Refusing to package: the repository has uncommitted changes (staged, unstaged or untracked):'
    $changes | ForEach-Object { Write-Host $_ }
    throw 'Commit or remove those changes before packaging. No publish or signing was performed.'
}
if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
    throw ('App project directory not found: ' + $projectPath)
}
if (Test-Path -LiteralPath $outputDirectory) {
    throw ('Output already exists; move it aside before packaging: ' + $outputDirectory)
}

$publishArguments = @(
    'publish', $projectPath,
    '-c', 'Release', '-r', 'win-x64', '--self-contained', $selfContained,
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    # The SDK supports bundle compression only for self-contained applications.
    ('-p:EnableCompressionInSingleFile=' + $selfContained), '-p:DebugType=none',
    '-p:SatelliteResourceLanguages=en', '-p:PublishTrimmed=false',
    ('-p:Version=' + $Version),
    '--output', $outputDirectory, '--artifacts-path', $artifactsDirectory
)
$publishCommand = 'dotnet ' + (($publishArguments | ForEach-Object { ConvertTo-PowerShellLiteral $_ }) -join ' ')
$hashCommand = 'Get-FileHash -LiteralPath ' + (ConvertTo-PowerShellLiteral $releaseExe) +
    ' -Algorithm SHA256 | ForEach-Object { ''{0}  {1}'' -f $_.Hash.ToLowerInvariant(), ' +
    (ConvertTo-PowerShellLiteral $fileName) + ' } | Set-Content -LiteralPath ' +
    (ConvertTo-PowerShellLiteral $checksumPath) + ' -Encoding Ascii'

Write-Host 'Packaging commands (the executable remains unsigned):'
if ($FrameworkDependent) {
    Write-Host 'This package requires the Windows x64 .NET 10 Desktop Runtime on the destination PC.'
}
Write-Host ('New-Item -ItemType Directory -Path ' + (ConvertTo-PowerShellLiteral $outputDirectory))
Write-Host $publishCommand
Write-Host ('Move-Item -LiteralPath ' + (ConvertTo-PowerShellLiteral $publishedExe) + ' -Destination ' + (ConvertTo-PowerShellLiteral $releaseExe))
Write-Host $hashCommand

if ($PSCmdlet.ShouldProcess($outputDirectory, 'Publish unsigned Windows x64 release and write SHA256SUMS.txt')) {
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    & $dotnet.Source @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed. No checksum was written; move the partial output aside before retrying.'
    }
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
        throw ('Publish did not produce the expected executable: ' + $publishedExe)
    }
    Move-Item -LiteralPath $publishedExe -Destination $releaseExe
    $hash = (Get-FileHash -LiteralPath $releaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($checksumPath, ($hash + '  ' + $fileName + [Environment]::NewLine), [System.Text.Encoding]::ASCII)
    Write-Host ('Packaged unsigned executable: {0} ({1:N0} bytes)' -f $releaseExe, (Get-Item -LiteralPath $releaseExe).Length)
}

Write-Host ''
Write-Host 'Founder: run these commands in your own PowerShell terminal after packaging. These are printed ONLY.'
Write-Host 'Use a Windows SDK terminal where signtool is on PATH and your signing certificate/provider is available.'
Write-Host ('signtool sign /sha1 {0} /fd sha256 /tr http://time.certum.pl /td sha256 {1}' -f (ConvertTo-PowerShellLiteral $Thumbprint), (ConvertTo-PowerShellLiteral $releaseExe))
Write-Host 'if ($LASTEXITCODE -ne 0) { throw ''Signing failed; stop here.'' }'
Write-Host ('signtool verify /pa /v ' + (ConvertTo-PowerShellLiteral $releaseExe))
Write-Host 'if ($LASTEXITCODE -ne 0) { throw ''Signature verification failed; stop here.'' }'
Write-Host $hashCommand
Write-Host 'Signing changes the executable. Distribute SHA256SUMS.txt only after regenerating it for the signed file.'
