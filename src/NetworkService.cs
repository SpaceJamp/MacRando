using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MacRando
{
    /// <summary>
    /// Shape returned by the network-identity PowerShell query. Kept separate from
    /// <see cref="NetworkIdentity"/> so the deserialized payload stays minimal and the key
    /// and description are always derived in one place.
    /// </summary>
    internal sealed class NetworkProfileRaw
    {
        public string ConnectionProfile { get; set; }
        public string Gateway { get; set; }
        public string Ssid { get; set; }
    }
    internal sealed class NetworkService
    {
        private const int PowerShellTimeoutMilliseconds = 30000;
        private const int VpnTimeoutMilliseconds = 45000;
        private static readonly HttpClient PublicIpClient = CreatePublicIpClient();
        private readonly IPowerShellRunner _runner;

        public NetworkService()
            : this(new WindowsPowerShellRunner())
        {
        }

        internal NetworkService(IPowerShellRunner runner)
        {
            if (runner == null)
            {
                throw new ArgumentNullException("runner");
            }
            _runner = runner;
        }

        /// <summary>
        /// Read-only. Identifies the network the machine is attached to, so a preset can be
        /// bound to it. Never changes anything, and returns an unusable identity rather than
        /// throwing when the information is unavailable.
        /// </summary>
        private const string NetworkIdentityScript = @"
$profileName = ''
try {
  $profile = @(Get-NetConnectionProfile -ErrorAction Stop |
    Where-Object { $_.IPv4Connectivity -and $_.IPv4Connectivity -ne 'Disconnected' } |
    Sort-Object -Property @{ Expression = { $_.IPv4Connectivity -eq 'Internet' }; Descending = $true } |
    Select-Object -First 1)[0]
  if ($null -ne $profile) { $profileName = [string]$profile.Name }
} catch { $profileName = '' }

$gateway = ''
try {
  $configuration = @(Get-NetIPConfiguration -ErrorAction Stop |
    Where-Object { $_.IPv4DefaultGateway } | Select-Object -First 1)[0]
  if ($null -ne $configuration) {
    $route = @($configuration.IPv4DefaultGateway | Where-Object { $_.NextHop } | Select-Object -First 1)[0]
    if ($null -ne $route) { $gateway = [string]$route.NextHop }
  }
} catch { $gateway = '' }

$ssid = ''
try {
  $lines = @(& netsh.exe wlan show interfaces 2>$null)
  $index = 0
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*SSID\s*:') {
      # netsh prints the state first with an empty SSID when it is not connected, so the
      # first non-empty value is the network actually joined.
      $value = ($lines[$i] -split ':', 2)[1].Trim()
      if ($value.Length -gt 0) { $ssid = $value; break }
    }
  }
} catch { $ssid = '' }

Write-MacRandoJson64 ([pscustomobject]@{
  ConnectionProfile = $profileName
  Gateway           = $gateway
  Ssid              = $ssid
})
";

        private const string AdapterScript = @"
$items = @()
# All adapters, not just -Physical: an unflagged virtual machine host adapter or tunnel
# is exactly the case that needs classifying, and filtering it out here would hide it
# instead of labelling it. MacRando decides what is safe to change, in C#, from the
# driver strings and the media type read below.
#
# The NetworkAddress lookup is hoisted out of the loop below, and that is the change that
# actually made a refresh fast. Get-NetAdapterAdvancedProperty -AllProperties enumerates
# the advanced properties of every adapter on the machine, so asking for it once per
# adapter re-scanned the whole set N times to pick out one row per adapter. It was by far
# the most expensive query in a refresh. Keyed on the GUID prefix the InstanceID uses, so
# the per-adapter lookup is now a hashtable hit.
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
  } catch {
    # A connected adapter may briefly have no IPv4 configuration.
  }
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
    # Read-only extras. The filter above already drops adapters Windows flags as virtual,
    # but a virtual machine host adapter or a third-party tunnel registers a real NDIS
    # miniport and is not flagged at all, so it reaches this list looking like hardware.
    # MediaType and PhysicalMediaType come from the driver and cost nothing to read.
    MediaType = [string]$adapter.MediaType
    NdisPhysicalMedium = [int]$adapter.NdisPhysicalMedium
    HardwareInterface = [bool]$adapter.HardwareInterface
  }
}
Write-MacRandoJson64 -Value @($items)
";

        private const string StateScript = @"
$adapterGuid = [string]$env:MR_ADAPTER_GUID
if ([string]::IsNullOrWhiteSpace($adapterGuid)) { throw 'The selected adapter has no stable interface GUID.' }
$matches = @(Get-NetAdapter -ErrorAction Stop | Where-Object {
  ([string]$_.InterfaceGuid) -eq $adapterGuid
})
if ($matches.Count -ne 1) { throw 'The selected adapter GUID is not unique or is no longer available.' }
$adapter = $matches[0]

function Convert-MacRandoDnsList([object]$Value) {
  if ($null -eq $Value) { return @() }
  return @(([string]$Value -split '[;, ]+') | Where-Object { $_ } | ForEach-Object {
    try { [Net.IPAddress]::Parse([string]$_).ToString() } catch { [string]$_ }
  } | Sort-Object -Unique)
}

$adapterPrefix = ([string]$adapter.InterfaceGuid) + '::'
$property = @(Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction SilentlyContinue | Where-Object {
  $_.RegistryKeyword -eq 'NetworkAddress' -and
  ([string]$_.InstanceID).StartsWith($adapterPrefix, [System.StringComparison]::OrdinalIgnoreCase)
})[0]
$macSupported = ($null -ne $property)
$macValue = ([string]$adapter.MacAddress) -replace '[-:\s]', ''
$macOverrideValue = ''
$macOverridePresent = $false
$macDisplayValue = ''
if ($macSupported) {
  $macDisplayValue = [string]$property.DisplayValue
  if (-not [string]::IsNullOrWhiteSpace([string]$property.RegistryValue)) {
    $macOverridePresent = $true
    $macOverrideValue = [string]$property.RegistryValue
  }
}

$addresses = @(Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction Stop |
  Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -ne '0.0.0.0' })
$preferredIps = @($addresses | ForEach-Object { [string]$_.IPAddress })
$primary = $addresses[0]
$ipAddress = ''
$prefixLength = 0
if ($null -ne $primary) {
  $ipAddress = [string]$primary.IPAddress
  $prefixLength = [int]$primary.PrefixLength
}

