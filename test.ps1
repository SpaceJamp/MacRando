[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$output = Join-Path $env:TEMP 'MacRandoNetworkServiceMockTests.exe'
$sources = @(
    (Join-Path $root 'src\Models.cs'),
    (Join-Path $root 'src\AppSettings.cs'),
    (Join-Path $root 'src\PowerShellRunner.cs'),
    (Join-Path $root 'src\PowerShellRunnerService.cs'),
    (Join-Path $root 'src\NetworkService.cs'),
    (Join-Path $root 'src\AppLogger.cs'),
    (Join-Path $root 'src\StateStore.cs'),
    (Join-Path $root 'src\DiagnosticsService.cs'),
    (Join-Path $root 'src\UpdateService.cs'),
    (Join-Path $root 'src\LicenseInfo.cs'),
    (Join-Path $root 'tests\NetworkServiceMockTests.cs'),
    (Join-Path $root 'tests\UpgradeRegressionTests.cs')
)
$references = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Net.Http.dll',
    '/reference:System.Security.dll',
    '/reference:System.Web.Extensions.dll'
)
& $compiler /nologo /target:exe /platform:anycpu /langversion:5 /out:$output $references $sources
if ($LASTEXITCODE -ne 0) {
    throw "Mock test compilation failed with exit code $LASTEXITCODE."
}
& $output
if ($LASTEXITCODE -ne 0) {
    throw "Mock tests failed with exit code $LASTEXITCODE."
}
