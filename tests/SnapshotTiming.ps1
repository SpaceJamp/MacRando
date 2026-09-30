# Measures what a refresh actually costs on this machine, and checks the combined snapshot
# is not a regression.
#
# Read-only: every variant only queries.
#
# Expectations, so the numbers are not read as a surprise. On this machine a PowerShell
# process costs about 190ms before it runs a line of script, and the networking cmdlets
# dominate the rest: Get-NetAdapter roughly 630ms, Get-NetAdapterAdvancedProperty roughly
# 650ms, Get-NetIPConfiguration roughly 1640ms, and the two Get-VpnConnection calls roughly
# 360ms. Folding the VPN query into the adapter query therefore saves one process launch
# plus the cmdlet load that goes with it, which is the bulk of the 360ms.
#
# Two variants are compared, not three. The intermediate "hoisted but still two launches"
# variant was measured while building this and is recorded in the changelog; keeping it here
# meant keeping a second copy of the per-adapter body correct, which is more risk than the
# extra data point is worth.
param()

$ErrorActionPreference = 'Stop'

$preamble = @"
`$ErrorActionPreference = 'Stop'
`$ProgressPreference = 'SilentlyContinue'
function Write-MacRandoJson64([object]`$Value) {
  `$json = ConvertTo-Json -InputObject `$Value -Depth 8 -Compress
  [Console]::Out.WriteLine('__MACRANDO_JSON__')
  [Console]::Out.WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(`$json)))
}
"@

# The per-adapter body, shared by all three variants. Only how NetworkAddress is looked up
# and what the script wraps its result in differ between them.
$adapterBody = @'
$items = @()
$macPropertiesByPrefix = @{}
foreach ($property in @(Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction SilentlyContinue |
    Where-Object { $_.RegistryKeyword -eq 'NetworkAddress' })) {
  $instance = [string]$property.InstanceID
  if ([string]::IsNullOrWhiteSpace($instance)) { continue }
  $separator = $instance.IndexOf('::')
  if ($separator -le 0) { continue }
  $macPropertiesByPrefix[$instance.Substring(0, $separator + 2)] = $property
}
foreach ($adapter in @(Get-NetAdapter -ErrorAction Stop | Where-Object { $_.InterfaceGuid })) {
  $ipAddress = ''
  $prefixLength = 0
  $dhcpEnabled = $false
  $adapterPrefix = ([string]$adapter.InterfaceGuid) + '::'
  $macProperty = $macPropertiesByPrefix[$adapterPrefix]
  try {
    $configuration = Get-NetIPConfiguration -InterfaceIndex $adapter.InterfaceIndex -ErrorAction Stop
    $address = @(Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop |
      Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -and $_.IPAddress -notlike '169.254.*' } |
      Select-Object -First 1)[0]
    if ($null -ne $address) {
      $ipAddress = [string]$address.IPAddress
      $prefixLength = [int]$address.PrefixLength
    }
    $interface = @(Get-NetIPInterface -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)[0]
    if ($null -ne $interface) {
      $dhcpEnabled = ([string]$interface.Dhcp -eq 'Enabled')
    }
  } catch { }
  $items += [pscustomobject]@{
    Name = [string]$adapter.Name
    Description = [string]$adapter.InterfaceDescription
    InterfaceIndex = [int]$adapter.InterfaceIndex
    MacAddress = [string]$adapter.MacAddress
    PermanentMacAddress = [string]$adapter.PermanentAddress
    MacPropertySupported = ($null -ne $macProperty)
    LinkSpeed = [string]$adapter.LinkSpeed
    InterfaceGuid = [string]$adapter.InterfaceGuid
    Status = [string]$adapter.Status
    IsUp = ([string]$adapter.Status -eq 'Up')
    IpAddress = $ipAddress
    PrefixLength = $prefixLength
    DhcpEnabled = $dhcpEnabled
    MediaType = [string]$adapter.MediaType
    NdisPhysicalMedium = [int]$adapter.NdisPhysicalMedium
    HardwareInterface = [bool]$adapter.HardwareInterface
  }
}
'@

$vpnBody = @'
$vpnProfiles = @()
try { $vpnProfiles += @(Get-VpnConnection -ErrorAction SilentlyContinue) } catch { }
try { $vpnProfiles += @(Get-VpnConnection -AllUserConnection -ErrorAction SilentlyContinue) } catch { }
$unique = @{}
foreach ($profile in $vpnProfiles) {
  if ($null -eq $profile -or [string]::IsNullOrWhiteSpace([string]$profile.Name)) { continue }
  $unique[[string]$profile.Name] = [pscustomobject]@{
    Name = [string]$profile.Name
    ServerAddress = [string]$profile.ServerAddress
    TunnelType = [string]$profile.TunnelType
    SplitTunneling = [bool]$profile.SplitTunneling
  }
}
'@