$configuration = $null
$configurationKnown = $false
try {
  $configuration = Get-NetIPConfiguration -InterfaceIndex $adapter.InterfaceIndex -ErrorAction Stop
  $configurationKnown = ($null -ne $configuration)
} catch {
  $configurationKnown = $false
}
$gateway = ''
$dnsServers = @()
if ($null -ne $configuration) {
  $route = @($configuration.IPv4DefaultGateway | Where-Object { $_.NextHop } | Select-Object -First 1)[0]
  if ($null -ne $route) { $gateway = [string]$route.NextHop }
  if ($null -ne $configuration.DNSServer) {
    $dnsServers = @($configuration.DNSServer.ServerAddresses | Where-Object { $_ })
  }
}

$staticDnsServers = @()
$dhcpDnsServers = @()
$dnsPolicyKnown = $false
$tcpipPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\' + [string]$adapter.InterfaceGuid
$tcpip6Path = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\' + [string]$adapter.InterfaceGuid
try {
  $registry = Get-ItemProperty -LiteralPath $tcpipPath -ErrorAction SilentlyContinue
  if ($null -ne $registry) {
    $dnsPolicyKnown = $true
    $staticDnsServers += Convert-MacRandoDnsList $registry.NameServer
    $dhcpDnsServers += Convert-MacRandoDnsList $registry.DhcpNameServer
  }
} catch { }
try {
  $registry6 = Get-ItemProperty -LiteralPath $tcpip6Path -ErrorAction SilentlyContinue
  if ($null -ne $registry6) {
    $dnsPolicyKnown = $true
    $staticDnsServers += Convert-MacRandoDnsList $registry6.NameServer
    $dhcpDnsServers += Convert-MacRandoDnsList $registry6.DhcpNameServer
  }
} catch { }
$staticDnsServers = @($staticDnsServers | Sort-Object -Unique)
$dhcpDnsServers = @($dhcpDnsServers | Sort-Object -Unique)

$interface = @(Get-NetIPInterface -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)[0]
$dhcpEnabled = ($null -ne $interface -and [string]$interface.Dhcp -eq 'Enabled')

[pscustomobject]@{
  AdapterName = [string]$adapter.Name
  InterfaceGuid = [string]$adapter.InterfaceGuid
  InterfaceIndex = [int]$adapter.InterfaceIndex
  CurrentMacAddress = [string]$adapter.MacAddress
  MacRegistryValue = $macValue
  MacOverrideValue = $macOverrideValue
  MacOverridePresent = $macOverridePresent
  MacDisplayValue = $macDisplayValue
  MacPropertySupported = $macSupported
  IpAddress = $ipAddress
  PrefixLength = $prefixLength
  Gateway = $gateway
  DhcpEnabled = $dhcpEnabled
  ConfigurationKnown = $configurationKnown
  DnsPolicyKnown = $dnsPolicyKnown
  IpAddresses = @($preferredIps)
  StaticDnsServers = @($staticDnsServers)
  DhcpDnsServers = @($dhcpDnsServers)
  DnsServers = @($dnsServers)
  PreferredAddressCount = [int]$addresses.Count
} | ForEach-Object { Write-MacRandoJson64 -Value $_ }
";

        private const string ApplyScript = @"
$adapterGuid = [string]$env:MR_ADAPTER_GUID
if ([string]::IsNullOrWhiteSpace($adapterGuid)) { throw 'The selected adapter has no stable interface GUID.' }
$matches = @(Get-NetAdapter -ErrorAction Stop | Where-Object {
  ([string]$_.InterfaceGuid) -eq $adapterGuid
})
if ($matches.Count -ne 1) { throw 'The selected adapter GUID is not unique or is no longer available.' }
$adapter = $matches[0]
$index = [int]$adapter.InterfaceIndex

if ($env:MR_CHANGE_IP -eq '1') {
  $preflightAddresses = @(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop |
    Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -ne '0.0.0.0' })
  if ($preflightAddresses.Count -ne 1 -or
      [string]$preflightAddresses[0].IPAddress -ne $env:MR_OLD_IP -or
      [int]$preflightAddresses[0].PrefixLength -ne [int]$env:MR_EXPECTED_PREFIX) {
    throw 'The adapter IP configuration changed while MacRando was starting. Please try again.'
  }
  $preflightInterface = @(Get-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop)[0]
  $preflightDhcp = if ($null -ne $preflightInterface -and [string]$preflightInterface.Dhcp -eq 'Enabled') { '1' } else { '0' }
  if ($preflightDhcp -ne $env:MR_EXPECTED_DHCP) {
    throw 'The adapter DHCP state changed while MacRando was starting. Please try again.'
  }
  if (-not [string]::IsNullOrWhiteSpace($env:MR_EXPECTED_GATEWAY)) {
    $preflightConfiguration = Get-NetIPConfiguration -InterfaceIndex $index -ErrorAction Stop
    $preflightGateway = @($preflightConfiguration.IPv4DefaultGateway | Where-Object { $_.NextHop } | Select-Object -First 1)[0]
    if ($null -eq $preflightGateway -or [string]$preflightGateway.NextHop -ne $env:MR_EXPECTED_GATEWAY) {
      throw 'The adapter gateway changed while MacRando was starting. Please try again.'
    }
  }
}

