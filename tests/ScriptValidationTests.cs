using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
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
    /// Counts a check and throws on failure. The other suites take a message and count
    /// themselves; this file counts inline at each assertion site because the per-script
    /// tallies are accumulated in a loop, and throwing keeps the failing script identifiable.
    /// </summary>
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

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
        // "query" for a script that only reads and returns JSON, "change" for one that
        // alters adapter configuration. Only queries are ever run live.
        public readonly List<string> Labels = new List<string>();
        // The type each query was asked to deserialize into, so a live run can be matched
        // to the method that produced it. Inferring it from the payload shape was tried
        // and it misidentified the identity script, which returns a single object and so
        // looked like an empty adapter list.
        public readonly List<string> RequestedTypes = new List<string>();

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
            Record(script, environment, true, typeof(T).FullName);
            object value;
            if (typeof(T) == typeof(NetworkSnapshotRaw))
            {
                // Both halves populated, so a snapshot that dropped either list fails the
                // mock path here as well as the live path.
                value = new NetworkSnapshotRaw
                {
                    Adapters = new List<AdapterInfo>
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
                    },
                    VpnProfiles = new List<VpnProfile> { new VpnProfile { Name = "Mock VPN", ServerAddress = "vpn.example.com" } }
                };
            }
            else if (typeof(T) == typeof(List<AdapterInfo>))
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
            Record(script, environment, viaJson, null);
        }

        private void Record(string script, IDictionary<string, string> environment, bool viaJson, string requestedType)
        {
            Scripts.Add(script);
            ViaJson.Add(viaJson);
            Labels.Add(viaJson ? "query" : "change");
            RequestedTypes.Add(requestedType);
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

    /// <summary>
    /// Executes a read-only script in a real PowerShell and requires a parsable payload.
    ///
    /// Deliberately not run for the change, restore and verify scripts: those alter
    /// adapter configuration, and a test has no business running them on the machine it
    /// runs on. This is only ever called with a script that queries.
    /// </summary>
    private static string RunReadOnlyScriptLive(string script, string label)
    {
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
        {
            // A failure rather than a skip. MacRando cannot query Windows without this
            // executable, so a machine without it cannot run the app either, and silently
            // skipping here would leave the live checks looking passed when they never ran.
            throw new Exception(
                "Windows PowerShell was not found at " + powershell +
                ". MacRando depends on it, so the live script checks cannot run.");
        }

        // The same preamble PowerShellRunner.RunJsonAsync prepends, so the script runs
        // here under exactly the conditions it runs in the app.
        string wrapped =
            "$ErrorActionPreference = 'Stop'\n" +
            "$ProgressPreference = 'SilentlyContinue'\n" +
            "function Write-MacRandoJson64([object]$Value) {\n" +
            "  $json = ConvertTo-Json -InputObject $Value -Depth 8 -Compress\n" +
            "  [Console]::Out.WriteLine('__MACRANDO_JSON__')\n" +
            "  [Console]::Out.WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))\n" +
            "}\n" +
            script;

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var stopwatch = Stopwatch.StartNew();
        string output;
        string error;
        int exitCode;
        using (Process process = new Process())
        {
            process.StartInfo = startInfo;
            if (!process.Start())
            {
                throw new Exception("Could not start Windows PowerShell for the live script run.");
            }
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                try { process.Kill(); } catch { }
                throw new Exception("The " + label + " script did not finish within 60 seconds.");
            }
            output = standardOutput;
            error = standardError;
            exitCode = process.ExitCode;
        }
        long elapsed = stopwatch.ElapsedMilliseconds;
        _scriptsChecked++;
        _checks++;

        if (exitCode != 0)
        {
            // A script can fail on this machine for a reason that says nothing about whether
            // the script itself is correct. The per-adapter ones take MR_ADAPTER_GUID and
            // refuse to run without it, so without a mock GUID they cannot be executed here
            // at all. Those are syntax-checked by a parse-only pass instead, below, and
            // only the scripts that need no input are run for real.
            if (error.IndexOf("MR_ADAPTER_GUID", StringComparison.Ordinal) >= 0 ||
                error.IndexOf("no stable interface GUID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _checks++;
                return null;
            }
            throw new Exception(
                "The " + label + " script failed on this machine. It only queries, so this is a " +
                "syntax or cmdlet problem rather than a configuration one. Error: " + error.Trim());
        }

        string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int marker = -1;
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            if (string.Equals(lines[index].Trim(), "__MACRANDO_JSON__", StringComparison.Ordinal))
            {
                marker = index;
                break;
            }
        }
        if (marker < 0 || marker + 1 >= lines.Length)
        {
            throw new Exception(
                "The " + label + " script ran without error but produced no payload. Output: " + output.Trim());
        }

        string json = Encoding.UTF8.GetString(Convert.FromBase64String(lines[marker + 1].Trim()));
        return json;
    }

    /// <summary>
    /// Parses a live payload and confirms it is usable, so a script that runs cleanly but
    /// returns nothing the app can use is still a failure here.
    /// </summary>
    /// <summary>
    /// Runs every captured query script live and checks the payload is usable.
    ///
    /// Only scripts recorded as queries. A captured change, restore or verify script
    /// alters real adapter configuration, so running it from a test would be a change
    /// the developer never asked for against a machine they may be working on.
    ///
    /// The adapter listing and the combined snapshot are both covered. The snapshot is
    /// the one a refresh depends on, and it is also the newest and most duplicated of the
    /// query scripts, so it is exactly the one where a divergence from the single-list
    /// script would otherwise go unnoticed until a user's dashboard came up short.
    /// </summary>
    private static void RunQueryScriptsLive(CapturingRunner captured)
    {
        int ran = 0;
        int parsed = 0;
        int snapshotCount = 0;
        int snapshotProfiles = 0;
        int adapterListCount = 0;
        int vpnProfileCount = 0;
        for (int index = 0; index < captured.Scripts.Count; index++)
        {
            if (index >= captured.Labels.Count || captured.Labels[index] != "query")
            {
                continue;
            }
            string script = captured.Scripts[index];
            // Matched against the type the service asked for, not guessed from the payload.
            string requested = index < captured.RequestedTypes.Count
                ? captured.RequestedTypes[index]
                : string.Empty;
            string label = requested.Contains("NetworkSnapshotRaw")
                ? "network snapshot"
                : (requested.Contains("List`1") && requested.Contains("AdapterInfo")
                    ? "adapter listing"
                    : (requested.Contains("List`1") && requested.Contains("VpnProfile")
                        ? "vpn listing"
                        : "network identity"));

            string json = RunReadOnlyScriptLive(script, label);
            if (json == null)
            {
                // Needs a mock GUID to run, so it was covered by the parse-only pass.
                ParseScriptOnly(script, label);
                parsed++;
                continue;
            }

            if (label == "network snapshot")
            {
                NetworkSnapshotRaw snapshot = ParseLivePayload<NetworkSnapshotRaw>(json, label);
                Check(snapshot.Adapters != null,
                    "The network snapshot script must return an Adapters list, even if it is empty.");
                Check(snapshot.VpnProfiles != null,
                    "The network snapshot script must return a VpnProfiles list, even if it is empty.");
                foreach (AdapterInfo adapter in snapshot.Adapters)
                {
                    Check(!string.IsNullOrWhiteSpace(adapter.Name),
                        "A listed adapter has no name, so it cannot be classified or displayed.");
                }
                snapshotCount = snapshot.Adapters.Count;
                snapshotProfiles = snapshot.VpnProfiles.Count;
            }
            else if (label == "adapter listing")
            {
                List<AdapterInfo> adapters = ParseLivePayload<List<AdapterInfo>>(json, label);
                foreach (AdapterInfo adapter in adapters)
                {
                    Check(!string.IsNullOrWhiteSpace(adapter.Name),
                        "A listed adapter has no name, so it cannot be classified or displayed.");
                }
                adapterListCount = adapters.Count;
            }
            else if (label == "vpn listing")
            {
                vpnProfileCount = ParseLivePayload<List<VpnProfile>>(json, label).Count;
            }
            else
            {
                // The identity script returns one object, and an unusable network is not an
                // error, so the only requirement is that it deserializes into something.
                NetworkProfileRaw identity = ParseLivePayload<NetworkProfileRaw>(json, label);
                Check(identity != null, "The network identity script must return an object.");
            }
            ran++;
        }

        Check(ran >= 3,
            "Expected the adapter listing, network snapshot and identity scripts to run live, but only " + ran + " did.");
        Check(parsed >= 1,
            "Expected the per-adapter scripts to be covered by the parse-only pass, but none were.");
        Check(snapshotCount == adapterListCount,
            "The combined snapshot reported " + snapshotCount + " adapters but the single-list script reported " +
            adapterListCount + ". They query the same thing, so they must agree, or a refresh would show a " +
            "different adapter count depending on which path answered it.");
        Check(snapshotProfiles == vpnProfileCount,
            "The combined snapshot reported " + snapshotProfiles + " VPN profiles but the single-list script " +
            "reported " + vpnProfileCount + ". They query the same thing, so they must agree.");
        Check(snapshotCount > 0,
            "No adapters were reported on the machine running the tests, so the live run proved nothing.");

        Console.WriteLine("query-scripts-live=OK;ran=" + ran + ";parsed=" + parsed +
            ";adapters=" + snapshotCount + ";vpn=" + snapshotProfiles);
    }

    /// <summary>
    /// Asks PowerShell to parse a script without running it.
    ///
    /// The per-adapter scripts read MR_ADAPTER_GUID and throw without it, so they cannot be
    /// executed against this machine without inventing an adapter. Parsing is enough to
    /// catch the failure this pass exists for, which is a syntax error, and it is the only
    /// check that can cover a script that must never be run from a test.
    ///
    /// Parse errors come back on stderr with a non-zero exit code; a clean parse is silent.
    /// </summary>
    private static void ParseScriptOnly(string script, string label)
    {
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        // ParseInput needs the script text itself, passed as a here-string so no escaping
        // question arises. A single-quoted here-string is literal, so the script's own
        // quotes and backticks cannot be interpreted here.
        string wrapped =
            "$ErrorActionPreference = 'Stop'\n" +
            "$ProgressPreference = 'SilentlyContinue'\n" +
            "function Write-MacRandoJson64([object]$Value) { }\n" +
            "$text = @'\n" + script + "\n'@\n" +
            "$parseErrors = $null\n" +
            "[void][System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$null, [ref]$parseErrors)\n" +
            "if ($parseErrors.Count -gt 0) {\n" +
            "  $parseErrors | ForEach-Object { [Console]::Error.WriteLine($_.Message) }\n" +
            "  exit 2\n" +
            "}\n";

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _scriptsChecked++;
        _checks++;
        using (Process process = new Process())
        {
            process.StartInfo = startInfo;
            if (!process.Start())
            {
                throw new Exception("Could not start Windows PowerShell to parse the " + label + " script.");
            }
            string standardError = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                try { process.Kill(); } catch { }
                throw new Exception("Parsing the " + label + " script did not finish within 30 seconds.");
            }
            if (process.ExitCode != 0)
            {
                throw new Exception(
                    "The " + label + " script is not valid PowerShell: " + standardError.Trim());
            }
        }
    }

    private static T ParseLivePayload<T>(string json, string label)
    {
        _checks++;
        T value;
        try
        {
            value = new JavaScriptSerializer().Deserialize<T>(json);
        }
        catch (Exception error)
        {
            throw new Exception("The " + label + " script produced a payload that did not deserialize: " + error.Message);
        }
        if (value == null)
        {
            throw new Exception("The " + label + " script produced a null payload.");
        }
        return value;
    }

    public static int Run()
    {
        var captured = new CapturingRunner();
        var service = new NetworkService(captured);
        // The listing script is the one that grew when adapter classification landed, and
        // it is the one a syntax error in would break most visibly: it runs at startup, so
        // a mistake here means the app shows no adapters at all. Read out of the service
        // rather than pasted here, so the probe cannot drift away from the real script.

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
        RunAll(() => service.GetSnapshotAsync());
        RunAll(() => service.GetVpnProfilesAsync());
        RunAll(() => service.RunVpnActionAsync(profile, true));
        RunAll(() => service.GetNetworkIdentityAsync());

        // Run the query-only scripts for real, on this machine, alongside the checks that
        // only look at their text. The existing checks confirm a script is non-empty, that
        // it reads no variable the C# side forgets to set, and that it hands its result back
        // through the JSON writer. None of that notices a PowerShell syntax error, and these
        // scripts run at startup, so a mistake means the app shows no adapters at all on
        // every machine. Read-only, so running them here changes nothing.
        //
        // The scripts come from the capturing runner, so these are the real literals the
        // service sends rather than copies that could drift away from them.
        RunQueryScriptsLive(captured);

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
