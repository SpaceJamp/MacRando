using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MacRando
{
    /// <summary>
    /// The two shapes a Global Device ID takes on Windows. Both are device identifiers that
    /// correlate activity across sessions, and neither is a switch: the value is a cache of
    /// what the platform fetched, not the thing that fetches it.
    /// </summary>
    internal static class GdidKind
    {
        public const string None = "none";
        public const string LocalLid = "local-lid";
        public const string GlobalPrefixed = "global-prefixed";
        public const string Unrecognized = "unrecognized";

        /// <summary>
        /// The plain form is 16 hex characters. The prefixed form is the same payload behind
        /// a "g:" marker, which is how the platform labels a genuine global device ID as
        /// opposed to a locally derived one.
        /// </summary>
        public static string Classify(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return None;
            }
            string trimmed = value.Trim();
            if (trimmed.StartsWith("g:", StringComparison.OrdinalIgnoreCase))
            {
                return IsHex(trimmed.Substring(2)) && trimmed.Length == 18 ? GlobalPrefixed : Unrecognized;
            }
            return IsHex(trimmed) && trimmed.Length == 16 ? LocalLid : Unrecognized;
        }

        public static string Describe(string kind)
        {
            switch (kind)
            {
                case None:
                    return "not present";
                case LocalLid:
                    return "present, local LID form (16 hex characters)";
                case GlobalPrefixed:
                    return "present, global device ID form (\"g:\" followed by 16 hex characters)";
                default:
                    return "present, but not a shape MacRando recognizes";
            }
        }

        private static bool IsHex(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Everything MacRando read, with no interpretation attached. Keeping the raw values
    /// separate is what lets the conclusions be tested without a registry.
    /// </summary>
    internal sealed class DeviceTrackingSnapshot
    {
        public string Lid { get; set; }
        public string GlobalDeviceId { get; set; }
        public int IdentityEntryCount { get; set; }
        public bool ImmersiveTokenPresent { get; set; }
        public int NegativeCacheEntryCount { get; set; }
        public int ConnectedDevicesEntryCount { get; set; }
        public int? AllowTelemetryPolicy { get; set; }
        public int? AllowTelemetryCurrentVersion { get; set; }
        public int? CeipEnable { get; set; }
        public int? AdvertisingEnabled { get; set; }
        public bool AdvertisingIdPresent { get; set; }
        public string MachineGuid { get; set; }
        public string EditionId { get; set; }
        public string ProductName { get; set; }
        public string DisplayVersion { get; set; }
        public int? Build { get; set; }
        public List<string> Unavailable { get; set; }
    }

    internal sealed class DeviceTrackingReport
    {
        public string GeneratedAtUtc { get; set; }
        public string Version { get; set; }
        public string Headline { get; set; }
        public string LidShape { get; set; }
        public string GlobalDeviceIdShape { get; set; }
        public string LidDisplay { get; set; }
        public string GlobalDeviceIdDisplay { get; set; }
        public string TelemetryEffective { get; set; }
        public bool TelemetryFullyOff { get; set; }
        public List<string> Checks { get; set; }
        public List<string> Warnings { get; set; }

        public string ToDisplayText()
        {
            var lines = new List<string>();
            lines.Add("MacRando device tracking report (read-only)");
            lines.Add("Generated (UTC): " + GeneratedAtUtc);
            lines.Add("Version: " + Version);
            lines.Add("");
            lines.Add(Headline);
            lines.Add("");
            lines.Add("Global Device ID: " + LidDisplay);
            lines.Add("Global device ID (g: form): " + GlobalDeviceIdDisplay);
            lines.Add("");
            lines.Add("Diagnostic data level: " + TelemetryEffective);
            lines.Add("");
            lines.Add("Findings:");
            foreach (string check in Checks ?? new List<string>())
            {
                lines.Add("- " + check);
            }
            if (Warnings != null && Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("Worth knowing:");
                foreach (string warning in Warnings)
                {
                    lines.Add("- " + warning);
                }
            }
            if (Unavailable != null && Unavailable.Count > 0)
            {
                lines.Add("");
                lines.Add("Could not be read:");
                foreach (string item in Unavailable)
                {
                    lines.Add("- " + item);
                }
            }
            lines.Add("");
            lines.Add("No registry value, file, or setting was changed by this report.");
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        public List<string> Unavailable { get; set; }
    }

    internal sealed class DeviceTrackingService
    {
        /// <summary>
        /// Reads only. Every read is independent so that one denied key does not cost the
        /// whole report, and the 64-bit view is requested explicitly so the answer does not
        /// depend on whether the process happened to start as 32-bit.
        /// </summary>
        public DeviceTrackingSnapshot Read()
        {
            var snapshot = new DeviceTrackingSnapshot { Unavailable = new List<string>() };
            try
            {
                using (RegistryKey key = OpenCurrentUser(@"SOFTWARE\Microsoft\IdentityCRL\ExtendedProperties"))
                {
                    snapshot.Lid = GetString(key, "LID");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Local device ID: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenCurrentUser(@"Software\Microsoft\Windows\CurrentVersion\IrisService\IrisActionCreatives"))
                {
                    snapshot.GlobalDeviceId = GetString(key, "GLOBALDEVICEID");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Global device ID cache: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenCurrentUser(@"Software\Microsoft\IdentityCRL\UserExtendedProperties"))
                {
                    snapshot.IdentityEntryCount = key == null ? 0 : key.GetSubKeyNames().Length;
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Signed-in identity count: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenCurrentUser(@"Software\Microsoft\IdentityCRL\Immersive\production\Token"))
                {
                    snapshot.ImmersiveTokenPresent = key != null;
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Connected-devices token cache: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenLocalMachine(@"SOFTWARE\Microsoft\IdentityCRL\NegativeCache"))
                {
                    snapshot.NegativeCacheEntryCount = CountEntries(key);
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Identity negative cache: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                string connected = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConnectedDevicesPlatform");
                snapshot.ConnectedDevicesEntryCount = Directory.Exists(connected)
                    ? Directory.GetFileSystemEntries(connected).Length
                    : 0;
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Connected-devices platform data: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenLocalMachine(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                {
                    snapshot.AllowTelemetryPolicy = GetInt(key, "AllowTelemetry");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Diagnostic data policy: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenLocalMachine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection"))
                {
                    snapshot.AllowTelemetryCurrentVersion = GetInt(key, "AllowTelemetry");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Diagnostic data user policy: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenLocalMachine(@"SOFTWARE\Microsoft\SQMClient\Windows"))
                {
                    snapshot.CeipEnable = GetInt(key, "CEIPEnable");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Customer experience program setting: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenCurrentUser(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo"))
                {
                    snapshot.AdvertisingEnabled = GetInt(key, "Enabled");
                    // The key also holds the Enabled flag itself, so the presence of any
                    // value says nothing. The identifier has to be looked for by name.
                    snapshot.AdvertisingIdPresent = HasValueNamed(key, "AdvertisingId");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Advertising ID: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenLocalMachine(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    snapshot.MachineGuid = GetString(key, "MachineGuid");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Machine GUID: " + AppLogger.Sanitize(error.Message));
            }

            try
            {
                using (RegistryKey key = OpenLocalMachine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    snapshot.EditionId = GetString(key, "EditionID");
                    snapshot.ProductName = GetString(key, "ProductName");
                    snapshot.DisplayVersion = GetString(key, "DisplayVersion");
                    snapshot.Build = GetInt(key, "CurrentBuild");
                }
            }
            catch (Exception error)
            {
                snapshot.Unavailable.Add("Windows edition: " + AppLogger.Sanitize(error.Message));
            }

            return snapshot;
        }

        /// <summary>
        /// Turns raw values into conclusions. Pure, so the wording and the edition rules can
        /// be tested against a machine that does not exist.
        /// </summary>
        public DeviceTrackingReport Analyze(DeviceTrackingSnapshot snapshot)
        {
            snapshot = snapshot ?? new DeviceTrackingSnapshot();
            var checks = new List<string>();
            var warnings = new List<string>();
            string lidKind = GdidKind.Classify(snapshot.Lid);
            string globalKind = GdidKind.Classify(snapshot.GlobalDeviceId);

            bool hasGdid = lidKind != GdidKind.None || globalKind != GdidKind.None;

            // The value is only ever a cache of something the platform fetched. Saying so
            // first is the point of the report: it stops the value being mistaken for a
            // switch, and stops it being deleted for no benefit.
            warnings.Add("A Global Device ID here is a local cache of an identifier the platform already holds. " +
                "Deleting the value does not withdraw it, and it can be written back on the next " +
                "connection to Microsoft. The effective levers are the diagnostic data level and whether a " +
                "Microsoft account is signed in, both listed below.");
            if (hasGdid)
            {
                warnings.Add("This report does not change anything, and nothing here should be edited by hand. " +
                    "Clearing the identifier does not reduce what has already been collected, and a value " +
                    "written back by the platform would make a manual edit look successful while doing nothing.");
            }

            if (lidKind == GdidKind.None)
            {
                checks.Add("No local Global Device ID was found. The value this path is documented to use is absent.");
            }
            else if (lidKind == GdidKind.Unrecognized)
            {
                warnings.Add("The local device ID value is not the documented 16-character hex shape, " +
                    "so MacRando cannot tell what it is. It was left untouched and is shown masked.");
            }
            else
            {
                checks.Add("A Global Device ID is present, in the local LID form. " +
                    "This is the identifier described in reporting about a device being traced across a VPN.");
            }

            if (globalKind == GdidKind.None)
            {
                // Deliberately no inference about the Microsoft account here. This cache is
                // populated by its own service, so its absence says nothing about whether an
                // account is signed in, and the identity count below is what actually
                // answers that.
                checks.Add("The separate \"g:\" global device ID cache is not provisioned on this machine. " +
                    "Its absence is not evidence that the machine is untracked; the local LID above is still present.");
            }
            else
            {
                checks.Add("A Global Device ID is present in the \"g:\" form, which is the variant the platform " +
                    "publishes as a global device identifier.");
            }

            if (snapshot.IdentityEntryCount > 0)
            {
                checks.Add("There " + (snapshot.IdentityEntryCount == 1 ? "is 1 Microsoft account identity entry" :
                    "are " + snapshot.IdentityEntryCount + " Microsoft account identity entries") +
                    " on this account. A signed-in Microsoft account is what keeps the device " +
                    "registration path able to re-provision an identifier, so it is the most effective thing " +
                    "to change. Removing it also removes OneDrive, the Store, and Office activation.");
            }
            else
            {
                checks.Add("No Microsoft account identity entries were found for this account, so the device " +
                    "registration path has no account to re-provision an identifier against.");
            }

            if (snapshot.ImmersiveTokenPresent)
            {
                checks.Add("A connected-devices platform token cache exists. This is part of the same " +
                    "device-registration machinery.");
            }
            if (snapshot.NegativeCacheEntryCount > 0)
            {
                checks.Add("The identity negative cache holds " + snapshot.NegativeCacheEntryCount +
                    " top-level entry(ies).");
            }
            if (snapshot.ConnectedDevicesEntryCount > 0)
            {
                checks.Add("The connected-devices platform folder holds " + snapshot.ConnectedDevicesEntryCount +
                    " top-level item(s).");
            }

            string effectiveTelemetry;
            bool fullyOff;
            InterpretTelemetry(snapshot, out effectiveTelemetry, out fullyOff);

            if (snapshot.AllowTelemetryPolicy.HasValue)
            {
                checks.Add("A diagnostic data policy is set to " + snapshot.AllowTelemetryPolicy.Value +
                    " and takes precedence over any value set through the Settings interface.");
            }
            else if (snapshot.AllowTelemetryCurrentVersion.HasValue)
            {
                checks.Add("Diagnostic data is set to " + snapshot.AllowTelemetryCurrentVersion.Value +
                    " through the Settings interface, with no policy overriding it.");
            }
            else
            {
                checks.Add("No diagnostic data setting is configured, so whatever the Windows default is applies.");
            }
            checks.Add("On this edition the effective diagnostic data level is: " + effectiveTelemetry);
            if (snapshot.CeipEnable.HasValue)
            {
                checks.Add("The customer experience program is " + (snapshot.CeipEnable.Value == 0 ? "not enabled" : "enabled") + ".");
            }
            if (snapshot.AdvertisingEnabled.HasValue)
            {
                checks.Add("The per-user advertising ID is " + (snapshot.AdvertisingEnabled.Value == 0 ? "disabled" : "enabled") +
                    ", and an advertising ID value is " + (snapshot.AdvertisingIdPresent ? "stored" : "not stored") + ".");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.MachineGuid))
            {
                // Flagged deliberately. It is the one value people reach for, and it is the
                // worst one to change: it is a machine identity, not a telemetry handle, and
                // Windows uses it for activation and SID creation.
                warnings.Add("This machine also has a Machine GUID (" + Mask(snapshot.MachineGuid) + "). It is a " +
                    "machine identity rather than a tracking handle, and is worth leaving alone: Windows uses it " +
                    "for activation, user SID creation, and DPAPI, and regenerating it is a documented way to " +
                    "break activation. It would also not meaningfully reduce collection, because the " +
                    "registration path carries other identifiers.");
            }

            string headline = hasGdid
                ? "A Global Device ID is stored on this machine."
                : "No Global Device ID was found in the locations MacRando checks.";

            return new DeviceTrackingReport
            {
                GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                Version = AppInfo.DisplayVersion,
                Headline = headline,
                LidShape = lidKind,
                GlobalDeviceIdShape = globalKind,
                LidDisplay = GdidKind.Describe(lidKind) +
                    (lidKind == GdidKind.None ? "" : " (" + Mask(snapshot.Lid) + ")"),
                GlobalDeviceIdDisplay = GdidKind.Describe(globalKind) +
                    (globalKind == GdidKind.None ? "" : " (" + Mask(snapshot.GlobalDeviceId) + ")"),
                TelemetryEffective = effectiveTelemetry,
                TelemetryFullyOff = fullyOff,
                Checks = checks,
                Warnings = warnings,
                Unavailable = snapshot.Unavailable
            };
        }

        /// <summary>
        /// Diagnostic data level 0 means "off" only on Enterprise, Education, and Server.
        /// Everywhere else Windows treats it as 1, so required data still leaves the machine.
        ///
        /// The editions not listed are reported as still sending the required floor. That is
        /// deliberately the pessimistic reading: overstating how much a setting achieves
        /// would be the worse error for a report whose purpose is to be believed.
        /// </summary>
        internal static void InterpretTelemetry(DeviceTrackingSnapshot snapshot, out string description, out bool fullyOff)
        {
            int? policy = snapshot.AllowTelemetryPolicy.HasValue
                ? snapshot.AllowTelemetryPolicy
                : snapshot.AllowTelemetryCurrentVersion;
            string edition = snapshot.EditionId ?? string.Empty;
            bool enterpriseFamily =
                edition.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0 ||
                edition.IndexOf("Education", StringComparison.OrdinalIgnoreCase) >= 0 ||
                edition.StartsWith("Server", StringComparison.OrdinalIgnoreCase) ||
                edition.StartsWith("Datacenter", StringComparison.OrdinalIgnoreCase);

            if (!policy.HasValue)
            {
                description = "not configured, so the Windows default applies";
                fullyOff = false;
                return;
            }
            switch (policy.Value)
            {
                case 0:
                    if (enterpriseFamily)
                    {
                        description = "0, diagnostic data off. This edition honours it, so optional and " +
                            "required diagnostic data are both suppressed.";
                        fullyOff = true;
                    }
                    else
                    {
                        description = "0, which this edition (" + (edition.Length == 0 ? "unknown" : edition) +
                            ") treats as 1. Required diagnostic data is still sent: hardware inventory, " +
                            "crash reports, and update status.";
                        fullyOff = false;
                    }
                    return;
                case 1:
                    description = "1, required diagnostic data only. Hardware inventory, crash reports, and " +
                        "update status are sent.";
                    fullyOff = false;
                    return;
                case 2:
                    description = "2, enhanced diagnostic data.";
                    fullyOff = false;
                    return;
                case 3:
                    description = "3, optional diagnostic data. This is the broadest setting and sends the most.";
                    fullyOff = false;
                    return;
                default:
                    description = policy.Value + ", which is not a documented diagnostic data level.";
                    fullyOff = false;
                    return;
            }
        }

        /// <summary>
        /// Identifiers are masked on principle. They are no more useful in clear text for
        /// diagnosis, and a report is meant to be attachable to a bug report.
        /// </summary>
        internal static string Mask(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(none)";
            }
            string trimmed = value.Trim();
            if (trimmed.Length <= 6)
            {
                return new string('*', trimmed.Length);
            }
            return trimmed.Substring(0, 3) + new string('*', trimmed.Length - 6) +
                trimmed.Substring(trimmed.Length - 3) + " (length " + trimmed.Length + ")";
        }

        private static RegistryKey OpenCurrentUser(string path)
        {
            return Registry.CurrentUser.OpenSubKey(path, false);
        }

        private static RegistryKey OpenLocalMachine(string path)
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                return root.OpenSubKey(path, false);
            }
        }

        private static int CountEntries(RegistryKey key)
        {
            if (key == null)
            {
                return 0;
            }
            return key.GetSubKeyNames().Length + key.GetValueNames().Length;
        }

        private static bool HasValueNamed(RegistryKey key, string name)
        {
            if (key == null)
            {
                return false;
            }
            foreach (string existing in key.GetValueNames())
            {
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetString(RegistryKey key, string name)
        {
            if (key == null)
            {
                return null;
            }
            object value = key.GetValue(name, null);
            return value == null ? null : value.ToString();
        }

        private static int? GetInt(RegistryKey key, string name)
        {
            if (key == null)
            {
                return null;
            }
            object value = key.GetValue(name, null);
            if (value == null)
            {
                return null;
            }
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }
    }
}