if ($env:MR_CHANGE_MAC -eq '1') {
  $adapterPrefix = ([string]$adapter.InterfaceGuid) + '::'
  $property = @(Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction SilentlyContinue | Where-Object {
    $_.RegistryKeyword -eq 'NetworkAddress' -and
    ([string]$_.InstanceID).StartsWith($adapterPrefix, [System.StringComparison]::OrdinalIgnoreCase)
  })[0]
  if ($null -eq $property) {
    throw 'This adapter does not expose a configurable Network Address property.'
  }
  Set-NetAdapterAdvancedProperty -InputObject $property `
    -RegistryValue $env:MR_NEW_MAC -NoRestart -Confirm:$false -ErrorAction Stop | Out-Null
}

if ($env:MR_CHANGE_IP -eq '1') {
  $addresses = @(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop |
    Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -ne '0.0.0.0' })
  if ($addresses.Count -ne 1) {
    throw 'The adapter must have exactly one active IPv4 address before MacRando changes it.'
  }
  if ([string]$addresses[0].IPAddress -ne $env:MR_OLD_IP) {
    throw 'The adapter IP configuration changed while MacRando was starting. Please try again.'
  }

  Set-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -Dhcp Disabled -Confirm:$false -ErrorAction Stop
  $remainingOldAddresses = @(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -eq $env:MR_OLD_IP })
  foreach ($oldAddress in $remainingOldAddresses) {
    Remove-NetIPAddress -InputObject $oldAddress -Confirm:$false -ErrorAction Stop
  }

  $newAddress = @{
    InterfaceIndex = $index
    IPAddress = $env:MR_NEW_IP
    PrefixLength = [int]$env:MR_NEW_PREFIX
    ErrorAction = 'Stop'
  }
  New-NetIPAddress @newAddress | Out-Null

  if ([string]::IsNullOrWhiteSpace($env:MR_DNS)) {
    Set-DnsClientServerAddress -InterfaceIndex $index -ResetServerAddresses -ErrorAction Stop
  } else {
    $servers = @($env:MR_DNS -split '\|' | Where-Object { $_ })
    Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $servers -ErrorAction Stop
  }
}

Restart-NetAdapter -InputObject $adapter -Confirm:$false -ErrorAction Stop
";

        private const string RestoreScript = @"
$adapterGuid = [string]$env:MR_ADAPTER_GUID
if ([string]::IsNullOrWhiteSpace($adapterGuid)) { throw 'The saved adapter has no stable interface GUID.' }
$matches = @(Get-NetAdapter -ErrorAction Stop | Where-Object {
  ([string]$_.InterfaceGuid) -eq $adapterGuid
})
if ($matches.Count -ne 1) { throw 'The saved adapter GUID is not unique or is no longer available.' }
$adapter = $matches[0]
$index = [int]$adapter.InterfaceIndex

if ($env:MR_RESTORE_MAC -eq '1') {
  $adapterPrefix = ([string]$adapter.InterfaceGuid) + '::'
  $property = @(Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction SilentlyContinue | Where-Object {
    $_.RegistryKeyword -eq 'NetworkAddress' -and
    ([string]$_.InstanceID).StartsWith($adapterPrefix, [System.StringComparison]::OrdinalIgnoreCase)
  })[0]
  if ($null -eq $property) { throw 'The saved adapter no longer exposes a configurable Network Address property.' }
  if ($env:MR_ORIGINAL_MAC_OVERRIDE_PRESENT -eq '1') {
    Set-NetAdapterAdvancedProperty -InputObject $property `
      -RegistryValue $env:MR_ORIGINAL_MAC -NoRestart -Confirm:$false -ErrorAction Stop | Out-Null
  } else {
    $resetDisplayName = if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.DisplayName)) { [string]$property.DisplayName } else { 'Network Address' }
    Reset-NetAdapterAdvancedProperty -InputObject $property `
      -NoRestart -Confirm:$false -ErrorAction Stop | Out-Null
  }
}

if ($env:MR_RESTORE_IP -eq '1') {
  $originalIps = @($env:MR_ORIGINAL_IPS -split '\|' | Where-Object { $_ })
  if ($originalIps.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($env:MR_ORIGINAL_IP)) {
    $originalIps = @($env:MR_ORIGINAL_IP)
  }
  if ($originalIps.Count -eq 0) {
    throw 'The saved restore profile does not contain a valid original IPv4 address.'
  }

  Set-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -Dhcp Disabled -Confirm:$false -ErrorAction Stop
  $current = @(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction Stop |
    Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -ne '0.0.0.0' })
  foreach ($address in $current) {
    Remove-NetIPAddress -InputObject $address -Confirm:$false -ErrorAction Stop
  }

  if ($env:MR_ORIGINAL_DHCP -eq '1') {
    Set-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -Dhcp Enabled -Confirm:$false -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($env:MR_ORIGINAL_STATIC_DNS)) {
      Set-DnsClientServerAddress -InterfaceIndex $index -ResetServerAddresses -ErrorAction Stop
    } else {
      $staticServers = @($env:MR_ORIGINAL_STATIC_DNS -split '\|' | Where-Object { $_ })
      Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $staticServers -ErrorAction Stop
    }
  } else {
    $first = $true
    foreach ($originalIp in $originalIps) {
      $restoredAddress = @{
        InterfaceIndex = $index
        IPAddress = $originalIp
        PrefixLength = [int]$env:MR_ORIGINAL_PREFIX
        ErrorAction = 'Stop'
      }
      New-NetIPAddress @restoredAddress | Out-Null
      $first = $false
    }
    if ([string]::IsNullOrWhiteSpace($env:MR_ORIGINAL_DNS)) {
      Set-DnsClientServerAddress -InterfaceIndex $index -ResetServerAddresses -ErrorAction Stop
    } else {
      $servers = @($env:MR_ORIGINAL_DNS -split '\|' | Where-Object { $_ })
      Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $servers -ErrorAction Stop
    }
  }
}

Restart-NetAdapter -InputObject $adapter -Confirm:$false -ErrorAction Stop
";

        private const string VerifyScript = @"
$adapterGuid = [string]$env:MR_ADAPTER_GUID
if ([string]::IsNullOrWhiteSpace($adapterGuid)) { throw 'The saved adapter has no stable interface GUID.' }
$verifyMac = $env:MR_VERIFY_MAC -eq '1'
$verifyIp = $env:MR_VERIFY_IP -eq '1'
$lastProblems = @()

for ($attempt = 0; $attempt -lt 20; $attempt++) {
  $problems = @()
  $currentMatches = @(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object {
    ([string]$_.InterfaceGuid) -eq $adapterGuid
  })
  $currentAdapter = $null
  if ($currentMatches.Count -ne 1) {
    $problems += 'the adapter is not available during verification'
  } else {
    $currentAdapter = $currentMatches[0]
  }
  if ($verifyMac -and $null -ne $currentAdapter) {
    $actualMac = ([string]$currentAdapter.MacAddress) -replace '[^0-9A-Fa-f]', ''
    $expectedMac = ([string]$env:MR_EXPECTED_MAC) -replace '[^0-9A-Fa-f]', ''
    if ([string]::IsNullOrWhiteSpace($actualMac) -or $actualMac -ne $expectedMac) {
      $problems += ('MAC is ' + $actualMac + ', expected ' + $expectedMac)
    }
    $currentPrefix = ([string]$currentAdapter.InterfaceGuid) + '::'
    $property = @(Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction SilentlyContinue | Where-Object {
      $_.RegistryKeyword -eq 'NetworkAddress' -and
      ([string]$_.InstanceID).StartsWith($currentPrefix, [System.StringComparison]::OrdinalIgnoreCase)
    })[0]
    $actualOverridePresent = ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.RegistryValue))
    $expectedOverridePresent = $env:MR_EXPECTED_MAC_OVERRIDE_PRESENT -eq '1'
    if ($actualOverridePresent -ne $expectedOverridePresent) {
      $problems += 'the MAC override state does not match'
    }
    if ($expectedOverridePresent -and $null -ne $property) {
      $actualOverride = ([string]$property.RegistryValue) -replace '[^0-9A-Fa-f]', ''
      $expectedOverride = ([string]$env:MR_EXPECTED_MAC_OVERRIDE_VALUE) -replace '[^0-9A-Fa-f]', ''
      if ($actualOverride -ne $expectedOverride) {
        $problems += 'the saved MAC override value does not match'
      }
    }
  }

  if ($verifyIp -and $null -ne $currentAdapter) {
    $addresses = @(Get-NetIPAddress -InterfaceIndex $currentAdapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
      Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -ne '0.0.0.0' })
    $actualIps = @($addresses | ForEach-Object { [string]$_.IPAddress } | Sort-Object -Unique)
    $expectedIps = @($env:MR_EXPECTED_IPS -split '\|' | Where-Object { $_ } | Sort-Object -Unique)
    if ($expectedIps.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($env:MR_EXPECTED_IP)) {
      $expectedIps = @($env:MR_EXPECTED_IP)
    }
    $interface = @(Get-NetIPInterface -InterfaceIndex $currentAdapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)[0]
    $configuration = Get-NetIPConfiguration -InterfaceIndex $currentAdapter.InterfaceIndex -ErrorAction SilentlyContinue
    if ($null -eq $configuration) {
      $problems += 'the IP configuration is not available yet'
    }

    if ($env:MR_EXPECTED_DHCP -eq '1') {
      if ($null -eq $interface -or [string]$interface.Dhcp -ne 'Enabled') {
        $problems += 'DHCP is not enabled'
      }
      $usableLease = @($addresses | Where-Object {
        $_.IPAddress -notlike '169.254.*' -and $_.IPAddress -notlike '0.*' -and $_.IPAddress -notlike '127.*'
      })
      if ($usableLease.Count -eq 0) {
        $problems += 'no usable DHCP lease is assigned yet'
      }
      if (-not [string]::IsNullOrWhiteSpace($env:MR_MODIFIED_IP) -and
          @($addresses | Where-Object { $_.IPAddress -eq $env:MR_MODIFIED_IP }).Count -gt 0) {
        $problems += 'the randomized address is still assigned'
      }

      $expectedStaticDns = @($env:MR_EXPECTED_STATIC_DNS -split '\|' | Where-Object { $_ } | Sort-Object -Unique)
      $expectedDns = @($env:MR_EXPECTED_DNS -split '\|' | Where-Object { $_ } | Sort-Object -Unique)
      $actualDns = @()
      if ($null -ne $configuration -and $null -ne $configuration.DNSServer) {
        $actualDns = @($configuration.DNSServer.ServerAddresses | Where-Object { $_ } | ForEach-Object {
          try { [Net.IPAddress]::Parse([string]$_).ToString() } catch { [string]$_ }
        } | Sort-Object -Unique)
      }
      if ($expectedStaticDns.Count -gt 0) {
        if ($actualDns.Count -eq 0 -or @(Compare-Object -ReferenceObject $expectedStaticDns -DifferenceObject $actualDns).Count -gt 0) {
          $problems += 'the manually configured DNS servers are not assigned'
        }
      } elseif ($expectedDns.Count -gt 0 -and $actualDns.Count -eq 0) {
        $problems += 'DNS servers are not available yet'
      }

      $actualGateway = ''
      if ($null -ne $configuration) {
        $gateway = @($configuration.IPv4DefaultGateway | Where-Object { $_.NextHop } | Select-Object -First 1)[0]
        if ($null -ne $gateway) { $actualGateway = [string]$gateway.NextHop }
      }
      if ($actualGateway -ne $env:MR_EXPECTED_GATEWAY) {
        $problems += 'the DHCP gateway does not match'
      }
    } else {
      if ($null -eq $interface -or [string]$interface.Dhcp -eq 'Enabled') {
        $problems += 'DHCP is unexpectedly enabled'
      }
      if ($actualIps.Count -ne $expectedIps.Count -or
          @(Compare-Object -ReferenceObject $expectedIps -DifferenceObject $actualIps).Count -gt 0) {
        $problems += 'the original IPv4 address set does not match'
      }
      if (-not [string]::IsNullOrWhiteSpace($env:MR_MODIFIED_IP) -and
          @($addresses | Where-Object { $_.IPAddress -eq $env:MR_MODIFIED_IP }).Count -gt 0) {
        $problems += 'the randomized address is still assigned'
      }
      foreach ($expectedIp in $expectedIps) {
        $address = @($addresses | Where-Object { $_.IPAddress -eq $expectedIp } | Select-Object -First 1)[0]
        if ($null -eq $address -or [int]$address.PrefixLength -ne [int]$env:MR_EXPECTED_PREFIX) {
          $problems += 'the original IPv4 prefix does not match'
        }
      }

      $actualGateway = ''
      if ($null -ne $configuration) {
        $gateway = @($configuration.IPv4DefaultGateway | Where-Object { $_.NextHop } | Select-Object -First 1)[0]
        if ($null -ne $gateway) { $actualGateway = [string]$gateway.NextHop }
      }
      if ($actualGateway -ne $env:MR_EXPECTED_GATEWAY) {
        $problems += 'the original gateway does not match'
      }

      $expectedDns = @($env:MR_EXPECTED_DNS -split '\|' | Where-Object { $_ } | ForEach-Object {
        try { [Net.IPAddress]::Parse([string]$_).ToString() } catch { [string]$_ }
      } | Sort-Object -Unique)
      $actualDns = @()
      if ($null -ne $configuration -and $null -ne $configuration.DNSServer) {
        $actualDns = @($configuration.DNSServer.ServerAddresses | Where-Object { $_ } | ForEach-Object {
          try { [Net.IPAddress]::Parse([string]$_).ToString() } catch { [string]$_ }
        } | Sort-Object -Unique)
      }
      if ($expectedDns.Count -ne $actualDns.Count -or
          ($expectedDns.Count -gt 0 -and @(Compare-Object -ReferenceObject $expectedDns -DifferenceObject $actualDns).Count -gt 0)) {
        $problems += 'the original DNS servers do not match'
      }
    }
  }

  if ($problems.Count -eq 0) { return }
  $lastProblems = $problems
  Start-Sleep -Milliseconds 750
}

throw ('Adapter verification failed: ' + ($lastProblems -join '; '))
";

        private const string VpnListScript = @"
$allProfiles = @()
try { $allProfiles += @(Get-VpnConnection -ErrorAction SilentlyContinue) } catch { }
try { $allProfiles += @(Get-VpnConnection -AllUserConnection -ErrorAction SilentlyContinue) } catch { }
$unique = @{}
foreach ($profile in $allProfiles) {
  if ($null -eq $profile -or [string]::IsNullOrWhiteSpace([string]$profile.Name)) { continue }
  $unique[[string]$profile.Name] = [pscustomobject]@{
    Name = [string]$profile.Name
    ServerAddress = [string]$profile.ServerAddress
    TunnelType = [string]$profile.TunnelType
    SplitTunneling = [bool]$profile.SplitTunneling
  }
}
Write-MacRandoJson64 -Value @($unique.Values)
";

        /// <summary>
        /// One script returning both the adapter list and the VPN profile list.
        ///
        /// Identical in what it queries to <see cref="AdapterScript"/> and
        /// <see cref="VpnListScript"/>, which remain for the paths that need one list on
        /// its own. Kept as a separate literal rather than assembled from the other two
        /// because PowerShell here is a verbatim string: the body has to read as a script
        /// on its own for anyone reading this file, and the tests run the real thing
        /// against a live machine.
        ///
        /// The two halves are independent, so neither depends on the other having
        /// succeeded. A machine with no VPN profiles, or a cmdlet missing entirely, still
        /// gets a full adapter list back rather than a failed refresh.
        /// </summary>
        private const string SnapshotScript = @"
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
  } catch {
    # A connected adapter may briefly have no IPv4 configuration.
  }
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

Write-MacRandoJson64 -Value ([pscustomobject]@{
  Adapters = @($items)
  VpnProfiles = @($unique.Values)
})
";

        private const string VpnActionScript = @"
$arguments = @($env:MR_VPN_NAME)
if ($env:MR_VPN_DISCONNECT -eq '1') { $arguments += '/disconnect' }
$output = & ($env:WINDIR + '\System32\rasdial.exe') @arguments 2>&1
$code = $LASTEXITCODE
if ($code -ne 0) {
  $detail = (($output | Out-String) -replace '\s+', ' ').Trim()
  if ([string]::IsNullOrWhiteSpace($detail)) { $detail = 'rasdial returned exit code ' + $code }
  throw $detail
}
";

        public Task<List<AdapterInfo>> GetAdaptersAsync()
        {
            return _runner.RunJsonAsync<List<AdapterInfo>>(
                AdapterScript, null, PowerShellTimeoutMilliseconds);
        }

        /// <summary>
        /// Everything a refresh needs that Windows will only answer by running a script.
        ///
        /// The adapter list and the VPN profile list were two sequential process launches,
        /// each paying the full cost of starting PowerShell and loading the networking
        /// cmdlets before it can answer a question. They are independent, so one script
        /// returning both halves the launches per refresh. Public IP is deliberately not
        /// here: that is an HTTP call, and folding it in would make a dashboard refresh
        /// wait on a third-party service.
        ///
        /// Split out as a type so the payload stays a contract that can be asserted, and
        /// so a field added to one list without the other is a compile error rather than a
        /// silently empty half of the dashboard.
        /// </summary>
        public async Task<NetworkSnapshot> GetSnapshotAsync()
        {
            var raw = await _runner.RunJsonAsync<NetworkSnapshotRaw>(
                SnapshotScript, null, PowerShellTimeoutMilliseconds);
            if (raw == null)
            {
                throw new InvalidOperationException("Windows returned no network snapshot.");
            }

            return new NetworkSnapshot
            {
                Adapters = raw.Adapters ?? new List<AdapterInfo>(),
                VpnProfiles = raw.VpnProfiles ?? new List<VpnProfile>()
            };
        }

        public Task<NetworkState> GetStateAsync(AdapterInfo adapter)
        {
            return GetStateAsync(adapter, PowerShellTimeoutMilliseconds);
        }

        public Task<NetworkState> GetStateAsync(AdapterInfo adapter, int timeoutMilliseconds)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException("adapter");
            }

            Dictionary<string, string> environment = AdapterEnvironment(adapter);
            return _runner.RunJsonAsync<NetworkState>(
                StateScript, environment, timeoutMilliseconds);
        }

        public Task ApplyChangesAsync(
            AdapterInfo adapter,
            NetworkState originalState,
            string newMac,
            string newIp,
            bool changeMac,
            bool changeIp)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException("adapter");
            }

            Dictionary<string, string> environment = AdapterEnvironment(adapter);
            environment["MR_CHANGE_MAC"] = changeMac ? "1" : "0";
            environment["MR_CHANGE_IP"] = changeIp ? "1" : "0";
            environment["MR_NEW_MAC"] = NormalizeMac(newMac);
            environment["MR_NEW_IP"] = newIp ?? string.Empty;
            environment["MR_NEW_PREFIX"] = originalState.PrefixLength.ToString();
            environment["MR_EXPECTED_PREFIX"] = originalState.PrefixLength.ToString();
            environment["MR_OLD_IP"] = originalState.IpAddress ?? string.Empty;
            environment["MR_ORIGINAL_IPS"] = JoinDns(originalState.IpAddresses);
            environment["MR_EXPECTED_DHCP"] = originalState.DhcpEnabled ? "1" : "0";
            environment["MR_GATEWAY"] = originalState.Gateway ?? string.Empty;
            environment["MR_EXPECTED_GATEWAY"] = originalState.Gateway ?? string.Empty;
            environment["MR_DNS"] = JoinDns(originalState.DnsServers);
            return RunNonJsonAsync(ApplyScript, environment);
        }

        public Task RestoreAsync(AdapterBackup backup)
        {
            if (backup == null)
            {
                throw new ArgumentNullException("backup");
            }

            if (string.IsNullOrWhiteSpace(backup.InterfaceGuid))
            {
                throw new InvalidOperationException("The saved adapter has no stable interface GUID.");
            }

            if (!backup.MacChanged && !backup.IpChanged)
            {
                throw new InvalidDataException("The saved restore profile contains no requested changes.");
            }

            if (backup.IpChanged &&
                (backup.OriginalPrefixLength < 1 || backup.OriginalPrefixLength > 30 ||
                 string.IsNullOrWhiteSpace(backup.OriginalIpAddress)))
            {
                throw new InvalidDataException("The saved restore profile has incomplete IP settings.");
            }

            Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "MR_ADAPTER_NAME", backup.AdapterName ?? string.Empty },
                { "MR_ADAPTER_GUID", backup.InterfaceGuid ?? string.Empty },
                { "MR_RESTORE_MAC", backup.MacChanged ? "1" : "0" },
                { "MR_RESTORE_IP", backup.IpChanged ? "1" : "0" },
                { "MR_ORIGINAL_MAC", NormalizeMac(
                    string.IsNullOrWhiteSpace(backup.OriginalMacOverrideValue)
                        ? backup.OriginalMacRegistryValue
                        : backup.OriginalMacOverrideValue) },
                { "MR_EXPECTED_MAC", GetOriginalMacAddress(backup) },
                { "MR_ORIGINAL_MAC_OVERRIDE_PRESENT", HasMacOverride(backup) ? "1" : "0" },
                { "MR_ORIGINAL_DHCP", backup.OriginalDhcpEnabled ? "1" : "0" },
                { "MR_MODIFIED_IP", backup.ModifiedIpAddress ?? string.Empty },
                { "MR_ORIGINAL_IP", backup.OriginalIpAddress ?? string.Empty },
                { "MR_ORIGINAL_IPS", JoinDns(backup.OriginalIpAddresses) },
                { "MR_ORIGINAL_PREFIX", backup.OriginalPrefixLength.ToString() },
                { "MR_ORIGINAL_GATEWAY", backup.OriginalGateway ?? string.Empty },
                { "MR_ORIGINAL_DNS", JoinDns(backup.OriginalDnsServers) },
                { "MR_ORIGINAL_STATIC_DNS", JoinDns(backup.OriginalStaticDnsServers) }
            };
            return RunNonJsonAsync(RestoreScript, environment);
        }

        public Task VerifyRestoreAsync(AdapterBackup backup)
        {
            if (backup == null)
            {
                throw new ArgumentNullException("backup");
            }

            if (string.IsNullOrWhiteSpace(backup.InterfaceGuid))
            {
                throw new InvalidOperationException("The saved adapter has no stable interface GUID.");
            }

            if (!backup.MacChanged && !backup.IpChanged)
            {
                throw new InvalidDataException("The saved restore profile contains no requested changes.");
            }

            if (backup.IpChanged &&
                (backup.OriginalPrefixLength < 1 || backup.OriginalPrefixLength > 30 ||
                 string.IsNullOrWhiteSpace(backup.OriginalIpAddress)))
            {
                throw new InvalidDataException("The saved restore profile has incomplete IP settings.");
            }

            Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "MR_ADAPTER_GUID", backup.InterfaceGuid },
                { "MR_VERIFY_MAC", backup.MacChanged ? "1" : "0" },
                { "MR_VERIFY_IP", backup.IpChanged ? "1" : "0" },
                { "MR_EXPECTED_MAC", GetOriginalMacAddress(backup) },
                { "MR_EXPECTED_MAC_OVERRIDE_PRESENT", HasMacOverride(backup) ? "1" : "0" },
                { "MR_EXPECTED_MAC_OVERRIDE_VALUE", NormalizeMac(
                    string.IsNullOrWhiteSpace(backup.OriginalMacOverrideValue)
                        ? backup.OriginalMacRegistryValue
                        : backup.OriginalMacOverrideValue) },
                { "MR_EXPECTED_DHCP", backup.OriginalDhcpEnabled ? "1" : "0" },
                { "MR_MODIFIED_IP", backup.ModifiedIpAddress ?? string.Empty },
                { "MR_EXPECTED_IP", backup.OriginalIpAddress ?? string.Empty },
                { "MR_EXPECTED_IPS", JoinDns(backup.OriginalIpAddresses) },
                { "MR_EXPECTED_PREFIX", backup.OriginalPrefixLength.ToString() },
                { "MR_EXPECTED_GATEWAY", backup.OriginalGateway ?? string.Empty },
                { "MR_EXPECTED_DNS", JoinDns(backup.OriginalDnsServers) },
                { "MR_EXPECTED_STATIC_DNS", JoinDns(backup.OriginalStaticDnsServers) }
            };
            return RunNonJsonAsync(VerifyScript, environment, 30000);
        }

        public async Task VerifyAppliedAsync(
            AdapterInfo adapter,
            NetworkState originalState,
            string newMac,
            string newIp,
            bool changeMac,
            bool changeIp)
        {
            if (!changeMac && !changeIp)
            {
                return;
            }

            string lastProblem = "The adapter did not reach the requested state.";
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    NetworkState current = await GetStateAsync(adapter, 8000);
                    string problem = ValidateAppliedState(current, originalState, newMac, newIp, changeMac, changeIp);
                    if (string.IsNullOrWhiteSpace(problem))
                    {
                        return;
                    }

                    lastProblem = problem;
                }
                catch (Exception error)
                {
                    lastProblem = error.Message;
                }

                if (attempt < 4)
                {
                    await Task.Delay(750);
                }
            }

            throw new InvalidOperationException(lastProblem + " The restore profile was kept.");
        }

        public async Task<NetworkIdentity> GetNetworkIdentityAsync()
        {
            NetworkIdentity identity = new NetworkIdentity();
            try
            {
                NetworkProfileRaw raw = await _runner.RunJsonAsync<NetworkProfileRaw>(
                    NetworkIdentityScript, null, PowerShellTimeoutMilliseconds);
                string profile = raw == null ? null : raw.ConnectionProfile;
                string gateway = raw == null ? null : raw.Gateway;
                string ssid = raw == null ? null : raw.Ssid;
                identity.ConnectionProfile = profile;
                identity.Gateway = gateway;
                identity.Ssid = ssid;
                identity.Key = NetworkIdentity.BuildKey(profile, gateway, ssid);
                identity.Description = NetworkIdentity.BuildDescription(profile, gateway, ssid);
            }
            catch (Exception error)
            {
                // An unidentified network is not worth interrupting the user for, since the
                // consequence is only that no preset can match. It is logged as a warning
                // because it usually means the query itself is broken.
                AppLogger.Warning("Could not identify the current network: " + AppLogger.Sanitize(error.Message));
                identity.Description = "unknown network";
            }
            return identity;
        }

        public Task<List<VpnProfile>> GetVpnProfilesAsync()        {
            return _runner.RunJsonAsync<List<VpnProfile>>(
                VpnListScript, null, PowerShellTimeoutMilliseconds);
        }

        public Task RunVpnActionAsync(VpnProfile profile, bool disconnect)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "MR_VPN_NAME", profile.Name ?? string.Empty },
                { "MR_VPN_DISCONNECT", disconnect ? "1" : "0" }
            };
            return RunNonJsonAsync(VpnActionScript, environment, VpnTimeoutMilliseconds);
        }

        public async Task<string> GetPublicIpAsync()
        {
            using (HttpResponseMessage response = await PublicIpClient.GetAsync("https://api.ipify.org"))
            {
                response.EnsureSuccessStatusCode();
                string value = (await response.Content.ReadAsStringAsync()).Trim();
                IPAddress parsed;
                if (!IPAddress.TryParse(value, out parsed))
                {
                    throw new InvalidDataException("The public IP service returned an invalid address.");
                }

                return value;
            }
        }

        /// <summary>
        /// Fetches geolocation data for the public IP from ipwhois.io.
        /// Only called when the user has opted in via ShowPublicIpLocation setting.
        /// </summary>
        public async Task<PublicIpGeolocation> GetPublicIpGeolocationAsync(string publicIp)
        {
            if (string.IsNullOrWhiteSpace(publicIp))
            {
                return null;
            }

            try
            {
                // ipwhois.io: HTTPS, no API key required, 10k req/month free, GDPR compliant
                string url = "https://ipwhois.io/" + publicIp + "?fields=country,region,city,isp,asn,timezone";
                using (HttpResponseMessage response = await PublicIpClient.GetAsync(url))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }
                    string json = await response.Content.ReadAsStringAsync();
                    var data = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<PublicIpGeolocation>(json);
                    if (data != null && data.Success)
                    {
                        return data;
                    }
                }
            }
            catch
            {
                // Silently fail - geolocation is a nice-to-have, not critical
            }
            return null;
        }

        /// <summary>
        /// DTO for ipwhois.io JSON response.
        /// </summary>
        internal sealed class PublicIpGeolocation
        {
            public bool Success { get; set; }
            public string Country { get; set; }
            public string Region { get; set; }
            public string City { get; set; }
            public string Isp { get; set; }
            public string Asn { get; set; }
            public string Timezone { get; set; }
        }

        public IPAddress FindRandomLocalAddress(NetworkState state)
        {
            return FindRandomLocalAddress(state, true);
        }

        internal IPAddress FindRandomLocalAddress(NetworkState state, bool checkConflicts)
        {
            if (state == null || !state.HasUsableIpv4)
            {
                throw new InvalidOperationException(
                    "The selected adapter does not have a usable IPv4 subnet. Connect it to a network and try again.");
            }

            IPAddress original;
            if (!IPAddress.TryParse(state.IpAddress, out original) || original.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                throw new InvalidOperationException("The selected adapter has an invalid IPv4 address.");
            }

            if (state.PrefixLength < 1 || state.PrefixLength > 30)
            {
                throw new InvalidOperationException("The selected adapter has an invalid IPv4 prefix length.");
            }

            byte[] originalBytes = original.GetAddressBytes();
            if (originalBytes[0] == 0 || originalBytes[0] == 127 || originalBytes[0] >= 224 ||
                (originalBytes[0] == 169 && originalBytes[1] == 254))
            {
                throw new InvalidOperationException("The selected adapter is using an address range that is not suitable for randomization.");
            }

            uint originalValue = HostToUInt(original);
            uint mask = uint.MaxValue << (32 - state.PrefixLength);
            uint network = originalValue & mask;
            uint broadcast = network | ~mask;
            uint hostCount = broadcast - network - 1;
            if (hostCount == 0)
            {
                throw new InvalidOperationException("The selected subnet has no available host addresses.");
            }

            var excluded = new HashSet<uint>();
            excluded.Add(network);
            excluded.Add(broadcast);
            excluded.Add(originalValue);
            IPAddress gateway;
            if (IPAddress.TryParse(state.Gateway, out gateway) && gateway.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                excluded.Add(HostToUInt(gateway));
            }

            List<uint> smallPool = null;
            if (hostCount <= 1024)
            {
                smallPool = new List<uint>();
                for (uint candidate = network + 1; candidate < broadcast; candidate++)
                {
                    if (!excluded.Contains(candidate))
                    {
                        smallPool.Add(candidate);
                    }
                }
                Shuffle(smallPool);
            }

            var tried = new HashSet<uint>();
            for (int attempt = 0; attempt < 128; attempt++)
            {
                uint candidate;
                if (smallPool != null)
                {
                    if (attempt >= smallPool.Count)
                    {
                        break;
                    }

                    candidate = smallPool[attempt];
                }
                else
                {
                    candidate = network + 1 + NextRandomUInt32() % hostCount;
                }

                if (excluded.Contains(candidate) || !tried.Add(candidate))
                {
                    continue;
                }

                if (!checkConflicts)
                {
                    return UIntToHost(candidate);
                }

                bool occupied = false;
                for (int probe = 0; probe < 3; probe++)
                {
                    if (IsAddressInUse(candidate, originalValue))
                    {
                        occupied = true;
                        break;
                    }

                    if (probe < 2)
                    {
                        Thread.Sleep(75);
                    }
                }

                if (!occupied)
                {
                    return UIntToHost(candidate);
                }
            }

            throw new InvalidOperationException(
                "MacRando could not find an apparently unused IPv4 address in this subnet. " +
                "Restore the adapter or try a different network.");
        }

        public string CreateRandomMac()
        {
            byte[] bytes = new byte[6];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            // Locally administered (bit 1 set), unicast (bit 0 clear).
            bytes[0] = (byte)((bytes[0] | 0x02) & 0xFE);
            if (bytes[0] == 0x02 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 && bytes[4] == 0 && bytes[5] == 0)
            {
                bytes[5] = 1;
            }

            return FormatMac(BitConverter.ToString(bytes).Replace("-", string.Empty));
        }

        private static bool HasMacOverride(AdapterBackup backup)
        {
            return backup.OriginalMacOverridePresent ||
                !string.IsNullOrWhiteSpace(backup.OriginalMacOverrideValue);
        }

        private static string GetOriginalMacAddress(AdapterBackup backup)
        {
            return !string.IsNullOrWhiteSpace(backup.OriginalMacAddress)
                ? NormalizeMac(backup.OriginalMacAddress)
                : NormalizeMac(backup.OriginalMacRegistryValue);
        }

        public static string NormalizeMac(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();
        }

        public static string FormatMac(string value)
        {
            string normalized = NormalizeMac(value);
            if (normalized.Length == 12)
            {
                return normalized.Substring(0, 2) + "-" + normalized.Substring(2, 2) + "-" +
                    normalized.Substring(4, 2) + "-" + normalized.Substring(6, 2) + "-" +
                    normalized.Substring(8, 2) + "-" + normalized.Substring(10, 2);
            }

            return value;
        }

        private static string ValidateAppliedState(
            NetworkState current,
            NetworkState originalState,
            string newMac,
            string newIp,
            bool changeMac,
            bool changeIp)
        {
            if (changeMac &&
                (!string.Equals(NormalizeMac(current.CurrentMacAddress), NormalizeMac(newMac), StringComparison.OrdinalIgnoreCase) ||
                 !current.MacOverridePresent ||
                 !string.Equals(NormalizeMac(current.MacOverrideValue), NormalizeMac(newMac), StringComparison.OrdinalIgnoreCase)))
            {
                return "The adapter restarted, but the requested MAC address or override was not active.";
            }

            if (changeIp)
            {
                if (current.DhcpEnabled)
                {
                    return "The adapter is still using DHCP instead of the requested static IPv4 configuration.";
                }

                if (!AddressSetEquals(new[] { newIp }, current.IpAddresses ?? new string[0]) ||
                    current.PrefixLength != originalState.PrefixLength)
                {
                    return "The adapter restarted, but the requested local IPv4 address was not active.";
                }

                if (!string.Equals(current.Gateway ?? string.Empty, originalState.Gateway ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return "The requested gateway was not retained.";
                }

                if (!AddressSetEquals(originalState.DnsServers, current.DnsServers))
                {
                    return "The DNS configuration was not retained.";
                }
            }

            return null;
        }

        private static bool AddressSetEquals(IEnumerable<string> expected, IEnumerable<string> actual)
        {
            var expectedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var actualSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (expected != null)
            {
                foreach (string value in expected)
                {
                    string normalized = NormalizeAddress(value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        expectedSet.Add(normalized);
                    }
                }
            }

            if (actual != null)
            {
                foreach (string value in actual)
                {
                    string normalized = NormalizeAddress(value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        actualSet.Add(normalized);
                    }
                }
            }

            return expectedSet.SetEquals(actualSet);
        }

        private static string NormalizeAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            IPAddress address;
            return IPAddress.TryParse(value.Trim(), out address)
                ? address.ToString()
                : value.Trim();
        }

        private async Task RunNonJsonAsync(
            string script,
            IDictionary<string, string> environment,
            int timeoutMilliseconds = PowerShellTimeoutMilliseconds)
        {
            string wrappedScript =
                "$ErrorActionPreference = 'Stop'\n" +
                "$ProgressPreference = 'SilentlyContinue'\n" +
                script;
            PowerShellResult result = await _runner.RunAsync(wrappedScript, environment, timeoutMilliseconds);
            if (result.ExitCode != 0)
            {
                throw PowerShellRunner.CreateCommandException(result);
            }
        }

        private static Dictionary<string, string> AdapterEnvironment(AdapterInfo adapter)
        {
            if (adapter == null || string.IsNullOrWhiteSpace(adapter.InterfaceGuid))
            {
                throw new InvalidOperationException("The selected adapter has no stable interface GUID.");
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "MR_ADAPTER_NAME", adapter.Name ?? string.Empty },
                { "MR_ADAPTER_GUID", adapter.InterfaceGuid ?? string.Empty }
            };
        }

        private static string JoinDns(IEnumerable<string> servers)
        {
            if (servers == null)
            {
                return string.Empty;
            }

            var values = new List<string>();
            foreach (string server in servers)
            {
                if (string.IsNullOrWhiteSpace(server))
                {
                    continue;
                }

                IPAddress address;
                if (IPAddress.TryParse(server.Trim(), out address))
                {
                    values.Add(address.ToString());
                }
            }

            return string.Join("|", values.ToArray());
        }

        private static uint HostToUInt(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        private static IPAddress UIntToHost(uint value)
        {
            return new IPAddress(new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            });
        }

        private static uint NextRandomUInt32()
        {
            byte[] bytes = new byte[4];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        private static void Shuffle<T>(IList<T> values)
        {
            byte[] randomBytes = new byte[4];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                for (int index = values.Count - 1; index > 0; index--)
                {
                    random.GetBytes(randomBytes);
                    uint randomValue = ((uint)randomBytes[0] << 24) | ((uint)randomBytes[1] << 16) |
                        ((uint)randomBytes[2] << 8) | randomBytes[3];
                    int swapIndex = (int)(randomValue % (uint)(index + 1));
                    T value = values[index];
                    values[index] = values[swapIndex];
                    values[swapIndex] = value;
                }
            }
        }

        private static bool IsAddressInUse(uint candidate, uint source)
        {
            byte[] candidateBytes = UIntToHost(candidate).GetAddressBytes();
            byte[] sourceBytes = UIntToHost(source).GetAddressBytes();
            byte[] hardwareAddress = new byte[6];
            uint hardwareLength = (uint)hardwareAddress.Length;
            try
            {
                int result = SendARP(
                    BitConverter.ToInt32(candidateBytes, 0),
                    BitConverter.ToInt32(sourceBytes, 0),
                    hardwareAddress,
                    ref hardwareLength);
                return result == 0 && hardwareLength > 0;
            }
            catch
            {
                // ARP is a best-effort check. The IP command still validates the address.
                return false;
            }
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(
            int destinationAddress,
            int sourceAddress,
            byte[] hardwareAddress,
            ref uint hardwareAddressLength);

        private static HttpClient CreatePublicIpClient()
        {
            // .NET Framework 4.x may otherwise negotiate an obsolete TLS version.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(6);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.ProductName + "/" + AppInfo.Version);
            return client;
        }
    }
}
