[CmdletBinding()]
param(
    # Optional. When omitted, a MacRando-Public.cer next to this script is used, which is
    # what the portable folder ships. Otherwise the in-app export location is used.
    [string]$CertificatePath,
    # CurrentUser needs no elevation. Use LocalMachine to trust the publisher for every user.
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$Store = 'CurrentUser',
    # Nothing happens without this switch, so the script is safe to read and share.
    [switch]$Install,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $besideScript = Join-Path $PSScriptRoot 'MacRando-Public.cer'
    $exported = Join-Path $env:LOCALAPPDATA 'MacRando\certificates\MacRando-Public.cer'
    if (Test-Path $besideScript) {
        $CertificatePath = $besideScript
    }
    else {
        $CertificatePath = $exported
    }
}

# TrustedPublisher is the store Windows consults for Authenticode publisher trust.
# 'Root' is intentionally not used: it would make the certificate a trust anchor and
# would then be able to sign anything the machine trusts.
$storeLocation = 'Cert:\{0}\TrustedPublisher' -f $Store

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ($Store -eq 'LocalMachine' -and -not (Test-Elevated)) {
    throw 'Installing into LocalMachine requires an elevated PowerShell session.'
}

if ($Remove) {
    if (-not (Test-Path $storeLocation)) {
        Write-Host "No TrustedPublisher store at $storeLocation."
        exit 0
    }
    $existing = Get-ChildItem $storeLocation | Where-Object { $_.Subject -eq 'CN=MacRando Development' }
    if (-not $existing) {
        Write-Host 'No MacRando Development certificate is installed in TrustedPublisher.'
        exit 0
    }
    if (-not $Install) {
        Write-Host 'The following certificates would be removed:'
        $existing | ForEach-Object { Write-Host ("  {0}  {1}" -f $_.Thumbprint, $_.Subject) }
        Write-Host ''
        Write-Host 'Re-run with -Remove -Install to actually remove them.'
        exit 0
    }
    $existing | Remove-Item -Force
    Write-Host ("Removed {0} certificate(s) from {1}." -f @($existing).Count, $storeLocation)
    exit 0
}

if (-not (Test-Path $CertificatePath)) {
    throw "Certificate not found: $CertificatePath`nExport it first from the tray menu: Export signing certificate (public)."
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $CertificatePath
Write-Host ("Subject    : {0}" -f $certificate.Subject)
Write-Host ("Issuer     : {0}" -f $certificate.Issuer)
Write-Host ("Thumbprint : {0}" -f $certificate.Thumbprint)
Write-Host ("Expires    : {0}" -f $certificate.NotAfter.ToString('u'))
Write-Host ("Store      : {0}" -f $storeLocation)
Write-Host ''

if (-not $Install) {
    Write-Host 'Dry run only. Re-run with -Install to trust this publisher on this machine.'
    Write-Host ''
    Write-Host 'What this does and does not do:'
    Write-Host '  * Installs the PUBLIC certificate into the TrustedPublisher store so Windows'
    Write-Host '    stops reporting an unknown publisher for binaries signed by it.'
    Write-Host '  * The private key is never involved and is never exported.'
    Write-Host '  * A self-signed development certificate is still not a CA-issued certificate,'
    Write-Host '    so SmartScreen reputation warnings may continue until the certificate is'
    Write-Host '    trusted on each machine that runs the build.'
    exit 0
}

if (-not (Test-Path $storeLocation)) {
    New-Item -Path $storeLocation -Force | Out-Null
}

$alreadyTrusted = Get-ChildItem $storeLocation | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }
if ($alreadyTrusted) {
    Write-Host 'This certificate is already trusted for the selected store. Nothing to do.'
    exit 0
}

Import-Certificate -FilePath $CertificatePath -CertStoreLocation $storeLocation | Out-Null
Write-Host ("Installed the MacRando Development certificate into {0}." -f $storeLocation)
Write-Host ''
Write-Host 'Verify with:'
Write-Host '  Get-AuthenticodeSignature .\bin\MacRando.exe | Format-List Status,SignerCertificate'
