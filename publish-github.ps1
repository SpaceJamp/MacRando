[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Repository = 'SpaceJamp/MacRando',
    [string]$DistDirectory,
    [string]$ManifestPath,
    [switch]$NotesOnly
)

# Creates the GitHub release and uploads its assets.
#
# This lives in the repository rather than in a scratch file outside it. It used to be a
# copy in the temp directory that was edited by string replacement each release, which is
# how two separate problems happened: a stale version marker in the notes extraction, and a
# tag pushed pointing at the previous commit. Both were invisible at the time and both had
# to be chased afterwards.
#
# run.ps1 does not call this, and should not. A release is published deliberately, by hand,
# with a credential in memory for the moment it takes. The updater path is separate and
# does not need this.
#
# The token is read from the git credential helper into memory and never written to disk,
# not even to a temporary file.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root 'ReleaseNotes.ps1')

if ([string]::IsNullOrWhiteSpace($DistDirectory)) {
    $DistDirectory = Join-Path $root 'dist'
}
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $root 'update.json'
}

$tag = 'v' + $Version
$notes = Get-ReleaseNotes -ChangelogPath (Join-Path $root 'CHANGELOG.md') -Version $Version

$expected = @(
    (Join-Path $DistDirectory "MacRando-$Version.exe"),
    (Join-Path $DistDirectory "MacRando-$Version.zip"),
    (Join-Path $DistDirectory "MacRando-$Version-setup.exe"),
    $ManifestPath
)
# -NotesOnly repairs a published page without touching assets. It exists because the
# release bodies of earlier versions were built by the old extraction and carry the whole
# changelog, and those releases' artifacts are long gone from disk, so demanding them
# would make the pages unrepairable.
if (-not $NotesOnly) {
    foreach ($path in $expected) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw ("Missing release asset: $path`nRun release.ps1 -Version $Version and publish-update.ps1 first.")
        }
    }
}

$filled = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
$token = $null
$user = $null
foreach ($line in $filled) {
    if ($line -like 'password=*') { $token = $line.Substring(9) }
    if ($line -like 'username=*') { $user = $line.Substring(9) }
}
if (-not $token) {
    throw 'No GitHub credential was available from the git credential helper.'
}
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(("{0}:{1}" -f $user, $token)))
$headers = @{
    Authorization  = "Basic $basic"
    'User-Agent'   = 'MacRando-release'
    Accept         = 'application/vnd.github+json'
}

function Invoke-GitHub {
    param([string]$Method, [string]$Uri, [byte[]]$Body, [string]$ContentType)
    $params = @{
        Method       = $Method
        Uri          = $Uri
        Headers      = $headers
        UseBasicParsing = $true
        TimeoutSec   = 300
    }
    if ($null -ne $Body) {
        $params.Body = $Body
        $params.ContentType = if ($ContentType) { $ContentType } else { 'application/octet-stream' }
    }
    try {
        $response = Invoke-WebRequest @params
        return @{ Status = [int]$response.StatusCode; Body = $response.Content }
    } catch [System.Net.WebException] {
        $errorResponse = $_.Exception.Response
        if ($null -eq $errorResponse) { throw }
        $reader = New-Object IO.StreamReader($errorResponse.GetResponseStream())
        $text = $reader.ReadToEnd()
        $reader.Close()
        return @{ Status = [int]$errorResponse.StatusCode; Body = $text }
    }
}

