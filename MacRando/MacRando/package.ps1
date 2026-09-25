[CmdletBinding()]
param(
    [string]$Version = '1.1.0',
    [string]$SignThumbprint,
    [string]$TimestampServer
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
    (Join-Path $root 'README.md')
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
