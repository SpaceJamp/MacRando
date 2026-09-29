[CmdletBinding()]
param(
    [string]$Version = '1.4.3',
    [string]$SignThumbprint,
    [string]$TimestampServer,
    # Also lay out an unzipped folder that can be copied and run without installing.
    [switch]$Portable
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$zip = Join-Path $dist ("MacRando-{0}.zip" -f $Version)

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$buildArguments = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $root 'build.ps1')
)
if (-not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
    $buildArguments += @('-SignThumbprint', $SignThumbprint)
    if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
        $buildArguments += @('-TimestampServer', $TimestampServer)
    }
}
& powershell.exe @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "MacRando build failed with exit code $LASTEXITCODE."
}

$files = @(
    (Join-Path $root 'bin\MacRando.exe'),
    (Join-Path $root 'bin\MacRando.ico'),
    (Join-Path $root 'bin\MacRandoTray.ico'),
    (Join-Path $root 'README.md'),
    (Join-Path $root 'CHANGELOG.md'),
    (Join-Path $root 'LICENSE')
)
foreach ($file in $files) {
    if (-not (Test-Path $file)) {
        throw "Required release file is missing: $file"
    }
}

if (Test-Path $zip) {
    Remove-Item $zip -Force
}
Compress-Archive -Path $files -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("Created " + $zip)

if ($Portable) {
    $portableRoot = Join-Path $dist ("MacRando-{0}-portable" -f $Version)
    if (Test-Path $portableRoot) {
        Remove-Item $portableRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $portableRoot | Out-Null
    foreach ($file in $files) {
        Copy-Item $file -Destination $portableRoot -Force
    }

    # Ship the public certificate next to the executable so a tester can trust the
    # publisher without a second download. The private key is never exported.
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $root 'bin\MacRando.exe')
    if ($signature -and $signature.SignerCertificate) {
        $cerPath = Join-Path $portableRoot 'MacRando-Public.cer'
        [System.IO.File]::WriteAllBytes(
            $cerPath,
            $signature.SignerCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Copy-Item (Join-Path $root 'trust-certificate.ps1') -Destination $portableRoot -Force
        Write-Host ("Included the public signing certificate: " + $cerPath)
    }
    else {
        Write-Warning 'The executable is not signed, so no public certificate was included.'
    }

    $instructions = @"
MacRando $Version - portable folder
====================================

Run MacRando.exe directly. No installer is required.

Windows will show an unknown-publisher warning because this build is signed with a
self-signed development certificate. To trust it on this machine only:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\trust-certificate.ps1 -Install

Read the script first; it does nothing without -Install, and it never handles the
private key.

User data (settings, restore profiles, notification history, logs) is stored per user in:

    %LOCALAPPDATA%\MacRando

Deleting this folder removes the application. Deleting that user-data folder removes
settings and any pending restore profile, so restore adapters before doing so.

Licensed under the Apache License, Version 2.0. See LICENSE.
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $portableRoot 'PORTABLE-README.txt'),
        $instructions,
        (New-Object System.Text.UTF8Encoding($false)))

    Write-Host ("Created " + $portableRoot)
}
