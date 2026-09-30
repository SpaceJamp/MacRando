# Tests the release-notes extraction against a temporary changelog.
#
# The failure this guards is silent: a release body that contains every version ever
# written still looks like a plausible release page, so nothing would report it. It
# happened. The extraction was marking the end of a version's notes with a hand-edited
# "the previous version" marker, and when that marker was stale the notes ran from the
# current version down to an unrelated heading far down the file, 38,944 characters
# covering fifteen versions. The published 1.15.0 page was correct only because the
# script used to do it had drifted from the one on disk.
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $root 'ReleaseNotes.ps1')

$checks = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:checks++
    if (-not $Condition) {
        throw $Message
    }
}

function New-Changelog {
    param([string]$Body)
    $path = Join-Path $env:TEMP ('MacRandoChangelog-' + [Guid]::NewGuid().ToString('N') + '.md')
    [System.IO.File]::WriteAllText($path, $Body, (New-Object System.Text.UTF8Encoding($false)))
    return $path
}

# A changelog shaped like the real one, with several versions so that an extraction which
# runs past its own section has somewhere to run to and can be caught.
$sample = @'
# MacRando Changelog

## 2.1.0 - 2026.10

### Added

- The thing this release added.

### Fixed

- The thing this release fixed.

### Notes

- Commentary about how the change was tested, which belongs in this file only.

## 2.0.0 - 2026.09

### Added

- An older release's entry, which must not appear in 2.1.0's notes.

### Fixed

- An older fix, which must not appear either.

## 1.9.9 - 2026.09

### Added

- The oldest entry in the file.
'@

try {
    $path = New-Changelog -Body $sample
    $notes = Get-ReleaseNotes -ChangelogPath $path -Version '2.1.0'

    Assert-True ($notes -notmatch 'An older release') "The notes for 2.1.0 contain another version's entry. This is the bug this file exists to prevent. Notes were:`n$notes"
    Assert-True ($notes -notmatch 'The oldest entry') "The notes for 2.1.0 contain the oldest entry in the changelog."
    Assert-True ($notes -notmatch '1\.9\.9') "The notes for 2.1.0 mention version 1.9.9."
    Assert-True ($notes -match 'The thing this release added') "The notes lost this release's Added entry."
    Assert-True ($notes -match 'The thing this release fixed') "The notes lost this release's Fixed entry."

    # Only one version heading may appear, and it must be the one asked for.
    $headings = [regex]::Matches($notes, '(?m)^##\s+(\S+)')
    Assert-True ($headings.Count -eq 1) "The notes contain $($headings.Count) version headings, expected exactly 1."
    Assert-True ($headings[0].Groups[1].Value -eq '2.1.0') "The notes are headed $($headings[0].Groups[1].Value), expected 2.1.0."

    # Commentary about testing is not a change, and does not belong on a release page.
    Assert-True ($notes -notmatch 'Commentary about how the change was tested') "A Notes section leaked into the release notes."
    Assert-True ($notes -notmatch '### Notes') "The Notes heading leaked into the release notes."

    # The date is dropped from the heading: the release page already shows it.
    Assert-True ($notes -notmatch '2026\.10') "The release notes heading still carries the build date."

    # A second version from the same changelog must come out clean too, which is the case
    # that catches an extraction anchored to a fixed offset rather than to a heading.
    $older = Get-ReleaseNotes -ChangelogPath $path -Version '2.0.0'
    Assert-True ($older -match 'An older release') "The notes for 2.0.0 lost its own Added entry."
    Assert-True ($older -notmatch 'The thing this release added') "The notes for 2.0.0 contain 2.1.0's entry."

    # The oldest version has no following heading, so it runs to the end of the file.
    $oldest = Get-ReleaseNotes -ChangelogPath $path -Version '1.9.9'
    Assert-True ($oldest -match 'The oldest entry') "The notes for the oldest version lost their entry."
    Assert-True ($oldest -notmatch '2\.0\.0') "The notes for 1.9.9 contain a newer version's entry."

    # A version that is not in the changelog is a failure, not an empty page.
    $threw = $false
    try { Get-ReleaseNotes -ChangelogPath $path -Version '3.0.0' | Out-Null } catch { $threw = $true }
    Assert-True $threw "Asking for a version with no changelog section should fail rather than publish empty notes."

    # A version whose heading merely starts with the same digits must not match. 2.1 must
    # not resolve to 2.1.0, or a typo would silently publish another version's notes.
    $threw = $false
    try { Get-ReleaseNotes -ChangelogPath $path -Version '2.1' | Out-Null } catch { $threw = $true }
    Assert-True $threw "A partial version number should not match a longer one."

    # A version entry with no change sections at all is also a failure.
    $empty = New-Changelog -Body "# Changelog`n`n## 4.0.0 - 2026.11`n`n### Notes`n`n- Only commentary.`n"
    $threw = $false
    try { Get-ReleaseNotes -ChangelogPath $empty -Version '4.0.0' | Out-Null } catch { $threw = $true }
    Assert-True $threw "A version whose only section is Notes should fail rather than publish nothing."

    # A single-version changelog, which is what a brand new repository looks like.
    $single = New-Changelog -Body "# Changelog`n`n## 0.1.0`n`n### Added`n`n- The first release.`n"
    $first = Get-ReleaseNotes -ChangelogPath $single -Version '0.1.0'
    Assert-True ($first -match 'The first release') "A single-version changelog lost its only entry."

    # A heading with no date, since the changelog did not always carry one.
    $nodate = New-Changelog -Body "# Changelog`n`n## 5.0.0`n`n### Fixed`n`n- A fix.`n`n## 4.9.0`n`n### Fixed`n`n- Older.`n"
    $clean = Get-ReleaseNotes -ChangelogPath $nodate -Version '5.0.0'
    Assert-True ($clean -match 'A fix') "A heading without a date lost its entry."
    Assert-True ($clean -notmatch 'Older') "A heading without a date pulled in the next version."

    Remove-Item -LiteralPath $path, $empty, $single, $nodate -Force -ErrorAction SilentlyContinue
} finally {
    Get-ChildItem -Path $env:TEMP -Filter 'MacRandoChangelog-*.md' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host ("release-notes-tests=OK;checks=" + $checks)
