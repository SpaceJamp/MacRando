using System;
using System.Collections.Generic;

namespace MacRando
{
    /// <summary>
    /// Identifies the network the machine is currently attached to, so a preset can be bound
    /// to a specific network and applied only when that network appears.
    /// </summary>
    internal sealed class NetworkIdentity
    {
        public string ConnectionProfile { get; set; }
        public string Gateway { get; set; }
        public string Ssid { get; set; }
        public string Key { get; set; }
        public string Description { get; set; }

        public bool IsUsable
        {
            get { return !string.IsNullOrWhiteSpace(Key); }
        }

        /// <summary>
        /// Builds a stable key from what identifies the network. A Wi-Fi SSID is preferred
        /// because it names the network, otherwise the connection profile name is used. The
        /// gateway is always included so two different networks that share a name, such as
        /// any two guest networks called "Free WiFi", are not treated as the same place.
        /// </summary>
        public static string BuildKey(string connectionProfile, string gateway, string ssid)
        {
            string name = !string.IsNullOrWhiteSpace(ssid) ? ssid : connectionProfile;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(gateway))
            {
                return string.Empty;
            }
            string prefix = !string.IsNullOrWhiteSpace(ssid) ? "wifi:" : "net:";
            return (prefix + Clean(name) + "|" + Clean(gateway)).ToLowerInvariant();
        }

        public static string BuildDescription(string connectionProfile, string gateway, string ssid)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ssid))
            {
                parts.Add("Wi-Fi " + ssid);
            }
            else if (!string.IsNullOrWhiteSpace(connectionProfile))
            {
                parts.Add(connectionProfile);
            }
            if (!string.IsNullOrWhiteSpace(gateway))
            {
                parts.Add("gateway " + gateway);
            }
            return parts.Count == 0 ? "unknown network" : string.Join(", ", parts.ToArray());
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim().Replace("|", "/");
        }
    }

    /// <summary>
    /// Decides whether a network-bound preset should be applied automatically.
    ///
    /// Kept as a pure function with no UI or network access so the rules can be tested
    /// exhaustively. The ordering matters: an unusable state is refused before anything
    /// else, because a restore profile that cannot be read is not a safety net.
    /// </summary>
    internal static class NetworkAutoApply
    {
        public const int DefaultCooldownMinutes = 10;

        public static AdapterPreset Select(
            AppState state,
            string currentNetworkKey,
            int pendingRestoreCount,
            bool restoreStateUnreadable,
            DateTime nowUtc,
            IDictionary<string, DateTime> lastAppliedUtc,
            int cooldownMinutes,
            out string reason)
        {
            if (state == null)
            {
                reason = "no loaded state";
                return null;
            }
            if (restoreStateUnreadable)
            {
                reason = "the restore data is unreadable, so no adapter will be changed";
                return null;
            }
            if (pendingRestoreCount > 0)
            {
                reason = "a restore profile is still pending, so a new change would compound it";
                return null;
            }
            if (string.IsNullOrWhiteSpace(currentNetworkKey))
            {
                reason = "the current network could not be identified";
                return null;
            }
            if (state.Presets == null)
            {
                reason = "no presets are saved";
                return null;
            }

            // Every bound preset is considered, rather than stopping at the first one that
            // declines: the cooldown belongs to a single preset and must not silence the
            // others. Two presets that are both eligible for the same network is a
            // configuration conflict, and is refused rather than resolved by the arbitrary
            // order of the dictionary.
            AdapterPreset eligible = null;
            string eligibleId = null;
            string refused = null;
            int eligibleCount = 0;

            foreach (KeyValuePair<string, AdapterPreset> item in state.Presets)
            {
                AdapterPreset preset = item.Value;
                if (preset == null)
                {
                    continue;
                }
                if (!preset.BindToNetwork ||
                    !string.Equals(Normalize(preset.NetworkKey), Normalize(currentNetworkKey), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(preset.Name) ? item.Key : preset.Name;
                if (!preset.AutoApplyOnNetworkChange)
                {
                    refused = "the preset bound to this network has automatic apply turned off";
                    continue;
                }
                if (!preset.RandomizeMac && !preset.RandomizeIp)
                {
                    refused = "the preset bound to this network has no enabled network action";
                    continue;
                }
                if (IsCoolingDown(preset, item.Key, nowUtc, lastAppliedUtc, cooldownMinutes))
                {
                    refused = "the preset bound to this network was applied recently and is still cooling down";
                    continue;
                }

                eligibleCount++;
                if (eligibleCount == 1)
                {
                    eligible = preset;
                    eligibleId = item.Key;
                }
            }

            if (eligibleCount > 1)
            {
                reason = "more than one preset is bound to this network with automatic apply enabled, " +
                    "so MacRando will not guess which one you meant";
                return null;
            }
            if (eligible != null)
            {
                reason = "bound preset " + (string.IsNullOrWhiteSpace(eligible.Name) ? eligibleId : eligible.Name) +
                    " matches the current network";
                return eligible;
            }
            reason = refused ?? "no preset is bound to this network";
            return null;
        }

        private static bool IsCoolingDown(
            AdapterPreset preset,
            string storageKey,
            DateTime nowUtc,
            IDictionary<string, DateTime> lastAppliedUtc,
            int cooldownMinutes)
        {
            if (lastAppliedUtc == null)
            {
                return false;
            }
            string presetId = !string.IsNullOrWhiteSpace(preset.PresetKey) ? preset.PresetKey : storageKey;
            DateTime previous;
            if (!lastAppliedUtc.TryGetValue(presetId, out previous))
            {
                return false;
            }
            double elapsed = (nowUtc - previous).TotalMinutes;
            // A negative elapsed time means the clock moved backwards, which is not evidence
            // that the preset ran a moment ago, so it must not lock the preset out.
            return elapsed >= 0 && elapsed < cooldownMinutes;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
