using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading.Tasks;

namespace MacRando
{
    internal sealed class DiagnosticReport
    {
        public string GeneratedAtUtc { get; set; }
        public string Version { get; set; }
        public bool IsAdministrator { get; set; }
        public string SelectedAdapter { get; set; }
        public string AdapterGuid { get; set; }
        public string CurrentMac { get; set; }
        public string PermanentMac { get; set; }
        public string MacProperty { get; set; }
        public string IPv4 { get; set; }
        public string PrefixLength { get; set; }
        public string DhcpMode { get; set; }
        public string LinkSpeed { get; set; }
        public string VpnProfileCount { get; set; }
        public List<string> Checks { get; set; }
        public List<string> Warnings { get; set; }

        public string ToDisplayText()
        {
            var lines = new List<string>();
            lines.Add("MacRando read-only diagnostics");
            lines.Add("Generated (UTC): " + GeneratedAtUtc);
            lines.Add("Version: " + Version);
            lines.Add("Administrator: " + (IsAdministrator ? "Yes" : "No"));
            lines.Add("");
            lines.Add("Selected adapter: " + (string.IsNullOrWhiteSpace(SelectedAdapter) ? "None" : SelectedAdapter));
            lines.Add("Interface GUID: " + (string.IsNullOrWhiteSpace(AdapterGuid) ? "Unavailable" : AdapterGuid));
            lines.Add("Current MAC: " + CurrentMac);
            lines.Add("Permanent MAC: " + PermanentMac);
            lines.Add("NetworkAddress property: " + MacProperty);
            lines.Add("IPv4: " + IPv4 + "/" + PrefixLength);
            lines.Add("DHCP: " + DhcpMode);
            lines.Add("Link: " + LinkSpeed);
            lines.Add("VPN profiles: " + VpnProfileCount);
            lines.Add("");
            lines.Add("Checks:");
            foreach (string check in Checks ?? new List<string>())
            {
                lines.Add("- " + check);
            }
            if (Warnings != null && Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("Warnings:");
                foreach (string warning in Warnings)
                {
                    lines.Add("- " + warning);
                }
            }
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }

    internal sealed class IpPreflightReport
    {
        public string GeneratedAtUtc { get; set; }
        public string Version { get; set; }
        public string AdapterName { get; set; }
        public string AdapterGuid { get; set; }
        public string CurrentIp { get; set; }
        public string PrefixLength { get; set; }
        public string DhcpMode { get; set; }
        public string ProposedIp { get; set; }
        public string ProposedPrefixLength { get; set; }
        public string GatewayPlan { get; set; }
        public string DhcpPlan { get; set; }
        public string DnsPlan { get; set; }
        public List<string> Checks { get; set; }
        public List<string> Warnings { get; set; }
        public List<PreflightChangeItem> Changes { get; set; }

        public string ToDisplayText()
        {
            var lines = new List<string>();
            lines.Add("MacRando read-only IP preflight");
            lines.Add("Generated (UTC): " + GeneratedAtUtc);
            lines.Add("Version: " + Version);
            lines.Add("");
            lines.Add("Adapter: " + (string.IsNullOrWhiteSpace(AdapterName) ? "None" : AdapterName));
            lines.Add("Interface GUID: " + (string.IsNullOrWhiteSpace(AdapterGuid) ? "Unavailable" : AdapterGuid));
            lines.Add("Current IPv4: " + CurrentIp + "/" + PrefixLength);
            lines.Add("Current DHCP: " + DhcpMode);
            lines.Add("Proposed IPv4: " + ProposedIp + "/" + ProposedPrefixLength);
            lines.Add("DHCP plan: " + DhcpPlan);
            lines.Add("Gateway plan: " + GatewayPlan);
            lines.Add("DNS plan: " + DnsPlan);
            if (Changes != null && Changes.Count > 0)
            {
                lines.Add("");
                lines.Add("Planned changes:");
                foreach (PreflightChangeItem item in Changes)
                {
                    lines.Add(item.ToDisplayLine());
                }
            }
            lines.Add("");
            lines.Add("Checks:");
            foreach (string check in Checks ?? new List<string>())
            {
                lines.Add("- " + check);
            }
            if (Warnings != null && Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("Warnings:");
                foreach (string warning in Warnings)
                {
                    lines.Add("- " + warning);
                }
            }
            lines.Add("");
            lines.Add("No adapter settings were changed by this preflight.");
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }

    internal sealed class DiagnosticsService
    {
        private readonly NetworkService _network;

        public DiagnosticsService(NetworkService network)
        {
            if (network == null)
            {
                throw new ArgumentNullException("network");
            }
            _network = network;
        }

