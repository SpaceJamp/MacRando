[CmdletBinding()]
param(
    [string]$Path = 'bin\MacRando.exe',
    [string]$Thumbprint,
    [string]$StoreLocation = 'Cert:\CurrentUser\My',
    [string]$TimestampServer
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($Path)) {
    $Path = Join-Path $root $Path
}
$Path = [System.IO.Path]::GetFullPath($Path)
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Cannot sign missing file: $Path"
}

$certificates = @(Get-ChildItem -Path $StoreLocation -ErrorAction Stop)
if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
    $certificate = $certificates |
        Where-Object {
            $_.HasPrivateKey -and
            $_.Subject -match 'CN=MacRando Development'
        } |
        Select-Object -First 1
}
else {
    $normalizedThumbprint = $Thumbprint.Replace(' ', '').ToUpperInvariant()
    $certificate = $certificates |
        Where-Object {
            $_.Thumbprint -and
            $_.Thumbprint.Replace(' ', '').ToUpperInvariant() -eq $normalizedThumbprint
        } |
        Select-Object -First 1
}

if ($null -eq $certificate) {
    if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
        throw 'No private-key development certificate with subject CN=MacRando Development was found. Supply -Thumbprint explicitly.'
    }
    throw ("Certificate thumbprint was not found in {0}: {1}" -f $StoreLocation, $Thumbprint)
}
if (-not $certificate.HasPrivateKey) {
    throw "Certificate $($certificate.Thumbprint) does not have an accessible private key."
}

$signatureArguments = @{
    FilePath = $Path
    Certificate = $certificate
    HashAlgorithm = 'SHA256'
    Force = $true
}
if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
    $signatureArguments.TimestampServer = $TimestampServer
}

$signature = Set-AuthenticodeSignature @signatureArguments
if ($null -eq $signature -or $null -eq $signature.SignerCertificate) {
    throw "Authenticode signing did not produce a signer certificate for $Path."
}
if ($signature.Status -eq 'NotSigned' -or $signature.Status -eq 'HashMismatch' -or $signature.Status -eq 'Invalid') {
    throw "Authenticode signing failed for $Path with status $($signature.Status): $($signature.StatusMessage)"
}

Write-Host ("Signed " + $Path)
Write-Host ("  Signer: " + $signature.SignerCertificate.Subject)
Write-Host ("  Thumbprint: " + $signature.SignerCertificate.Thumbprint)
Write-Host ("  Status: " + $signature.Status)
if ($signature.Status -ne 'Valid') {
    Write-Warning ("The signature is embedded, but Windows does not currently trust the certificate: " + $signature.StatusMessage)
}
