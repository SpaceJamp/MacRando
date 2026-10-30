using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MacRando
{
    /// <summary>
    /// Looks up the manufacturer behind a MAC address from the bundled IEEE OUI list.
    ///
    /// Read-only and offline: the database is embedded in the executable, so the lookup
    /// works on an air-gapped machine and needs no consent. The point is to tell the user
    /// what hardware they are looking at, and whether a randomized address looks like a
    /// real device or an obvious synthetic one.
    /// </summary>
    internal static class OuiLookup
    {
        private static readonly object Sync = new object();
        private static Dictionary<string, string> _vendors;
        private static bool _loaded;

        /// <summary>
        /// Returns the vendor name for the given MAC address, or "Unknown" when the
        /// prefix is not in the database or the address is malformed. Never throws and
        /// never returns null.
        /// </summary>
        /// <summary>
        /// Returns the vendor name for the given MAC address, or "Unknown" when the
        /// prefix is not in the database or the address is malformed. Never throws and
        /// never returns null.
        ///
        /// The optional database lets callers (and tests) supply their own data without
        /// depending on the embedded resource.
        /// </summary>
        public static string Lookup(string mac, Dictionary<string, string> database = null)
        {
            Dictionary<string, string> vendors = database ?? EnsureLoaded();
            string prefix = NormalizePrefix(mac);
            if (prefix == null)
            {
                return "Unknown";
            }
            string vendor;
            if (vendors.TryGetValue(prefix, out vendor) && !string.IsNullOrWhiteSpace(vendor))
            {
                return vendor;
            }
            return "Unknown";
        }

        /// <summary>
        /// Parses an OUI database in the bundled format: one entry per line, six uppercase
        /// hex digits, a tab, then the vendor name. Exposed so the parsing can be tested
        /// without depending on the embedded resource.
        /// </summary>
        internal static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }
            foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length < 8 || line[0] == '#')
                {
                    continue;
                }
                int tab = line.IndexOf('\t');
                if (tab < 6)
                {
                    tab = line.IndexOf(' ');
                }
                if (tab < 6)
                {
                    continue;
                }
                string prefix = line.Substring(0, 6).ToUpperInvariant();
                if (!IsHex(prefix))
                {
                    continue;
                }
                string vendor = line.Substring(tab + 1).Trim();
                if (string.IsNullOrWhiteSpace(vendor))
                {
                    continue;
                }
                // The first entry wins, matching the IEEE registry order.
                if (!result.ContainsKey(prefix))
                {
                    result[prefix] = vendor;
                }
            }
            return result;
        }

        private static Dictionary<string, string> EnsureLoaded()
        {
            if (_loaded)
            {
                return _vendors;
            }
            lock (Sync)
            {
                if (_loaded)
                {
                    return _vendors;
                }
                _vendors = Load();
                _loaded = true;
                return _vendors;
            }
        }

        private static Dictionary<string, string> Load()
        {
            try
            {
                Assembly assembly = typeof(OuiLookup).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream("MacRando.OuiDatabase.txt"))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            return Parse(reader.ReadToEnd());
                        }
                    }
                }
            }
            catch
            {
                // A missing or unreadable database must never break the application.
            }

            // Fall back to a file next to the executable, which is how a developer runs
            // against a freshly edited database without rebuilding the resource.
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OuiDatabase.txt");
                if (File.Exists(path))
                {
                    return Parse(File.ReadAllText(path));
                }
            }
            catch
            {
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizePrefix(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
            {
                return null;
            }
            string hex = mac.Replace(":", "").Replace("-", "").Replace(" ", "").Replace(".", "");
            if (hex.Length < 6)
            {
                return null;
            }
            hex = hex.Substring(0, 6).ToUpperInvariant();
            return IsHex(hex) ? hex : null;
        }

        private static bool IsHex(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            foreach (char c in value)
            {
                bool isDigit = c >= '0' && c <= '9';
                bool isUpper = c >= 'A' && c <= 'F';
                bool isLower = c >= 'a' && c <= 'f';
                if (!isDigit && !isUpper && !isLower)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
