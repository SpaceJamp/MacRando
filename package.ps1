[CmdletBinding()]
param(
    # Overridden by release.ps1, which takes the version from src\Models.cs so the
    # binary, the manifest, and the installer can never disagree.
    [string]$Version = '',
    [string]$SignThumbprint,
    [string]$TimestampServer,
    # Also lay out an unzipped folder that can be copied and run without installing.
    [switch]$Portable
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# A single source of truth for the version. Every artefact is named from this, so the
# installer cannot claim a different version from the executable it installs, and a
# default in a parameter list cannot go stale against a release.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $declared = Select-String -Path (Join-Path $root 'src\Models.cs') -Pattern 'Version\s*=\s*"([^"]+)"' |
        Select-Object -First 1
    if (-not $declared) {
        throw 'Could not read the version from src\Models.cs.'
    }
    $Version = $declared.Matches[0].Groups[1].Value
    Write-Host ("Version read from src\Models.cs: " + $Version)
}

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

# Export the public certificate next to the signed executable, so both the portable
# folder and the installer can offer it without a second download. The private key is
# never exported; only the public part is written.
$publicCert = Join-Path $dist 'MacRando-Public.cer'
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $root 'bin\MacRando.exe')
if ($signature -and $signature.SignerCertificate) {
    [System.IO.File]::WriteAllBytes(
        $publicCert,
        $signature.SignerCertificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
}
else {
    Write-Warning 'The executable is not signed, so no public certificate was exported.'
    if (Test-Path $publicCert) { Remove-Item $publicCert -Force }
}

# The installer, when Inno Setup is available. Skipped with a clear message rather than
# failing, so a contributor without Inno Setup can still build and package.
$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)
$iscc = $innoCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    $setupOutput = & $iscc "/DAppVersion=$Version" (Join-Path $root 'installer.iss') 2>&1
    if ($LASTEXITCODE -ne 0) {
        $setupOutput | ForEach-Object { Write-Host $_ }
        throw "The installer failed to compile with exit code $LASTEXITCODE."
    }
    $setupPath = Join-Path $dist ("MacRando-{0}-setup.exe" -f $Version)
    Write-Host ("Created " + $setupPath)

    # The installer must be signed, not only the executable inside it. It is the file a
    # user downloads and runs, so an unsigned setup shows Windows' "unknown publisher"
    # warning with no publisher at all, which is a worse first impression than the
    # self-signed build it installs.
    if (-not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
        $signSetupArguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $root 'sign.ps1'),
            '-Path', $setupPath,
            '-Thumbprint', $SignThumbprint
        )
        if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
            $signSetupArguments += @('-TimestampServer', $TimestampServer)
        }
        & powershell.exe @signSetupArguments
        if ($LASTEXITCODE -ne 0) {
            throw "The installer could not be signed (exit code $LASTEXITCODE)."
        }
        $setupSignature = Get-AuthenticodeSignature -LiteralPath $setupPath
        if (-not ($setupSignature -and $setupSignature.SignerCertificate)) {
            throw 'The installer was not signed. Shipping an unsigned installer would be worse than shipping none.'
        }
        Write-Host ('Signed the installer: ' + $setupSignature.SignerCertificate.Thumbprint)
    }
    else {
        Write-Warning 'No signing thumbprint was supplied, so the installer is UNSIGNED.'
    }
}
else {
    Write-Warning 'Inno Setup 6 was not found, so no installer was built. The ZIP was still created.'
}

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