$existing = Invoke-GitHub -Method GET -Uri "https://api.github.com/repos/$Repository/releases/tags/$tag"
if ($existing.Status -eq 200) {
    $release = $existing.Body | ConvertFrom-Json
    Write-Host ("Release already exists: id=" + $release.id)

    # The notes are corrected even when the release does, because a release created with
    # the old extraction can be carrying the whole changelog. Skipping this is how a bad
    # body survives every later release.
    $payload = @{ tag_name = $tag; name = "MacRando $Version"; body = $notes } | ConvertTo-Json
    $patched = Invoke-GitHub -Method PATCH `
        -Uri "https://api.github.com/repos/$Repository/releases/$($release.id)" `
        -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType 'application/json'
    if ($patched.Status -notin @(200, 201)) {
        throw "Could not update the release notes ($($patched.Status)): $($patched.Body)"
    }
    Write-Host 'Release notes updated to this version only.'
} elseif ($existing.Status -eq 404) {
    $payload = @{
        tag_name    = $tag
        name        = "MacRando $Version"
        body        = $notes
        draft       = $false
        prerelease = $false
    } | ConvertTo-Json
    $created = Invoke-GitHub -Method POST -Uri "https://api.github.com/repos/$Repository/releases" `
        -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -ContentType 'application/json'
    if ($created.Status -notin @(201, 200)) {
        throw "Create release failed ($($created.Status)): $($created.Body)"
    }
    $release = $created.Body | ConvertFrom-Json
    Write-Host ("Created release: id=" + $release.id)
} else {
    throw "Unexpected status looking up the release: $($existing.Status) $($existing.Body)"
}

$uploaded = @()
if ($NotesOnly) {
    Write-Host 'Notes only: leaving assets untouched.'
}
$assets = Invoke-GitHub -Method GET -Uri "https://api.github.com/repos/$Repository/releases/$($release.id)/assets?per_page=100"
if ($assets.Status -eq 200) {
    $uploaded = @($assets.Body | ConvertFrom-Json | ForEach-Object { $_.name })
}

foreach ($path in $(if ($NotesOnly) { @() } else { $expected })) {
    $name = Split-Path -Leaf $path
    if ($uploaded -contains $name) {
        Write-Host ("Skipping (already present): " + $name)
        continue
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $url = "https://uploads.github.com/repos/$Repository/releases/$($release.id)/assets?name=" + [Uri]::EscapeDataString($name)
    $result = Invoke-GitHub -Method POST -Uri $url -Body $bytes -ContentType 'application/octet-stream'
    if ($result.Status -notin @(201, 200)) {
        throw "Upload of $name failed ($($result.Status)): $($result.Body)"
    }
    Write-Host ("Uploaded: " + $name + " (" + $bytes.Length + " bytes)")
}

# The release must exist and be public, or "latest" will not resolve to it and the updater
# sees nothing to offer.
$verify = Invoke-GitHub -Method GET -Uri "https://api.github.com/repos/$Repository/releases/tags/$tag"
if ($verify.Status -ne 200) {
    throw "The release did not materialize ($($verify.Status))."
}
$final = $verify.Body | ConvertFrom-Json
if ($final.draft -or $final.prerelease) {
    throw "The release is a draft or prerelease, so the 'latest' download will not resolve to it."
}

# The notes are checked back off the published page rather than trusted, because the whole
# point of this change is that a body containing the entire changelog looks plausible.
$published = $final.body
if ($null -eq $published -or [string]::IsNullOrWhiteSpace($published)) {
    throw 'The published release has no notes.'
}
$publishedHeadings = [regex]::Matches($published, '(?m)^##\s+(\S+)')
if ($publishedHeadings.Count -ne 1) {
    throw ("The published release notes contain " + $publishedHeadings.Count +
           " version headings but must contain exactly 1. A release page that lists several versions is " +
           'showing the whole changelog instead of this release.')
}
if ($publishedHeadings[0].Groups[1].Value -ne $Version) {
    throw ("The published release notes are headed " + $publishedHeadings[0].Groups[1].Value +
           " but the release is $Version.")
}
if ($published -notmatch '###') {
    throw 'The published release notes have no sections, so they say nothing about what changed.'
}

Write-Host ("Release verified: " + $tag + " id=" + $final.id)
Write-Host ("Notes: " + $published.Length + " characters, " +
            (([regex]::Matches($published, '(?m)^###\s+(.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() }) -join ', '))
Write-Host ("Draft=" + $final.draft + " Prerelease=" + $final.prerelease)
