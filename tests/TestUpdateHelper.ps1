[CmdletBinding()]
param()

<#
.SYNOPSIS
End-to-end test for the update install helper.

.DESCRIPTION
Extracts the real helper script that MacRando generates, checks that it is valid
PowerShell, then runs it against a stub executable for every outcome it has to handle:

  good  a build that reports a successful start   -> installed, no rollback
  crash a build that exits immediately             -> rolled back to the previous build
  slow  a build that stays alive but never reports -> kept, because it is running

The third case is the one that regressed: treating "no marker" as failure rolled back
perfectly good builds whenever antivirus or a cold start was slow.

Nothing here touches a real adapter or a real installation; every path is a temp folder.
#>

$ErrorActionPreference = 'Stop'
$tests = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $tests
$work = Join-Path $env:TEMP ('MacRandoHelperTest-' + [Guid]::NewGuid().ToString('N'))

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$failures = 0
try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    # 1. Build the stub executable that stands in for the app.
    $stub = Join-Path $work 'UpdaterHelperStub.exe'
    & $compiler /nologo /target:exe /platform:anycpu /langversion:5 /out:$stub (Join-Path $tests 'UpdaterHelperStub.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the stub executable.' }

    # 2. Extract the real generated helper script, so this test cannot drift from
    #    what the application actually runs.
    $extractor = Join-Path $work 'ExtractHelper.cs'
    @'
