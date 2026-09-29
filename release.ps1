[CmdletBinding()]
param(
    [string]$Version = '1.3.0',
    # Signing is explicit. Without this the release is unsigned and the update manifest
    # will still be written, but the in-app verifier will reject the download.
    [string]$SignThumbprint,
    [string]$TimestampServer,
    [string]$Repository = 'SpaceJamp/MacRando',
    [switch]$SkipTests,
    [switch]$NoManifest,
    [switch]$Portable
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )
    Write-Host ''
    Write-Host ("==> " + $Name)
    Write-Host ('-' * 60)
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw ("Step failed: " + $Name + " (exit code " + $LASTEXITCODE + ")")
    }
}

Write-Host ("MacRando " + $Version + " release")
Write-Host ("Repository: " + $Repository)
if ([string]::IsNullOrWhiteSpace($SignThumbprint)) {
    Write-Warning 'No -SignThumbprint was supplied. The build and manifest will be UNSIGNED.'
}

# Fail fast if the declared version and the compiled version disagree, so a release can
# never ship a binary that reports a different version than the tag.
$appInfo = Select-String -Path (Join-Path $root 'src\Models.cs') -Pattern 'Version\s*=\s*"([^"]+)"' |
    Select-Object -First 1
$declaredVersion = $appInfo.Matches[0].Groups[1].Value
if ($declaredVersion -ne $Version) {
    throw "Version mismatch: -Version $Version but src\Models.cs declares $declaredVersion."
}

if (-not $SkipTests) {
    Invoke-Step 'Tests' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'test.ps1')
    }
}

$packageArguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $root 'package.ps1'),
    '-Version', $Version
)
if (-not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
    $packageArguments += @('-SignThumbprint', $SignThumbprint)
    if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
        $packageArguments += @('-TimestampServer', $TimestampServer)
    }
}
if ($Portable) {
    $packageArguments += '-Portable'
}
Invoke-Step 'Build, sign, and package' {
    & powershell.exe @packageArguments
}

$exe = Join-Path $root 'bin\MacRando.exe'
$info = (Get-Item $exe).VersionInfo
if ($info.FileVersion -ne ($Version + '.0')) {
    throw "Built file version $($info.FileVersion) does not match $Version.0."
}

$signature = Get-AuthenticodeSignature -LiteralPath $exe
Write-Host ''
Write-Host ("File version : " + $info.FileVersion)
Write-Host ("Product      : " + $info.ProductVersion)
Write-Host ("Signature    : " + $signature.Status)
if ($signature.SignerCertificate) {
    Write-Host ("Signer       : " + $signature.SignerCertificate.Subject)
    Write-Host ("Thumbprint   : " + $signature.SignerCertificate.Thumbprint)
}

if (-not $NoManifest) {
    Invoke-Step 'Update manifest' {
        $publishArguments = @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $root 'publish-update.ps1'),
            '-Version', $Version,
            '-Repository', $Repository
        )
        if (-not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
            $publishArguments += @('-ExpectedThumbprint', $SignThumbprint)
        }
        & powershell.exe @publishArguments
    }
}

Write-Host ''
Write-Host 'Release checklist'
Write-Host ('-' * 60)
Write-Host ("1. Commit the version bump and update.json:            git add -A; git commit -m ""MacRando $Version""")
Write-Host ("2. Tag the release:                                    git tag -a v$Version -m ""MacRando $Version""")
Write-Host ("3. Push the commit and tag:                            git push origin main; git push origin v$Version")
Write-Host "4. Pushing the tag runs .github/workflows/release.yml, which tests, builds an"
Write-Host "   unsigned archive, and creates the GitHub release."
Write-Host "5. Upload these signed files to that release:"
Write-Host ("     - " + $exe + "   (rename to MacRando-$Version.exe)")
Write-Host ("     - " + (Join-Path $root ("dist\MacRando-{0}.zip" -f $Version)))
Write-Host ("     - " + (Join-Path $root 'update.json'))
Write-Host ''
Write-Host ("In-app manifest URL:  https://raw.githubusercontent.com/$Repository/main/update.json")
Write-Host 'Expected signer thumbprint is shown above.'