        public async Task<IpPreflightReport> RunIpPreflightAsync(AdapterInfo selectedAdapter)
        {
            var report = new IpPreflightReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                Version = AppInfo.DisplayVersion,
                AdapterName = selectedAdapter == null ? string.Empty : selectedAdapter.Name,
                AdapterGuid = selectedAdapter == null ? string.Empty : selectedAdapter.InterfaceGuid,
                CurrentIp = "Unavailable",
                PrefixLength = string.Empty,
                DhcpMode = "Unavailable",
                ProposedIp = "Unavailable",
                ProposedPrefixLength = string.Empty,
                GatewayPlan = "Unavailable",
                DhcpPlan = "Unavailable",
                DnsPlan = "Unavailable",
                Checks = new List<string>(),
                Warnings = new List<string>(),
                Changes = new List<PreflightChangeItem>()
            };
            if (selectedAdapter == null)
            {
                report.Warnings.Add("Select a physical adapter before running IP preflight.");
                report.Checks.Add("No adapter was selected; no network state was read.");
                return report;
            }

            NetworkState capturedState = null;
            try
            {
                NetworkState state = await _network.GetStateAsync(selectedAdapter);
                capturedState = state;
                report.CurrentIp = AppLogger.MaskIp(state.IpAddress);
                report.PrefixLength = state.PrefixLength.ToString();
                report.DhcpMode = state.DhcpEnabled ? "Enabled" : "Disabled";
                report.ProposedPrefixLength = state.PrefixLength.ToString();
                report.DhcpPlan = state.DhcpEnabled
                    ? "Disable DHCP for the temporary static address; restore re-enables DHCP."
                    : "Keep static mode; restore returns the saved static configuration.";
                report.GatewayPlan = string.IsNullOrWhiteSpace(state.Gateway)
                    ? "No default gateway was detected; no gateway will be added."
                    : "Preserve the existing default route via " + AppLogger.MaskIp(state.Gateway) + ".";
                if (state.DnsServers == null || state.DnsServers.Length == 0)
                {
                    report.DnsPlan = "No DNS servers were detected; the live operation would reset DNS to automatic.";
                }
                else
                {
                    var dns = new List<string>();
                    foreach (string server in state.DnsServers)
                    {
                        dns.Add(AppLogger.MaskIp(server));
                    }
                    report.DnsPlan = "Reapply the current DNS configuration: " + string.Join(", ", dns.ToArray()) + ".";
                }
                report.Checks.Add("Adapter state was read successfully.");
                report.Checks.Add("The current IPv4 prefix is " + state.PrefixLength + ".");
                if (!state.HasUsableIpv4)
                {
                    report.Warnings.Add("The adapter does not have a usable IPv4 address for local randomization.");
                }
                if (state.PrefixLength < 1 || state.PrefixLength > 30)
                {
                    report.Warnings.Add("The current IPv4 prefix is outside the supported range.");
                }
                if (state.DhcpEnabled)
                {
                    report.Warnings.Add("DHCP consent is required before a live IP change; this preflight does not grant it.");
                }
                if (!state.ConfigurationKnown || !state.DnsPolicyKnown)
                {
                    report.Warnings.Add("The complete gateway/DNS configuration could not be read.");
                }

                try
                {
                    var candidate = await Task.Run(() => _network.FindRandomLocalAddress(state, false));
                    report.ProposedIp = AppLogger.MaskIp(candidate.ToString());
                    report.Checks.Add("A candidate address was selected without sending ARP probes.");
                    report.Warnings.Add("The live change will still perform best-effort ARP conflict checks before applying the address.");
                }
                catch (Exception error)
                {
                    report.Warnings.Add("No candidate address could be selected: " + AppLogger.Sanitize(error.Message));
                }
            }
            catch (Exception error)
            {
                report.Warnings.Add("Adapter state could not be read: " + AppLogger.Sanitize(error.Message));
            }

            report.Checks.Add("No Set-NetIPAddress, New-NetIPAddress, route, DHCP, or adapter restart command was run.");
            BuildChangeList(report, capturedState);
            return report;
        }