using System;
using System.IO;
using System.Reflection;
internal static class ExtractHelper
{
    private static int Main(string[] args)
    {
        MethodInfo method = typeof(MacRando.UpdateInstaller).GetMethod(
            "BuildHelperScript", BindingFlags.Static | BindingFlags.NonPublic);
        File.WriteAllText(args[0], (string)method.Invoke(null, null));
        return 0;
    }
}
'@ | Set-Content -LiteralPath $extractor -Encoding UTF8

    $extractExe = Join-Path $work 'ExtractHelper.exe'
    & $compiler /nologo /target:exe /platform:anycpu /langversion:5 /out:$extractExe `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
        (Join-Path $root 'src\Models.cs') (Join-Path $root 'src\RetentionPolicy.cs') (Join-Path $root 'src\AppLogger.cs') `
        (Join-Path $root 'src\UpdateInstaller.cs') $extractor
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the helper script extractor.' }

    $scriptPath = Join-Path $work 'helper.ps1'
    & $extractExe $scriptPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the helper script.' }

    # 3. The script must be valid PowerShell before it ever runs on a user's machine.
    $parseErrors = $null
    $tokens = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        foreach ($e in $parseErrors) { Write-Host ('PARSE ERROR: ' + $e.Message) }
        throw 'The generated helper script is not valid PowerShell.'
    }
    Write-Host 'helper script parses cleanly'

    function Invoke-Case {
        param(
            [string]$Label,
            [int]$WatchdogMs,
            [string]$Healthy,
            [string]$NoMarker,
            [int]$DelayMs,
            [string]$ExpectedHash,
            [switch]$TamperSource
        )
        $caseDir = Join-Path $work $Label
        New-Item -ItemType Directory -Force -Path $caseDir | Out-Null

        # target starts as five junk bytes so a swap or a rollback is obvious.
        $target = Join-Path $caseDir 'Target.exe'
        $source = Join-Path $caseDir 'Source.exe'
        $backup = Join-Path $caseDir 'backup\Target.exe'
        $marker = Join-Path $caseDir 'marker.txt'
        $log = Join-Path $caseDir 'helper.log'
        [System.IO.File]::WriteAllBytes($target, [byte[]](1, 2, 3, 4, 5))
        Copy-Item $stub $source -Force

        # The hash is taken before any tampering, mirroring the real sequence: the main
        # process verifies the download, then something else replaces it, then the
        # elevated helper runs. A literal sentinel is used for the case that supplies no
        # hash, because an empty argument would otherwise be indistinguishable from
        # "not supplied" and would be silently filled in here.
        if (-not $PSBoundParameters.ContainsKey('ExpectedHash')) {
            $ExpectedHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        }
        if ($TamperSource) {
            # A stand-in for another process running as the same user, writing to the
            # per-user temp folder between verification and install.
            [System.IO.File]::WriteAllBytes($source, [byte[]](0x4D, 0x5A, 0x90, 0x00, 0xDE, 0xAD, 0xBE, 0xEF))
        }

        $env:MACRANDO_TARGET = $target
        $env:MACRANDO_SOURCE = $source
        $env:MACRANDO_BACKUP = $backup
        $env:MACRANDO_MARKER = $marker
        $env:MACRANDO_PID = '999999'      # no such process, so the helper does not wait
        $env:MACRANDO_WATCHDOG_MS = "$WatchdogMs"
        $env:MACRANDO_EXIT_WAIT_MS = '5000'
        $env:MACRANDO_LOG = $log
        $env:MACRANDO_SHA256 = $ExpectedHash
        $env:MACRANDO_STUB_HEALTHY = $Healthy
        $env:MACRANDO_STUB_NO_MARKER = $NoMarker
        $env:MACRANDO_STUB_DELAY_MS = "$DelayMs"

        $helperCopy = Join-Path $caseDir 'helper.ps1'
        Copy-Item $scriptPath $helperCopy -Force

        $process = Start-Process -FilePath 'powershell.exe' -PassThru -WindowStyle Hidden -ArgumentList @(
            '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
            '-WindowStyle', 'Hidden', '-File', "`"$helperCopy`""
        )
        $process.WaitForExit(90000) | Out-Null

        # Stop any stub a case deliberately left running.
        Get-Process -Name 'UpdaterHelperStub' -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue

        Write-Host ''
        Write-Host "--- $Label ---"
        if (Test-Path $log) { Get-Content $log | ForEach-Object { Write-Host "    $_" } }

        $bytes = [System.IO.File]::ReadAllBytes($target)
        $isNewBuild = ($bytes.Length -gt 2 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A)
        $isOldBytes = ($bytes.Length -eq 5 -and $bytes[0] -eq 1)
        return @{
            NewBuild   = $isNewBuild
            OldBytes   = $isOldBytes
            Backup     = (Test-Path $backup)
            SelfRemoved = (-not (Test-Path $helperCopy))
        }
    }

    $good = Invoke-Case -Label 'good' -WatchdogMs 20000 -Healthy '1' -NoMarker '0' -DelayMs 1500
    $crash = Invoke-Case -Label 'crash' -WatchdogMs 20000 -Healthy '0' -NoMarker '0' -DelayMs 50
    $slow = Invoke-Case -Label 'slow' -WatchdogMs 3000 -Healthy '1' -NoMarker '1' -DelayMs 50

    # The case this hardening exists for. The download is verified in the main process,
    # then replaced by something else in the per-user temp folder, and only then does the
    # elevated helper run. The target must be left exactly as it was.
    $tampered = Invoke-Case -Label 'tampered' -WatchdogMs 20000 -Healthy '1' -NoMarker '0' -DelayMs 50 -TamperSource
    $tamperedBytes = [System.IO.File]::ReadAllBytes((Join-Path $work 'tampered\Target.exe'))
    $tamperedUntouched = ($tamperedBytes.Length -eq 5 -and $tamperedBytes[0] -eq 1)

    # A missing hash must also refuse, or the check could be skipped by simply not
    # supplying one.
    $noHash = Invoke-Case -Label 'nohash' -WatchdogMs 20000 -Healthy '1' -NoMarker '0' -DelayMs 50 -ExpectedHash ' '
    $noHashBytes = [System.IO.File]::ReadAllBytes((Join-Path $work 'nohash\Target.exe'))
    $noHashUntouched = ($noHashBytes.Length -eq 5 -and $noHashBytes[0] -eq 1)

    Write-Host ''
    Write-Host '=== assertions ==='

    if (-not $good.NewBuild) { Write-Host 'FAIL: a healthy build was not installed'; $failures++ }
    else { Write-Host 'PASS: a build that reports success is installed' }

    if (-not $crash.OldBytes) { Write-Host 'FAIL: a crashed build was not rolled back'; $failures++ }
    else { Write-Host 'PASS: a build that crashes on launch is rolled back' }

    # The regression this case exists for: a slow start must not be treated as a failure.
    if (-not $slow.NewBuild) { Write-Host 'FAIL: a slow but running build was wrongly rolled back'; $failures++ }
    else { Write-Host 'PASS: a slow but running build is kept, not rolled back' }

    if (-not $crash.Backup -or -not $good.Backup) { Write-Host 'FAIL: the previous build was not backed up'; $failures++ }
    else { Write-Host 'PASS: the previous build is backed up before a swap' }

    if (-not $good.SelfRemoved -or -not $crash.SelfRemoved -or -not $slow.SelfRemoved) {
        Write-Host 'FAIL: the helper did not delete itself'
        $failures++
    }
    else { Write-Host 'PASS: the helper cleans itself up' }

    # The security case: a download replaced after verification must never be installed.
    if (-not $tamperedUntouched) {
        Write-Host 'FAIL: a download that no longer matches the verified hash was installed'
        $failures++
    }
    else { Write-Host 'PASS: a download replaced after verification is refused' }

    $tamperedLog = Join-Path $work 'tampered\helper.log'
    if (Test-Path $tamperedLog) {
        $tamperedText = Get-Content -Raw $tamperedLog
        if ($tamperedText -match 'no longer matches the verified hash') {
            Write-Host 'PASS: the refusal names the hash mismatch as the reason'
        }
        else {
            Write-Host 'FAIL: the refusal did not record why it refused'
            $failures++
        }
    }
    else {
        Write-Host 'FAIL: the tampered case produced no log'
        $failures++
    }

    if (-not $noHashUntouched) {
        Write-Host 'FAIL: an install was allowed with no expected hash supplied'
        $failures++
    }
    else { Write-Host 'PASS: an install with no expected hash is refused' }
}
finally {
    Get-Process -Name 'UpdaterHelperStub' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    try { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    foreach ($name in @('MACRANDO_TARGET', 'MACRANDO_SOURCE', 'MACRANDO_BACKUP', 'MACRANDO_MARKER',
                        'MACRANDO_PID', 'MACRANDO_WATCHDOG_MS', 'MACRANDO_EXIT_WAIT_MS', 'MACRANDO_LOG',
                        'MACRANDO_SHA256',
                        'MACRANDO_STUB_HEALTHY', 'MACRANDO_STUB_NO_MARKER', 'MACRANDO_STUB_DELAY_MS')) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
}

if ($failures -eq 0) {
    Write-Host 'update-helper-e2e=OK'
    exit 0
}
Write-Host "update-helper-e2e=FAILED ($failures)"
exit 1
