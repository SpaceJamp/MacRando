[CmdletBinding()]
param(
    [string]$Version = '1.2.0',
    [string]$Executable,
    [string]$Archive,
    [string]$Repository = 'SpaceJamp/MacRando',
    [string]$Tag,
    [string]$AssetName,
    [string]$DownloadUrl,
    [string]$ExpectedThumbprint,
    [string]$ReleaseNotesUrl,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = 'v' + $Version
}
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $Executable = Join-Path $root 'bin\MacRando.exe'
}
if ([string]::IsNullOrWhiteSpace($Archive)) {
    $Archive = Join-Path $root ("dist\MacRando-{0}.zip" -f $Version)
}
if ([string]::IsNullOrWhiteSpace($AssetName)) {
    $AssetName = "MacRando-$Version.exe"
}
if ([string]::IsNullOrWhiteSpace($DownloadUrl)) {
    $DownloadUrl = 'https://github.com/{0}/releases/download/{1}/{2}' -f $Repository, $Tag, $AssetName
}
if ([string]::IsNullOrWhiteSpace($ReleaseNotesUrl)) {
    $ReleaseNotesUrl = 'https://github.com/{0}/releases/tag/{1}' -f $Repository, $Tag
}
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root 'update.json'
}

# Authenticode applies to the executable, not to the ZIP archive, and the in-app
# updater verifies a downloaded .exe. The manifest therefore describes the
# executable asset and the SHA-256 of exactly those bytes.
if (-not (Test-Path $Executable)) {
    throw "Signed executable not found: $Executable. Run package.ps1 -SignThumbprint <thumbprint> first."
}

$parsedUrl = $null
if (-not [System.Uri]::TryCreate($DownloadUrl, [System.UriKind]::Absolute, [ref]$parsedUrl) -or
    $parsedUrl.Scheme -ne [System.Uri]::UriSchemeHttps) {
    throw "DownloadUrl must be an absolute HTTPS URL: $DownloadUrl"
}

$signature = Get-AuthenticodeSignature -LiteralPath $Executable
if (-not $signature -or -not $signature.SignerCertificate) {
    throw "Executable is not Authenticode signed: $Executable"
}
$signerThumbprint = $signature.SignerCertificate.Thumbprint.ToUpperInvariant()
$signerSubject = $signature.SignerCertificate.Subject

if (-not [string]::IsNullOrWhiteSpace($ExpectedThumbprint)) {
    $expected = ($ExpectedThumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
    if ($expected -ne $signerThumbprint) {
        throw "Signer mismatch. Expected $expected but the executable is signed by $signerThumbprint ($signerSubject)."
    }
}

$executableHash = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash.ToUpperInvariant()

# If a release archive exists, make sure the archive really contains the binary that
# was just signed. Otherwise a published ZIP and the signed executable could drift apart.
#
# This is a hard failure, not a warning. A manifest describes one specific artifact, and a
# missing archive usually means the build step failed, in which case the executable on
# disk is a stale build from an earlier version.
$exeFileVersion = (Get-Item -LiteralPath $Executable).VersionInfo.FileVersion
if ($exeFileVersion -ne ($Version + '.0')) {
    throw "The executable reports file version '$exeFileVersion' but the manifest is being written for '$Version'. Rebuild before publishing."
}

if (Test-Path $Archive) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -ieq 'MacRando.exe' } | Select-Object -First 1
        if (-not $entry) {
            throw "Archive does not contain MacRando.exe: $Archive"
        }
        $stream = $entry.Open()
        try {
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                $entryHash = ([System.BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '')
            } finally {
                $sha.Dispose()
            }
        } finally {
            $stream.Dispose()
        }
        if ($entryHash -ne $executableHash) {
            throw "Archive MacRando.exe does not match the signed executable. Re-run package.ps1."
        }
        Write-Host ("Archive verified: " + $Archive)
    } finally {
        $zip.Dispose()
    }
} else {
    throw ("Release archive not found: " + $Archive + "`nRun package.ps1 before publishing. Refusing to write a manifest for an unverified build.")
}

$manifest = [ordered]@{
    Version          = $Version
    DownloadUrl      = $DownloadUrl
    Sha256           = $executableHash
    SignerThumbprint = $signerThumbprint
    ReleaseNotesUrl  = $ReleaseNotesUrl
    PublishedUtc     = [DateTime]::UtcNow.ToString('o')
}

$json = $manifest | ConvertTo-Json
[System.IO.File]::WriteAllText($Output, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ('Signer:     ' + $signerSubject)
Write-Host ('Thumbprint: ' + $signerThumbprint)
Write-Host ('SHA-256:    ' + $executableHash)
Write-Host ('Asset:      ' + $AssetName)
Write-Host ('Download:   ' + $DownloadUrl)
Write-Host ('Manifest:   ' + $Output)
Write-Host ''
Write-Host 'Upload this signed executable and the manifest to the same GitHub release, then set the in-app manifest URL to:'
Write-Host ('  https://raw.githubusercontent.com/{0}/main/update.json' -f $Repository)
