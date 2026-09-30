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
    (Join-Path $root 'src\NetworkAutoApply.cs'),
    (Join-Path $root 'src\DeviceTrackingService.cs'),
    (Join-Path $root 'src\NetworkService.cs'),
    (Join-Path $root 'src\AppLogger.cs'),
    (Join-Path $root 'src\StateStore.cs'),
    (Join-Path $root 'src\DiagnosticsService.cs'),
    (Join-Path $root 'src\UpdateService.cs'),
    (Join-Path $root 'src\UpdateInstaller.cs'),
    (Join-Path $root 'src\LicenseInfo.cs'),
    (Join-Path $root 'src\NotificationPopup.cs'),
    (Join-Path $root 'src\NotificationCenterForm.cs'),
    (Join-Path $root 'src\DashboardForm.cs'),
    (Join-Path $root 'tests\NetworkServiceMockTests.cs'),
    (Join-Path $root 'tests\UpgradeRegressionTests.cs'),
    (Join-Path $root 'tests\ContrastTests.cs'),
    (Join-Path $root 'tests\ScriptValidationTests.cs'),
    (Join-Path $root 'tests\NetworkPresetTests.cs'),
    (Join-Path $root 'tests\DeviceTrackingTests.cs'),
    (Join-Path $root 'src\RetentionPolicy.cs'),
    (Join-Path $root 'tests\RetentionTests.cs'),
    (Join-Path $root 'src\Accessibility.cs'),
    (Join-Path $root 'tests\AccessibilityTests.cs'),
    (Join-Path $root 'tests\LayoutTests.cs'),
    (Join-Path $root 'src\TrayContext.cs'),
    (Join-Path $root 'src\TrayMenuState.cs'),
    (Join-Path $root 'src\UpdateTrust.cs'),
    (Join-Path $root 'tests\UpdateTrustTests.cs')
)
$references = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Net.Http.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    '/reference:System.Security.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Windows.Forms.dll'
)
& $compiler /nologo /target:exe /platform:anycpu /langversion:5 /out:$output $references $sources
if ($LASTEXITCODE -ne 0) {
    throw "Mock test compilation failed with exit code $LASTEXITCODE."
}
& $output
if ($LASTEXITCODE -ne 0) {
    throw "Mock tests failed with exit code $LASTEXITCODE."
}

# The update install helper is exercised end to end against a stub executable:
# install, rollback on crash, and no false rollback on a slow start.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\TestUpdateHelper.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Update helper end-to-end test failed with exit code $LASTEXITCODE."
}
