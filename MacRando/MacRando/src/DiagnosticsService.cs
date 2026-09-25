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
