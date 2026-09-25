[CmdletBinding()]
param(
    [switch]$DebugBuild,
    [string]$SignThumbprint,
    [string]$TimestampServer
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceFolder = Join-Path $root 'src'
$outputFolder = Join-Path $root 'bin'
$outputPath = Join-Path $outputFolder 'MacRando.exe'
$appIconPath = Join-Path $root 'assets\MacRando.ico'
$trayIconPath = Join-Path $root 'assets\MacRandoTray.ico'

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) {
    throw 'The .NET Framework C# compiler was not found. Install or repair the .NET Framework developer tools.'
}

New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$sources = Get-ChildItem -Path $sourceFolder -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $sources) {
    throw 'No C# source files were found.'
}

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/langversion:5',
    '/utf8output',
    '/warn:4',
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Net.Http.dll',
    '/reference:System.Security.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Windows.Forms.dll',
    ('/out:' + $outputPath),
    ('/win32manifest:' + (Join-Path $sourceFolder 'app.manifest'))
)
if (Test-Path $appIconPath) {
    $arguments += ('/win32icon:' + $appIconPath)
}
if ($DebugBuild) {
    $arguments += '/debug:full'
    $arguments += '/optimize-'
} else {
    $arguments += '/debug:pdbonly'
    $arguments += '/optimize+'
}
$arguments += $sources

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "MacRando compilation failed with exit code $LASTEXITCODE."
}

if (Test-Path $appIconPath) {
    Copy-Item $appIconPath (Join-Path $outputFolder 'MacRando.ico') -Force
}
if (Test-Path $trayIconPath) {
    Copy-Item $trayIconPath (Join-Path $outputFolder 'MacRandoTray.ico') -Force
}

if (-not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
    $signArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $root 'sign.ps1'),
        '-Path', $outputPath,
        '-Thumbprint', $SignThumbprint
    )
    if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) {
        $signArguments += @('-TimestampServer', $TimestampServer)
    }
    & powershell.exe @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "MacRando signing failed with exit code $LASTEXITCODE."
    }
}

Write-Host ("Built " + $outputPath)
