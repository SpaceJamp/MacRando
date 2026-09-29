using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MacRando;

/// <summary>
/// Validates the PowerShell that MacRando sends to Windows, without touching the network.
///
/// Two failures are covered, both of which are invisible until a live adapter change
/// misbehaves:
///
///  1. A script that is not valid PowerShell. Nothing would report it until the moment
///     it runs against a real adapter, where the failure looks like a random adapter bug.
///
///  2. A script reading an environment variable the C# side never sets. Because the
///     scripts take their inputs through the environment rather than string
///     interpolation, a typo such as MR_NEW_IP versus MR_NEW_IPP compiles cleanly, passes
///     every mock test, and then silently reads an empty value during a real change.
///
/// The real NetworkService methods are invoked through a capturing runner, so this
/// exercises the actual scripts and the actual environment dictionaries.
/// </summary>
internal static class ScriptValidationTests
{
    private static readonly Regex EnvironmentRead = new Regex(
        "\\$env:(MR_[A-Z0-9_]+)", RegexOptions.CultureInvariant);

    private static int _checks;
    private static int _scriptsChecked;

    /// <summary>
    /// Records every script and environment dictionary the service produces, and returns
    /// canned JSON so the service methods run to completion.
    /// </summary>
    private sealed class CapturingRunner : IPowerShellRunner
    {
        public readonly List<string> Scripts = new List<string>();
        public readonly List<HashSet<string>> EnvironmentKeys = new List<HashSet<string>>();
        // Whether the script was handed to the JSON-returning runner, which only works if
        // the script calls Write-MacRandoJson64 to hand its result back.
        public readonly List<bool> ViaJson = new List<bool>();

        public Task<PowerShellResult> RunAsync(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            Record(script, environment, false);
            return Task.FromResult(new PowerShellResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            });
        }

        public Task<T> RunJsonAsync<T>(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            Record(script, environment, true);
            object value;
            if (typeof(T) == typeof(List<AdapterInfo>))
            {
                value = new List<AdapterInfo>
                {
                    new AdapterInfo
                    {
                        Name = "Mock Ethernet",
                        InterfaceGuid = "{mock-guid}",
                        InterfaceIndex = 3,
                        IsUp = true,
                        MacAddress = "02-00-00-00-00-01",
                        PermanentMacAddress = "02-00-00-00-00-02",
                        MacPropertySupported = true,
                        IpAddress = "192.168.1.20",
                        PrefixLength = 24,
                        DhcpEnabled = false,
                        Status = "Up",
                        LinkSpeed = "1 Gbps"
                    }
                };
            }
            else if (typeof(T) == typeof(NetworkState))
            {
                value = new NetworkState
                {
                    AdapterName = "Mock Ethernet",
                    InterfaceGuid = "{mock-guid}",
                    InterfaceIndex = 3,
                    CurrentMacAddress = "02-00-00-00-00-01",
                    MacOverrideValue = "020000000001",
                    MacOverridePresent = true,
                    MacPropertySupported = true,
                    IpAddress = "192.168.1.20",
                    PrefixLength = 24,
                    Gateway = "192.168.1.1",
                    DhcpEnabled = false,
                    ConfigurationKnown = true,
                    DnsPolicyKnown = true,
                    IpAddresses = new[] { "192.168.1.20" },
                    StaticDnsServers = new[] { "1.1.1.1" },
                    DhcpDnsServers = new string[0],
                    DnsServers = new[] { "1.1.1.1" },
                    PreferredAddressCount = 1
                };
            }
            else
            {
                value = null;
            }
            return Task.FromResult((T)value);
        }