        private static void BuildChangeList(IpPreflightReport report, NetworkState state)
        {
            if (report.Changes == null)
            {
                report.Changes = new List<PreflightChangeItem>();
            }
            if (state == null)
            {
                report.Changes.Add(new PreflightChangeItem
                {
                    Setting = "Adapter settings",
                    Current = "Unavailable",
                    Planned = "Unavailable",
                    Changes = false,
                    Note = "state could not be read"
                });
                return;
            }

            report.Changes.Add(new PreflightChangeItem
            {
                Setting = "IPv4 address",
                Current = AppLogger.MaskIp(state.IpAddress),
                Planned = report.ProposedIp,
                Changes = true,
                Note = "temporary static address; restore returns the saved value"
            });
            report.Changes.Add(new PreflightChangeItem
            {
                Setting = "IPv4 prefix",
                Current = state.PrefixLength.ToString(),
                Planned = state.PrefixLength.ToString(),
                Changes = false
            });
            report.Changes.Add(new PreflightChangeItem
            {
                Setting = "DHCP",
                Current = state.DhcpEnabled ? "Enabled" : "Disabled",
                Planned = "Disabled temporarily",
                Changes = state.DhcpEnabled,
                Note = state.DhcpEnabled ? "restore re-enables DHCP" : "static mode is preserved"
            });
            report.Changes.Add(new PreflightChangeItem
            {
                Setting = "Default gateway",
                Current = string.IsNullOrWhiteSpace(state.Gateway) ? "(none)" : AppLogger.MaskIp(state.Gateway),
                Planned = string.IsNullOrWhiteSpace(state.Gateway) ? "(none)" : AppLogger.MaskIp(state.Gateway),
                Changes = false,
                Note = "existing default route is preserved"
            });
            report.Changes.Add(new PreflightChangeItem
            {
                Setting = "DNS servers",
                Current = FormatDns(state.DnsServers),
                Planned = FormatDns(state.DnsServers),
                Changes = false,
                Note = state.DnsServers == null || state.DnsServers.Length == 0
                    ? "no DNS detected; live operation would reset to automatic"
                    : "reapplied after the address change"
            });
            report.Changes.Add(new PreflightChangeItem
            {
                Setting = "Default routes",
                Current = "Existing routes",
                Planned = "Unchanged",
                Changes = false,
                Note = "no route command is ever run"
            });
            report.Changes.Add(new PreflightChangeItem
            {
                Setting = "MAC address",
                Current = AppLogger.MaskMac(state.CurrentMacAddress),
                Planned = AppLogger.MaskMac(state.CurrentMacAddress),
                Changes = false,
                Note = "an IP-only change does not touch the MAC"
            });
        }

        private static string FormatDns(string[] servers)
        {
            if (servers == null || servers.Length == 0)
            {
                return "(automatic)";
            }
            var parts = new List<string>();
            foreach (string server in servers)
            {
                parts.Add(AppLogger.MaskIp(server));
            }
            return string.Join(", ", parts.ToArray());
        }

        public async Task<DiagnosticReport> RunAsync(AdapterInfo selectedAdapter)
        {
            var report = new DiagnosticReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                Version = AppInfo.DisplayVersion,
                IsAdministrator = IsAdministrator(),
                SelectedAdapter = selectedAdapter == null ? string.Empty : selectedAdapter.Name,
                AdapterGuid = selectedAdapter == null ? string.Empty : selectedAdapter.InterfaceGuid,
                CurrentMac = "Unavailable",
                PermanentMac = "Unavailable",
                MacProperty = "Unavailable",
                IPv4 = "Unavailable",
                PrefixLength = string.Empty,
                DhcpMode = "Unavailable",
                LinkSpeed = "Unavailable",
                VpnProfileCount = "Unavailable",
                Checks = new List<string>(),
                Warnings = new List<string>()
            };

            try
            {
                List<AdapterInfo> adapters = await _network.GetAdaptersAsync();
                report.Checks.Add("Adapter discovery succeeded (" + (adapters == null ? 0 : adapters.Count) + " physical adapters).");
            }
            catch (Exception error)
            {
                report.Warnings.Add("Adapter discovery failed: " + AppLogger.Sanitize(error.Message));
            }

            if (selectedAdapter != null)
            {
                try
                {
                    NetworkState state = await _network.GetStateAsync(selectedAdapter);
                    report.CurrentMac = AppLogger.MaskMac(state.CurrentMacAddress);
                    report.PermanentMac = AppLogger.MaskMac(state.MacRegistryValue);
                    report.MacProperty = state.MacPropertySupported ? "Available" : "Not exposed";
                    report.IPv4 = AppLogger.MaskIp(state.IpAddress);
                    report.PrefixLength = state.PrefixLength.ToString();
                    report.DhcpMode = state.DhcpEnabled ? "Enabled" : "Disabled";
                    report.LinkSpeed = selectedAdapter.LinkSpeed ?? "Unavailable";
                    report.Checks.Add("Selected adapter state was read successfully.");
                    if (!state.MacPropertySupported)
                    {
                        report.Warnings.Add("The selected adapter does not expose the NetworkAddress advanced property.");
                    }
                }
                catch (Exception error)
                {
                    report.Warnings.Add("Selected adapter state could not be read: " + AppLogger.Sanitize(error.Message));
                }
            }
            else
            {
                report.Checks.Add("No adapter was selected; only discovery diagnostics were run.");
            }

            try
            {
                List<VpnProfile> profiles = await _network.GetVpnProfilesAsync();
                report.VpnProfileCount = (profiles == null ? 0 : profiles.Count).ToString();
                report.Checks.Add("VPN profile enumeration succeeded.");
            }
            catch (Exception error)
            {
                report.Warnings.Add("VPN profile enumeration failed: " + AppLogger.Sanitize(error.Message));
            }

            report.Checks.Add("No network-changing command was run by diagnostics.");
            return report;
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
