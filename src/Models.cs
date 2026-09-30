using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace MacRando
{
    internal sealed class AdapterInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int InterfaceIndex { get; set; }
        public string MacAddress { get; set; }
        public string PermanentMacAddress { get; set; }
        public bool MacPropertySupported { get; set; }
        public string LinkSpeed { get; set; }
        public string InterfaceGuid { get; set; }
        public string Status { get; set; }
        public bool IsUp { get; set; }
        public string IpAddress { get; set; }
        public int PrefixLength { get; set; }
        public bool DhcpEnabled { get; set; }

        public string Key
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(InterfaceGuid))
                {
                    return InterfaceGuid;
                }

                return Description ?? Name;
            }
        }

        public override string ToString()
        {
            string suffix = string.IsNullOrWhiteSpace(Description) ? string.Empty : " - " + Description;
            string state = string.IsNullOrWhiteSpace(Status) ? (IsUp ? "Up" : "Unknown") : Status;
            return (Name ?? "Unknown adapter") + " [" + state + "]" + suffix;
        }
    }

    internal sealed class VpnProfile
    {
        public string Name { get; set; }
        public string ServerAddress { get; set; }
        public string TunnelType { get; set; }
        public bool SplitTunneling { get; set; }

        public override string ToString()
        {
            string server = string.IsNullOrWhiteSpace(ServerAddress) ? string.Empty : " (" + ServerAddress + ")";
            return (Name ?? "Unnamed profile") + server;
        }
    }

    internal sealed class NetworkState
    {
        public string AdapterName { get; set; }
        public string InterfaceGuid { get; set; }
        public int InterfaceIndex { get; set; }
        public string CurrentMacAddress { get; set; }
        public string MacRegistryValue { get; set; }
        public string MacOverrideValue { get; set; }
        public bool MacOverridePresent { get; set; }
        public string MacDisplayValue { get; set; }
        public bool MacPropertySupported { get; set; }
        public string IpAddress { get; set; }
        public int PrefixLength { get; set; }
        public string Gateway { get; set; }
        public bool DhcpEnabled { get; set; }
        public bool ConfigurationKnown { get; set; }
        public bool DnsPolicyKnown { get; set; }
        public string[] IpAddresses { get; set; }
        public string[] StaticDnsServers { get; set; }
        public string[] DhcpDnsServers { get; set; }
        public string[] DnsServers { get; set; }
        public int PreferredAddressCount { get; set; }

        public bool HasUsableIpv4
        {
            get
            {
                if (string.IsNullOrWhiteSpace(IpAddress) || PrefixLength < 1 || PrefixLength > 30)
                {
                    return false;
                }

                IPAddress address;
                if (!IPAddress.TryParse(IpAddress, out address) || address.AddressFamily != AddressFamily.InterNetwork)
                {
                    return false;
                }

                byte[] bytes = address.GetAddressBytes();
                return bytes[0] != 0 &&
                    bytes[0] != 127 &&
                    bytes[0] < 224 &&
                    !(bytes[0] == 169 && bytes[1] == 254);
            }
        }
    }

    internal sealed class AdapterBackup
    {
        public string AdapterKey { get; set; }
        public string AdapterName { get; set; }
        public string AdapterDescription { get; set; }
        public string InterfaceGuid { get; set; }
        public int InterfaceIndex { get; set; }
        public string OriginalMacAddress { get; set; }
        public string OriginalMacRegistryValue { get; set; }
        public string OriginalMacOverrideValue { get; set; }
        public bool OriginalMacOverridePresent { get; set; }
        public string OriginalMacDisplayValue { get; set; }
        public string OriginalIpAddress { get; set; }
        public int OriginalPrefixLength { get; set; }
        public string OriginalGateway { get; set; }
        public bool OriginalDhcpEnabled { get; set; }
        public string[] OriginalIpAddresses { get; set; }
        public string[] OriginalStaticDnsServers { get; set; }
        public string[] OriginalDnsServers { get; set; }
        public string ModifiedMacAddress { get; set; }
        public string ModifiedIpAddress { get; set; }
        public bool MacChanged { get; set; }
        public bool IpChanged { get; set; }
        public DateTime SavedAtUtc { get; set; }
        public DateTime LastOperationUtc { get; set; }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrWhiteSpace(AdapterName)
                    ? "Unknown adapter"
                    : AdapterName + " - " + (AdapterDescription ?? "adapter");
            }
        }
    }

    internal static class AppInfo
    {
        public const string ProductName = "MacRando";
        public const string Version = "1.12.0";
        public const string BuildLabel = "2026.09";
        public static string DisplayVersion { get { return Version + " (" + BuildLabel + ")"; } }
    }

    internal sealed class AdapterPreset
    {
        public string PresetKey { get; set; }
        public string Name { get; set; }
        public bool RandomizeMac { get; set; }
        public bool RandomizeIp { get; set; }
        public bool RestoreOnExit { get; set; }
        public bool AllowDhcpIpRandomization { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        // Network binding. Absent in presets written before 1.5.0, so every consumer has
        // to treat a missing NetworkKey as "not bound".
        public bool BindToNetwork { get; set; }
        public string NetworkKey { get; set; }
        public string NetworkDescription { get; set; }
        public bool AutoApplyOnNetworkChange { get; set; }

        public bool IsNetworkBound
        {
            get
            {
                return BindToNetwork && !string.IsNullOrWhiteSpace(NetworkKey);
            }
        }

        /// <summary>
        /// The adapter this preset belongs to, taken from the preset key rather than stored
        /// separately. Preset keys are built as adapterKey + "::" + name, and the adapter key
        /// is an interface GUID, so the separator is unambiguous.
        /// </summary>
        public string AdapterKey
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PresetKey))
                {
                    return string.Empty;
                }
                int separator = PresetKey.IndexOf("::", StringComparison.Ordinal);
                return separator <= 0 ? string.Empty : PresetKey.Substring(0, separator);
            }
        }

        public override string ToString()
        {
            string label = string.IsNullOrWhiteSpace(Name) ? "Unnamed preset" : Name;
            return IsNetworkBound ? label + "  [" + (NetworkDescription ?? "bound network") + "]" : label;
        }
    }

    internal static class RetryKinds
    {
        public const string None = "";
        public const string Mac = "Mac";
        public const string Ip = "Ip";
        public const string MacAndIp = "MacAndIp";

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return None;
            }
            string normalized = value.Trim();
            if (string.Equals(normalized, Mac, StringComparison.OrdinalIgnoreCase))
            {
                return Mac;
            }
            if (string.Equals(normalized, Ip, StringComparison.OrdinalIgnoreCase))
            {
                return Ip;
            }
            if (string.Equals(normalized, MacAndIp, StringComparison.OrdinalIgnoreCase))
            {
                return MacAndIp;
            }
            return None;
        }

        public static string FromFlags(bool changeMac, bool changeIp)
        {
            if (changeMac && changeIp)
            {
                return MacAndIp;
            }
            if (changeMac)
            {
                return Mac;
            }
            if (changeIp)
            {
                return Ip;
            }
            return None;
        }

        public static bool IncludesMac(string value)
        {
            string kind = Normalize(value);
            return string.Equals(kind, Mac, StringComparison.Ordinal) ||
                string.Equals(kind, MacAndIp, StringComparison.Ordinal);
        }

        public static bool IncludesIp(string value)
        {
            string kind = Normalize(value);
            return string.Equals(kind, Ip, StringComparison.Ordinal) ||
                string.Equals(kind, MacAndIp, StringComparison.Ordinal);
        }
    }

    internal sealed class PendingOperation
    {
        public string OperationId { get; set; }
        public string AdapterKey { get; set; }
        public string Action { get; set; }
        public bool Automatic { get; set; }
        public DateTime StartedAtUtc { get; set; }

        // Structured retry metadata. Older state files only have Action, so callers
        // must fall back to parsing it when Kind is missing.
        public string Kind { get; set; }
        public bool GenerateRandomMac { get; set; }
        public string RequestedMac { get; set; }
    }

    internal sealed class IpChangeRecord
    {
        public string RecordId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string AdapterKey { get; set; }
        public string AdapterName { get; set; }
        public string OriginalAddress { get; set; }
        public string ProposedAddress { get; set; }
        public string OriginalPrefixLength { get; set; }
        public string OriginalDhcp { get; set; }
        public string OriginalGateway { get; set; }
        public string Outcome { get; set; }
        public bool Automatic { get; set; }
        public string Notes { get; set; }
    }

    internal static class IpChangeOutcomes
    {
        public const string Applied = "Applied";
        public const string Verified = "Verified";
        public const string RolledBack = "RolledBack";
        public const string RestoreFailed = "RestoreFailed";
        public const string Failed = "Failed";

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Failed;
            }
            string normalized = value.Trim();
            if (string.Equals(normalized, Applied, StringComparison.OrdinalIgnoreCase)) return Applied;
            if (string.Equals(normalized, Verified, StringComparison.OrdinalIgnoreCase)) return Verified;
            if (string.Equals(normalized, RolledBack, StringComparison.OrdinalIgnoreCase)) return RolledBack;
            if (string.Equals(normalized, RestoreFailed, StringComparison.OrdinalIgnoreCase)) return RestoreFailed;
            return Failed;
        }
    }

    internal sealed class OperationHistoryEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string AdapterKey { get; set; }
        public string Action { get; set; }
        public bool Automatic { get; set; }
        public bool Success { get; set; }
        public string Result { get; set; }
    }

    internal static class NotificationKinds
    {
        public const string Info = "Info";
        public const string Success = "Success";
        public const string Warning = "Warning";
        public const string Error = "Error";
        public const string Critical = "Critical";

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Info;
            }
            string normalized = value.Trim();
            if (string.Equals(normalized, Success, StringComparison.OrdinalIgnoreCase))
            {
                return Success;
            }
            if (string.Equals(normalized, Warning, StringComparison.OrdinalIgnoreCase))
            {
                return Warning;
            }
            if (string.Equals(normalized, Error, StringComparison.OrdinalIgnoreCase))
            {
                return Error;
            }
            if (string.Equals(normalized, Critical, StringComparison.OrdinalIgnoreCase))
            {
                return Critical;
            }
            return Info;
        }

        public static bool IsCritical(string value)
        {
            return string.Equals(Normalize(value), Critical, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Normalize(value), Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class NotificationHistoryEntry
    {
        public string NotificationId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public string AdapterKey { get; set; }
        public string Action { get; set; }
        public string Details { get; set; }
        public bool CanRestore { get; set; }
        public bool CanRetry { get; set; }
        public bool Acknowledged { get; set; }

        // Structured retry metadata, replacing the need to parse the display Action text.
        // Empty Kind means the entry predates this field and cannot be retried reliably.
        public string RetryKind { get; set; }
        public bool RetryGenerateRandomMac { get; set; }
        public string RetryRequestedMac { get; set; }

        public bool HasStructuredRetry
        {
            get
            {
                return !string.Equals(RetryKinds.Normalize(RetryKind), RetryKinds.None, StringComparison.Ordinal);
            }
        }
    }

    internal sealed class PreflightChangeItem
    {
        public string Setting { get; set; }
        public string Current { get; set; }
        public string Planned { get; set; }
        public bool Changes { get; set; }
        public string Note { get; set; }

        public string ToDisplayLine()
        {
            string marker = Changes ? "CHANGE" : "keep  ";
            string line = "  [" + marker + "] " + (Setting ?? "?") +
                ": " + (string.IsNullOrWhiteSpace(Current) ? "(none)" : Current) +
                "  ->  " + (string.IsNullOrWhiteSpace(Planned) ? "(none)" : Planned);
            if (!string.IsNullOrWhiteSpace(Note))
            {
                line += "  (" + Note + ")";
            }
            return line;
        }
    }

    internal sealed class UpdateManifest
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
        public string SignerThumbprint { get; set; }
        public string ReleaseNotesUrl { get; set; }
        public string PublishedUtc { get; set; }
    }

    internal sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public bool IsVerified { get; set; }
        public bool RequiresConfiguration { get; set; }
        public string StatusMessage { get; set; }
        public string LocalDownloadPath { get; set; }
        public UpdateManifest Manifest { get; set; }
    }

    internal sealed class AppState
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, AdapterBackup> Backups { get; set; }
        public Dictionary<string, AdapterPreset> Presets { get; set; }
        public PendingOperation PendingOperation { get; set; }
        public List<OperationHistoryEntry> History { get; set; }
        public List<NotificationHistoryEntry> Notifications { get; set; }
        public List<IpChangeRecord> IpChangeHistory { get; set; }

        public AppState()
        {
            SchemaVersion = 1;
            Backups = new Dictionary<string, AdapterBackup>(StringComparer.OrdinalIgnoreCase);
            Presets = new Dictionary<string, AdapterPreset>(StringComparer.OrdinalIgnoreCase);
            History = new List<OperationHistoryEntry>();
            Notifications = new List<NotificationHistoryEntry>();
            IpChangeHistory = new List<IpChangeRecord>();
        }
    }
}
