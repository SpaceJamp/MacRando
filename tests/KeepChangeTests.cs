using System;
using System.Collections.Generic;
using KeepChangePolicy = MacRando.KeepChangePolicy;
using KeptMacRecord = MacRando.KeptMacRecord;

/// <summary>
/// Coverage for keeping a changed MAC address.
///
/// Keeping is the one action MacRando cannot undo, so the refusals matter more than the
/// happy path. The cases below are the ones where pressing the button would be a mistake:
/// nothing to keep, a profile that cannot say what was applied, and a profile that cannot
/// say what the original was, which would make keeping a one-way door with no record.
/// </summary>
internal static class KeepChangeTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            CanKeepAMacChange();
            RefusesWhenNothingChanged();
            RefusesAnAddressOnlyChange();
            RefusesWhenTheAppliedAddressIsUnknown();
            RefusesWhenTheOriginalIsUnknown();
            RefusesWhenThereIsNoProfile();
            RecordsTheOriginalBeforeDiscarding();
            OneRecordPerAdapter();
            TheListIsCapped();
            NullEntriesAreDropped();
            DescribeIsUseful();
            Console.WriteLine("keep-change-tests=OK;checks=" + _checks);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static MacRando.AdapterBackup Backup(bool macChanged, bool ipChanged, string original, string modified)
    {
        return new MacRando.AdapterBackup
        {
            AdapterKey = "{121EC763-A5F6-4EF7-AD11-554AD8464295}",
            AdapterName = "Ethernet",
            InterfaceGuid = "{121EC763-A5F6-4EF7-AD11-554AD8464295}",
            MacChanged = macChanged,
            IpChanged = ipChanged,
            OriginalMacAddress = original,
            ModifiedMacAddress = modified
        };
    }

    private static void CanKeepAMacChange()
    {
        string reason;
        Check(KeepChangePolicy.CanKeep(Backup(true, false, "A4-1B-C0-11-22-33", "02-00-00-00-00-04"), out reason),
            "a MAC change with both addresses known should be keepable, got: " + reason);
        Check(string.IsNullOrEmpty(reason), "an allowed keep should not report a refusal reason");

        // A MAC and IP change together is still keepable: keeping the MAC half is
        // meaningful, and the IP half is restored separately.
        Check(KeepChangePolicy.CanKeep(Backup(true, true, "A4-1B-C0-11-22-33", "02-00-00-00-00-04"), out reason),
            "a combined change should still allow keeping the MAC half");
    }

    private static void RefusesWhenNothingChanged()
    {
        string reason;
        Check(!KeepChangePolicy.CanKeep(Backup(false, false, "A4-1B-C0-11-22-33", null), out reason),
            "a profile with no changes must not be keepable");
        Check(reason.IndexOf("nothing to keep", StringComparison.OrdinalIgnoreCase) >= 0,
            "the refusal should say there is nothing to keep, got: " + reason);
    }

    private static void RefusesAnAddressOnlyChange()
    {
        // The deliberate limit: an IP change left in place after a reboot tends to break
        // connectivity, so those are always restored and never kept.
        string reason;
        Check(!KeepChangePolicy.CanKeep(Backup(false, true, "A4-1B-C0-11-22-33", null), out reason),
            "an IP-only change must not be keepable");
        Check(reason.IndexOf("always restored", StringComparison.OrdinalIgnoreCase) >= 0,
            "the refusal should say address changes are always restored, got: " + reason);
    }

    private static void RefusesWhenTheAppliedAddressIsUnknown()
    {
        string reason;
        Check(!KeepChangePolicy.CanKeep(Backup(true, false, "A4-1B-C0-11-22-33", null), out reason),
            "a profile that does not record what was applied must not be keepable");
        Check(reason.IndexOf("which MAC address was applied", StringComparison.OrdinalIgnoreCase) >= 0,
            "the refusal should name the missing field, got: " + reason);
    }

    private static void RefusesWhenTheOriginalIsUnknown()
    {
        // The important one. If the original is not recorded there is nowhere to put it,
        // so keeping would discard the only route back.
        string reason;
        Check(!KeepChangePolicy.CanKeep(Backup(true, false, null, "02-00-00-00-00-04"), out reason),
            "a profile with no original address must not be keepable");
        Check(reason.IndexOf("no way to get back", StringComparison.OrdinalIgnoreCase) >= 0,
            "the refusal should explain the original is unrecoverable, got: " + reason);
    }

    private static void RefusesWhenThereIsNoProfile()
    {
        string reason;
        Check(!KeepChangePolicy.CanKeep(null, out reason), "there must be no profile to keep");
        Check(reason.Length > 0, "a missing profile should still explain itself");
    }

    private static void RecordsTheOriginalBeforeDiscarding()
    {
        var state = new MacRando.AppState();
        MacRando.AdapterBackup backup = Backup(true, false, "A4-1B-C0-11-22-33", "02-00-00-00-00-04");
        var when = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        KeptMacRecord record = KeepChangePolicy.Record(state, backup, "02-00-00-00-00-04", when);
        Check(record != null, "keeping should produce a record");
        Check(record.OriginalMacAddress == "A4-1B-C0-11-22-33",
            "the record must hold the original, or keeping loses the way back");
        Check(record.KeptMacAddress == "02-00-00-00-00-04", "the record must hold what was kept");
        Check(record.AdapterKey == backup.AdapterKey, "the record must identify the adapter");
        Check(state.KeptMacs.Count == 1, "the record must be stored in the state");
        Check(KeepChangePolicy.Find(state, backup.AdapterKey) != null, "the record must be findable by adapter");
    }

    private static void OneRecordPerAdapter()
    {
        var state = new MacRando.AppState();
        MacRando.AdapterBackup backup = Backup(true, false, "A4-1B-C0-11-22-33", "02-00-00-00-00-04");
        var first = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        KeepChangePolicy.Record(state, backup, "02-00-00-00-00-04", first);
        backup.ModifiedMacAddress = "02-00-00-00-00-05";
        KeepChangePolicy.Record(state, backup, "02-00-00-00-00-05", first.AddHours(1));

        Check(state.KeptMacs.Count == 1, "keeping twice for one adapter must not grow the list");
        Check(state.KeptMacs[0].KeptMacAddress == "02-00-00-00-00-05",
            "the newer kept address should replace the older one");
        Check(state.KeptMacs[0].KeptAtUtc == first.AddHours(1), "the timestamp should be updated too");
        // The original must survive a second keep, which is the whole point of the record.
        Check(state.KeptMacs[0].OriginalMacAddress == "A4-1B-C0-11-22-33",
            "the original must remain recorded after a second keep");
    }

    private static void TheListIsCapped()
    {
        var state = new MacRando.AppState();
        var when = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        for (int index = 0; index < KeepChangePolicy.MaxKeptRecords + 20; index++)
        {
            MacRando.AdapterBackup backup = Backup(true, false, "A4-1B-C0-11-22-33", "02-00-00-00-00-04");
            backup.AdapterKey = "{guid-" + index + "}";
            KeepChangePolicy.Record(state, backup, "02-00-00-00-00-04", when.AddMinutes(index));
        }
        Check(state.KeptMacs.Count == KeepChangePolicy.MaxKeptRecords,
            "the kept list must be capped, found " + state.KeptMacs.Count);

        // The oldest are dropped, not the newest: a record of a long-kept address is the
        // one most likely to still be wanted.
        Check(KeepChangePolicy.Find(state, "{guid-0}") == null, "the oldest record should have been dropped");
        Check(KeepChangePolicy.Find(state, "{guid-" + (KeepChangePolicy.MaxKeptRecords + 19) + "}") != null,
            "the newest record must be kept");
    }

    private static void NullEntriesAreDropped()
    {
        // A hand-edited or partially written state file could hold a null, and a null in
        // the list would break the find used by the UI.
        var state = new MacRando.AppState();
        state.KeptMacs.Add(null);
        MacRando.AdapterBackup backup = Backup(true, false, "A4-1B-C0-11-22-33", "02-00-00-00-00-04");
        state.KeptMacs.Add(new KeptMacRecord { AdapterKey = backup.AdapterKey });
        state.KeptMacs.Add(null);

        KeepChangePolicy.Trim(state);
        Check(state.KeptMacs.Count == 1, "null entries must be dropped");
        Check(KeepChangePolicy.Find(state, backup.AdapterKey) != null, "the real entry must survive");
        Check(KeepChangePolicy.Find(state, "{nope}") == null, "an absent adapter must not be found");
        Check(KeepChangePolicy.Find(null, "x") == null, "a null state must not be searched");
    }

    private static void DescribeIsUseful()
    {
        var record = new KeptMacRecord
        {
            AdapterName = "Ethernet",
            OriginalMacAddress = "A4-1B-C0-11-22-33",
            KeptMacAddress = "02-00-00-00-00-04",
            KeptAtUtc = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)
        };
        string text = KeepChangePolicy.Describe(record);
        Check(text.Contains("02-00-00-00-00-04"), "the description should name what was kept");
        Check(text.Contains("A4-1B-C0-11-22-33"), "the description should name the original");
        Check(KeepChangePolicy.Describe(null) == string.Empty, "a missing record describes as nothing");
    }
}
