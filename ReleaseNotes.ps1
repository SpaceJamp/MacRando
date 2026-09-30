[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Release notes for one version, taken from CHANGELOG.md and nothing else.
#
# This is its own file, rather than a few lines inside the publishing script, for two
# reasons. The extraction is the part that can be silently wrong: a release body that
# accidentally contains every version ever written still looks like a plausible release
# page, and nobody checks a page for that. And it is testable only if the decision is
# separate from the uploading.
#
# Two mistakes are designed out here, both of which happened while building it:
#
#   1. Naming the *previous* version as the end marker. That has to be edited by hand every
#      release, and when it is wrong the notes silently run from the current version down
#      to an unrelated one far down the file. It was producing 38,944 characters covering
#      fifteen versions. The end marker is now "the next heading of any kind", so there is
#      nothing to keep in step.
#
#   2. Guessing when the version is missing. A version with no changelog section used to
#      produce an empty or nonsense body. It is a hard failure now, because a release whose
#      notes cannot be found should not be published with notes that are merely empty.

# Sections that describe a change to the product. Anything else in a version's entry is
# commentary about how the change was tested, which belongs in the repository changelog
# and not on a release page a user is reading to decide whether to update.
$ChangeSections = @('Added', 'Changed', 'Fixed', 'Removed', 'Deprecated', 'Security')

function Get-ReleaseNotes {
    param(
        [Parameter(Mandatory = $true)][string]$ChangelogPath,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if (-not (Test-Path -LiteralPath $ChangelogPath)) {
        throw "Changelog not found: $ChangelogPath"
    }
    $changelog = [System.IO.File]::ReadAllText($ChangelogPath, [System.Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($changelog)) {
        throw "Changelog is empty: $ChangelogPath"
    }

    # The heading for this exact version, and nothing else. Anchored to a line so that
    # asking for 1.15.0 cannot match a "## 1.15.0-beta" line or a 1.1.5 heading.
    $headingPattern = '(?m)^##\s+' + [regex]::Escape($Version) + '(?=\s|$)'
    $heading = [regex]::Match($changelog, $headingPattern)
    if (-not $heading.Success) {
        throw ("The changelog has no '" + $Version + "' section, so there are no release notes to publish. " +
               "Add it to CHANGELOG.md before releasing, otherwise the release page would have empty or wrong notes.")
    }

    # Everything up to the next level-two heading, whichever version it names. This is the
    # part that replaces the hand-edited end marker.
    $following = [regex]::Match($changelog.Substring($heading.Index + $heading.Length), '(?m)^##\s')
    $section = if ($following.Success) {
        $changelog.Substring($heading.Index, ($heading.Length + $following.Index))
    } else {
        # A single-version changelog, so this one runs to the end of the file.
        $changelog.Substring($heading.Index)
    }

    # Trim, and drop the date from the heading: the release page already shows the date,
    # and a heading that repeats it looks like the notes were pasted rather than written.
    $notes = $section.Trim()
    $notes = [regex]::Replace($notes, '(?m)^##\s+' + [regex]::Escape($Version) + '[ \t]*(-.*)?$', "## $Version")

    $notes = Remove-NonChangeSections -Notes $notes -Version $Version

    # A version with nothing but a heading is not notes. Checking for whitespace would
    # miss that case, because a bare "## 2.1.0" is not empty, so the check is that at
    # least one change section survived and has prose under it.
    $sectionsKept = [regex]::Matches($notes, '(?ms)^###\s+(.+?)$(.+?)(?=^###\s|^##\s|\z)')
    $withContent = @($sectionsKept | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Groups[2].Value) })
    if ($withContent.Count -eq 0) {
        throw ("The changelog's $Version section has no change entries after the non-change sections were " +
               "omitted, so there are no release notes to publish. A release page should say what changed; " +
               "an empty one tells a user nothing. Check for a '### Added', '### Changed' or '### Fixed' " +
               'heading with an entry under it.')
    }

    $kept = [regex]::Matches($notes, '(?m)^###\s+(.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() }
    Write-Host ("Release notes for ${Version}: " + $notes.Length + " characters, sections: " + ($kept -join ', '))
    return $notes
}

# Strips the sections that are not about a change, keeping the heading and its prose.
function Remove-NonChangeSections {
    param(
        [Parameter(Mandatory = $true)][string]$Notes,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $headingPattern = '(?m)^##\s+' + [regex]::Escape($Version) + '[ \t]*(-.*)?$'
    $heading = [regex]::Match($Notes, $headingPattern)
    if (-not $heading.Success) {
        throw "Internal error: the $Version heading was lost before the sections were filtered."
    }

    $result = New-Object System.Text.StringBuilder
    [void]$result.Append($Notes.Substring(0, $heading.Index + $heading.Length))

    # A preamble under the version heading but above the first "###" is kept: it is part of
    # what changed, not commentary about how it was tested.
    $bodyStart = $heading.Index + $heading.Length
    $firstSection = [regex]::Match($Notes.Substring($bodyStart), '(?m)^###\s')
    if (-not $firstSection.Success) {
        [void]$result.Append($Notes.Substring($bodyStart))
        return $result.ToString().Trim()
    }
    [void]$result.Append($Notes.Substring($bodyStart, $firstSection.Index))

    $dropped = @()
    $sections = [regex]::Matches($Notes, '(?ms)^###\s+(.+?)$(.*?)(?=^###\s|^##\s|\z)')
    foreach ($section in $sections) {
        $name = $section.Groups[1].Value.Trim()
        if ($ChangeSections -contains $name) {
            [void]$result.Append("`n`n### $name`n" + $section.Groups[2].Value.Trim())
        } else {
            $dropped += $name
        }
    }

    if ($dropped.Count -gt 0) {
        # Said out loud, because a section silently vanishing from a release page is the
        # kind of thing that gets noticed only after publishing.
        Write-Host ("Omitted from the release page (kept in CHANGELOG.md): " + ($dropped -join ', '))
    }
    return $result.ToString().Trim()
}