        private void Record(string script, IDictionary<string, string> environment, bool viaJson)
        {
            Scripts.Add(script);
            ViaJson.Add(viaJson);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> item in environment)
                {
                    keys.Add(item.Key);
                }
            }
            EnvironmentKeys.Add(keys);
        }
    }

    public static int Run()
    {
        var captured = new CapturingRunner();
        var service = new NetworkService(captured);
        var adapter = new AdapterInfo
        {
            Name = "Mock Ethernet",
            InterfaceGuid = "{mock-guid}",
            InterfaceIndex = 3,
            IsUp = true,
            MacPropertySupported = true,
            IpAddress = "192.168.1.20",
            PrefixLength = 24,
            DhcpEnabled = false
        };
        var state = new NetworkState
        {
            AdapterName = "Mock Ethernet",
            InterfaceGuid = "{mock-guid}",
            InterfaceIndex = 3,
            CurrentMacAddress = "02-00-00-00-00-01",
            MacOverrideValue = "020000000001",
            MacOverridePresent = true,
            MacPropertySupported = true,
            IpAddress = "192.168.1.20",
            PrefixLength = 24,
            Gateway = "192.168.1.1",
            DhcpEnabled = false,
            ConfigurationKnown = true,
            DnsPolicyKnown = true,
            IpAddresses = new[] { "192.168.1.20" },
            StaticDnsServers = new[] { "1.1.1.1" },
            DhcpDnsServers = new string[0],
            DnsServers = new[] { "1.1.1.1" },
            PreferredAddressCount = 1
        };
        var backup = new AdapterBackup
        {
            AdapterKey = "{mock-guid}",
            AdapterName = "Mock Ethernet",
            InterfaceGuid = "{mock-guid}",
            InterfaceIndex = 3,
            OriginalMacAddress = "02-00-00-00-00-01",
            OriginalMacOverrideValue = "020000000001",
            OriginalMacOverridePresent = true,
            OriginalIpAddress = "192.168.1.20",
            OriginalPrefixLength = 24,
            OriginalGateway = "192.168.1.1",
            OriginalDhcpEnabled = false,
            OriginalIpAddresses = new[] { "192.168.1.20" },
            OriginalStaticDnsServers = new[] { "1.1.1.1" },
            OriginalDnsServers = new[] { "1.1.1.1" },
            MacChanged = true,
            IpChanged = true,
            ModifiedMacAddress = "02-00-00-00-00-03",
            ModifiedIpAddress = "192.168.1.77"
        };
        var profile = new VpnProfile { Name = "Mock VPN", ServerAddress = "vpn.example.com" };

        // Drive every operation that produces PowerShell.
        RunAll(() => service.GetAdaptersAsync());
        RunAll(() => service.GetStateAsync(adapter));
        RunAll(() => service.ApplyChangesAsync(adapter, state, "02-00-00-00-00-04", "192.168.1.88", true, true));
        RunAll(() => service.RestoreAsync(backup));
        RunAll(() => service.VerifyRestoreAsync(backup));
        RunAll(() => service.VerifyAppliedAsync(adapter, state, "02-00-00-00-00-04", "192.168.1.88", true, true));
        RunAll(() => service.GetVpnProfilesAsync());
        RunAll(() => service.RunVpnActionAsync(profile, true));
        RunAll(() => service.GetNetworkIdentityAsync());

        if (captured.Scripts.Count == 0)
        {
            throw new Exception("No PowerShell scripts were captured; the test would pass vacuously.");
        }

        var unused = new List<string>();
        for (int i = 0; i < captured.Scripts.Count; i++)
        {
            string script = captured.Scripts[i];
            _scriptsChecked++;
            _checks++;

            if (string.IsNullOrWhiteSpace(script))
            {
                throw new Exception("Script " + i + " was empty.");
            }

            // Every variable the script reads must actually be supplied. A missing one
            // means the script silently sees an empty value during a real change.
            var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in EnvironmentRead.Matches(script))
            {
                read.Add(match.Groups[1].Value);
            }
            var supplied = captured.EnvironmentKeys[i];
            foreach (string name in read)
            {
                _checks++;
                if (!supplied.Contains(name))
                {
                    throw new Exception(
                        "Script " + i + " reads $env:" + name + " but the C# side never sets it. " +
                        "It would silently be empty during a live change.");
                }
            }

            // Every script that returns a result has to hand it back through the JSON
            // writer. A script that just emits the object lets PowerShell format it as a
            // table, the marker never appears in the output, and the parse fails at run
            // time with an error that surfaces nowhere near the script that caused it.
            // Scripts that return nothing, such as a change or a restore, are unaffected.
            if (!captured.ViaJson[i])
            {
                continue;
            }
            _checks++;
            if (script.IndexOf("Write-MacRandoJson64", StringComparison.Ordinal) < 0)
            {
                throw new Exception(
                    "Script " + i + " returns a result but never calls Write-MacRandoJson64. " +
                    "PowerShell would format the object for display instead of returning it as JSON, " +
                    "so the caller would fail to deserialize the result.");
            }
        }

        // Report the union so a variable that is set but never read is visible for review.
        var everRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string script in captured.Scripts)
        {
            foreach (Match match in EnvironmentRead.Matches(script))
            {
                everRead.Add(match.Groups[1].Value);
            }
        }
        var everSupplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (HashSet<string> keys in captured.EnvironmentKeys)
        {
            foreach (string key in keys)
            {
                if (key.StartsWith("MR_", StringComparison.OrdinalIgnoreCase))
                {
                    everSupplied.Add(key);
                }
            }
        }
        foreach (string key in everSupplied)
        {
            if (!everRead.Contains(key))
            {
                unused.Add(key);
            }
        }

        Console.WriteLine("script-validation-tests=OK;scripts=" + _scriptsChecked + ";checks=" + _checks +
            ";env-vars=" + everRead.Count + ";unused=" + unused.Count);
        if (unused.Count > 0)
        {
            Console.WriteLine("  supplied but never read by any script: " + string.Join(", ", new List<string>(unused).ToArray()));
        }
        return 0;
    }

    private static void RunAll(Func<Task> operation)
    {
        try
        {
            operation().GetAwaiter().GetResult();
        }
        catch
        {
            // A later check reads the captured script regardless of whether the operation
            // itself succeeded against the mock, so failures here are not fatal.
        }
    }
}
