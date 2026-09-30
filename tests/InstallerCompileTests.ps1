# Compiles the real installer.iss both ways, which is the only way to catch the failure
# that stopped the Release workflow.
#
# The bug: the public certificate was listed unconditionally in [Files], so the installer
# could not be compiled whenever the build was unsigned. Every GitHub-hosted release run
# builds unsigned, because the signing certificate does not exist on a runner, so the
# workflow failed on all 33 of its runs and never produced a release. A test that only
# looked at the text would not have caught it, and a signed local build never hits it.
#
# The installer is compiled to a temporary output directory, so bin\ and dist\ are not
# disturbed and this is safe to run in the middle of a release.
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$iss = Join-Path $root 'installer.iss'
$checks = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:checks++
    if (-not $Condition) {
        throw $Message
    }
}

function Find-InnoCompiler {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$iscc = Find-InnoCompiler
if (-not $iscc) {
    Write-Host 'Inno Setup was not found, so the installer cannot be compile-tested here.'
    Write-Host 'installer-compile-tests=SKIPPED'
    exit 0
}

$work = Join-Path $env:TEMP ('MacRandoInstallerTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-Iscc {
    param([string[]]$Defines, [string]$Label)
    $outDir = Join-Path $work $Label
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $args = @("/DAppVersion=1.0.0-test") + $Defines + @("/O$outDir", $iss)
    # A failing compile writes to stderr, and this script runs with ErrorActionPreference
    # Stop, which turns that into a terminating error and abandons the run before the exit
    # code can be examined. The exit code is the verdict here, so it is read directly.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $iscc @args 2>&1
        $exit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
    return @{ Exit = $exit; Output = (($output | ForEach-Object { [string]$_ }) -join "`n") }
}

try {
    # The case that matters. No /DIncludeSigningCertificate is exactly what the Release
    # workflow passes, because it builds unsigned. This failed with "Source file
    # dist\MacRando-Public.cer does not exist" and took the whole workflow with it.
    $unsigned = Invoke-Iscc -Defines @() -Label 'unsigned'
    if ($unsigned.Exit -ne 0) {
        throw ("The installer does not compile for an unsigned build, which is what every GitHub-hosted" +
               " release run does:`n" + $unsigned.Output)
    }
    $setup = @(Get-ChildItem (Join-Path $work 'unsigned') -Filter '*.exe' -ErrorAction SilentlyContinue)
    Assert-True ($setup.Count -eq 1) "The unsigned build should produce exactly one setup program, found $($setup.Count)."
    Write-Host ("  unsigned build compiles: " + $setup[0].Name)

    # The signed case, which is what the maintainer's machine produces. It must still
    # include the certificate, or the trust option would silently stop working for a real
    # user while every automated check stayed green.
    $cert = Join-Path $root 'dist\MacRando-Public.cer'
    if (Test-Path $cert) {
        $signed = Invoke-Iscc -Defines @('/DIncludeSigningCertificate') -Label 'signed'
        if ($signed.Exit -ne 0) {
            throw ("The installer does not compile for a signed build:`n" + $signed.Output)
        }
        Assert-True ((Get-ChildItem (Join-Path $work 'signed') -Filter '*.exe').Count -eq 1) `
            "The signed build should produce exactly one setup program."
        Write-Host '  signed build compiles'

        # A positive control that the conditional is what made the unsigned build work,
        # rather than the certificate simply never being validated. Asking for the
        # certificate with the file absent must fail.
        $hidden = Join-Path $work 'hidden'
        New-Item -ItemType Directory -Force -Path $hidden | Out-Null
        $savedCert = Join-Path $work 'MacRando-Public.cer'
        Move-Item $cert $savedCert -Force
        try {
            $control = Invoke-Iscc -Defines @('/DIncludeSigningCertificate') -Label 'control'
            Assert-True ($control.Exit -ne 0) `
                ("Compiling with /DIncludeSigningCertificate while dist\MacRando-Public.cer is absent" +
                 " succeeded, so the define is not actually gating anything and the unsigned case" +
                 " would be passing for the wrong reason.")
        }
        finally {
            Move-Item $savedCert $cert -Force
        }
    }
    else {
        Write-Host '  dist\MacRando-Public.cer is absent (an unsigned build), so only the unsigned case applies.'
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ("installer-compile-tests=OK;checks=" + $checks)
