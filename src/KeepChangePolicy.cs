using System;
using System.Collections.Generic;

namespace MacRando
{
    /// <summary>
    /// Deciding whether a change may be kept rather than restored.
    ///
    /// Pure, so the rules are testable. The rules exist because "keep" is the one action in
    /// this application that is not fully reversible by it, and the cases where it would be
    /// a mistake are narrow enough to enumerate.
    ///
    /// Two deliberate limits. Keeping applies to the MAC address only: an IP change left in
    /// place after a reboot tends to break connectivity, so those are always restored. And
    /// a change that is not actually there cannot be kept, because "keep" means "leave it
    /// alone", not "make it so".
    /// </summary>
    internal static class KeepChangePolicy
    {
        public const int MaxKeptRecords = 50;

        public static bool CanKeep(
            AdapterBackup backup,
            out string reason)
        {
            if (backup == null)
            {
                reason = "There is no saved profile for this adapter.";
                return false;
            }
            if (!backup.MacChanged)
            {
                // Refused rather than silently doing nothing, because the button being
                // present and inert is more confusing than being told why.
                reason = "This change did not alter the MAC address, so there is nothing to keep. " +
                    "Address changes are always restored on exit.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(backup.ModifiedMacAddress))
            {
                reason = "The profile does not record which MAC address was applied, so it cannot be kept safely.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(backup.OriginalMacAddress))
            {
                // The record exists precisely so the original is not lost. Without it there
                // is no way to record what was kept, so keeping would be a one-way door.
                reason = "The profile does not record the original MAC address, so keeping this change " +
                    "would leave no way to get back to it.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Records the kept address, replacing any earlier record for the same adapter so
        /// the list stays one entry per adapter rather than growing with every change.
        /// </summary>
        public static KeptMacRecord Record(AppState state, AdapterBackup backup, string keptMac, DateTime nowUtc)
        {
            if (state == null)
            {
                return null;
            }
            if (state.KeptMacs == null)
            {
                state.KeptMacs = new List<KeptMacRecord>();
            }

            KeptMacRecord existing = Find(state, backup.AdapterKey);
            KeptMacRecord record = existing ?? new KeptMacRecord { AdapterKey = backup.AdapterKey };
            record.AdapterName = backup.AdapterName;
            record.InterfaceGuid = backup.InterfaceGuid;
            record.OriginalMacAddress = backup.OriginalMacAddress;
            record.KeptMacAddress = string.IsNullOrWhiteSpace(keptMac) ? backup.ModifiedMacAddress : keptMac;
            record.KeptAtUtc = nowUtc;
            if (existing == null)
            {
                state.KeptMacs.Add(record);
            }
            Trim(state);
            return record;
        }

        public static KeptMacRecord Find(AppState state, string adapterKey)
        {
            if (state == null || state.KeptMacs == null || string.IsNullOrWhiteSpace(adapterKey))
            {
                return null;
            }
            foreach (KeptMacRecord record in state.KeptMacs)
            {
                if (record != null && string.Equals(record.AdapterKey, adapterKey, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }
            }
            return null;
        }

        /// <summary>
        /// Caps the list. A record is a convenience for the user, not a log to keep
        /// forever, and an unbounded list in the state file is a slow problem.
        /// </summary>
        public static void Trim(AppState state)
        {
            if (state == null || state.KeptMacs == null)
            {
                return;
            }
            // Drop anything null, which a hand-edited or partially written file could hold.
            for (int index = state.KeptMacs.Count - 1; index >= 0; index--)
            {
                if (state.KeptMacs[index] == null)
                {
                    state.KeptMacs.RemoveAt(index);
                }
            }
            while (state.KeptMacs.Count > MaxKeptRecords)
            {
                state.KeptMacs.RemoveAt(0);
            }
        }

        public static string Describe(KeptMacRecord record)
        {
            if (record == null)
            {
                return string.Empty;
            }
            string name = string.IsNullOrWhiteSpace(record.AdapterName) ? "adapter" : record.AdapterName;
            return name + ": kept " + (record.KeptMacAddress ?? "(unknown)") +
                ", original " + (record.OriginalMacAddress ?? "(unknown)") +
                " (" + record.KeptAtUtc.ToString("u") + ")";
        }
    }
}