# "before": the lookup ran once per adapter, re-scanning every adapter each time. Written
# out rather than derived from $adapterBody with a regex, because a replacement that has to
# match a multi-line block exactly is a second copy of the code to keep correct.
$beforeAdapter = @'
$items = @()
foreach ($adapter in @(Get-NetAdapter -ErrorAction Stop | Where-Object { $_.InterfaceGuid })) {
  $ipAddress = ''
  $prefixLength = 0
  $dhcpEnabled = $false
  $adapterPrefix = ([string]$adapter.InterfaceGuid) + '::'
  $macProperty = @(Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction SilentlyContinue | Where-Object {
    $_.RegistryKeyword -eq 'NetworkAddress' -and
    ([string]$_.InstanceID).StartsWith($adapterPrefix, [System.StringComparison]::OrdinalIgnoreCase)
  })[0]
  try {
    $configuration = Get-NetIPConfiguration -InterfaceIndex $adapter.InterfaceIndex -ErrorAction Stop
    $address = @(Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop |
      Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -and $_.IPAddress -notlike '169.254.*' } |
      Select-Object -First 1)[0]
    if ($null -ne $address) {
      $ipAddress = [string]$address.IPAddress
      $prefixLength = [int]$address.PrefixLength
    }
    $interface = @(Get-NetIPInterface -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)[0]
    if ($null -ne $interface) {
      $dhcpEnabled = ([string]$interface.Dhcp -eq 'Enabled')
    }
  } catch { }
  $items += [pscustomobject]@{
    Name = [string]$adapter.Name
    Description = [string]$adapter.InterfaceDescription
    InterfaceIndex = [int]$adapter.InterfaceIndex
    MacAddress = [string]$adapter.MacAddress
    PermanentMacAddress = [string]$adapter.PermanentAddress
    MacPropertySupported = ($null -ne $macProperty)
    LinkSpeed = [string]$adapter.LinkSpeed
    InterfaceGuid = [string]$adapter.InterfaceGuid
    Status = [string]$adapter.Status
    IsUp = ([string]$adapter.Status -eq 'Up')
    IpAddress = $ipAddress
    PrefixLength = $prefixLength
    DhcpEnabled = $dhcpEnabled
    MediaType = [string]$adapter.MediaType
    NdisPhysicalMedium = [int]$adapter.NdisPhysicalMedium
    HardwareInterface = [bool]$adapter.HardwareInterface
  }
}
'@

$beforeScripts = @(
    ($beforeAdapter + "`nWrite-MacRandoJson64 -Value @(`$items)"),
    ($vpnBody + "`nWrite-MacRandoJson64 -Value @(`$unique.Values)")
)
$currentScripts = @(
    ($adapterBody + $vpnBody + "`nWrite-MacRandoJson64 -Value ([pscustomobject]@{`n  Adapters = @(`$items)`n  VpnProfiles = @(`$unique.Values)`n})")
)

function Invoke-Script([string]$script) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($preamble + $script))
    $out = & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded 2>&1
    $sw.Stop()
    if ($LASTEXITCODE -ne 0) { throw ("script failed: " + ($out -join ' ')) }
    return $sw.ElapsedMilliseconds
}

function Measure-Variant([string[]]$scripts) {
    $total = 0
    foreach ($script in $scripts) { $total += Invoke-Script $script }
    return $total
}

# Warm-up, discarded. The first invocation pays for loading the cmdlets from disk, so
# whichever variant ran first would otherwise look slowest for no real reason.
Write-Host '  warming up (discarded)...'
Measure-Variant $beforeScripts | Out-Null
Measure-Variant $currentScripts | Out-Null

$iterations = 5
$before = 0
$current = 0
for ($i = 0; $i -lt $iterations; $i++) {
    $before += Measure-Variant $beforeScripts
    $current += Measure-Variant $currentScripts
}

Write-Host ("  before (" + $beforeScripts.Count + " launches): " + [math]::Round($before / $iterations) + "ms per refresh")
Write-Host ("  current (" + $currentScripts.Count + " launch ): " + [math]::Round($current / $iterations) + "ms per refresh")
Write-Host ("  saved : " + [math]::Round(($before - $current) / $iterations) + "ms per refresh")

if ($current -gt $before) {
    throw ("The current refresh path is SLOWER than the one it replaced: " + $current + "ms against " + $before + "ms over " + $iterations + " refreshes. The change is not worth keeping.")
}

Write-Host 'snapshot-timing=OK'
