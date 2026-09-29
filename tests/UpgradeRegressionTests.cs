using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using IpChangeOutcomes = MacRando.IpChangeOutcomes;
using RetryKinds = MacRando.RetryKinds;
using UpdateCheckResult = MacRando.UpdateCheckResult;
using UpdateManifest = MacRando.UpdateManifest;

/// <summary>
/// Upgrade regression coverage for 1.3.0.
///
/// The point of these checks is backward compatibility: a state or settings file
/// written by an older MacRando must still load, and the new structured retry
/// metadata must degrade gracefully when it is absent.
/// </summary>
internal static class UpgradeRegressionChecks
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            RetryKindNormalization();
            RetryKindFlags();
            LegacyPendingOperationHasNoStructuredRetry();
            LegacyNotificationIsNotStructured();
            NewNotificationIsStructured();
            RandomRetryDropsStaleManualMac();
            StateNormalizationCapsAndSanitizes();
            StateRoundTripKeepsNewCollections();
            OlderStateFileStillLoads();
            PreflightChangeLines();
            InstallGuardRefusesUnsafeCases();
            InstallGuardAllowsVerifiedNewerBuild();
            CorruptRestoreStateRaisesTheSpecificError();
            CorruptRestoreStateDetailsAreActionable();
            ReadableRestoreStateDoesNotRaiseTheError();
            Console.WriteLine("upgrade-regression-tests=OK;checks=" + _checks);
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

    private static void RetryKindNormalization()
    {
        Check(RetryKinds.Normalize(null) == RetryKinds.None, "null retry kind should normalize to None");
        Check(RetryKinds.Normalize("  ") == RetryKinds.None, "blank retry kind should normalize to None");
        Check(RetryKinds.Normalize("mac") == RetryKinds.Mac, "retry kind should be case-insensitive");
        Check(RetryKinds.Normalize("MACANDIP") == RetryKinds.MacAndIp, "combined retry kind should normalize");
        Check(RetryKinds.Normalize("nonsense") == RetryKinds.None, "unknown retry kind should not be trusted");
    }

    private static void RetryKindFlags()
    {
        Check(RetryKinds.FromFlags(true, false) == RetryKinds.Mac, "mac only should map to Mac");
        Check(RetryKinds.FromFlags(false, true) == RetryKinds.Ip, "ip only should map to Ip");
        Check(RetryKinds.FromFlags(true, true) == RetryKinds.MacAndIp, "both should map to MacAndIp");
        Check(RetryKinds.FromFlags(false, false) == RetryKinds.None, "neither should map to None");

        Check(RetryKinds.IncludesMac(RetryKinds.MacAndIp) && RetryKinds.IncludesIp(RetryKinds.MacAndIp),
            "a combined retry should include both mac and ip");
        Check(!RetryKinds.IncludesMac(RetryKinds.Ip) && !RetryKinds.IncludesIp(RetryKinds.Mac),
            "single-target retries should not include the other target");
        Check(!RetryKinds.IncludesMac(RetryKinds.None), "None should include neither target");
    }

    private static void LegacyPendingOperationHasNoStructuredRetry()
    {
        // Simulates a PendingOperation deserialized from a pre-1.3.0 state file.
        var legacy = new MacRando.PendingOperation
        {
            OperationId = "op-1",
            AdapterKey = "{guid}",
            Action = "randomize the MAC address",
            Automatic = true,
            StartedAtUtc = DateTime.UtcNow
        };
        Check(string.IsNullOrEmpty(legacy.Kind), "a legacy pending operation must have no retry kind");
        Check(!legacy.GenerateRandomMac, "a legacy pending operation defaults to not requesting a random MAC");
        Check(string.IsNullOrEmpty(legacy.RequestedMac), "a legacy pending operation must not invent a MAC");
    }

    private static void LegacyNotificationIsNotStructured()
    {
        var legacy = new MacRando.NotificationHistoryEntry
        {
            NotificationId = "n-1",
            Title = "MAC randomized",
            Action = "randomize the MAC address",
            CanRetry = true
        };
        Check(!legacy.HasStructuredRetry, "a legacy notification must not claim structured retry metadata");

        // The old parser would treat this display text as "randomize the MAC".
        string action = legacy.Action ?? string.Empty;
        bool parsedMac = action.IndexOf("MAC", StringComparison.OrdinalIgnoreCase) >= 0;
        Check(parsedMac, "the legacy display text should still parse the way the old code did");
    }

    private static void NewNotificationIsStructured()
    {
        var entry = new MacRando.NotificationHistoryEntry
        {
            NotificationId = "n-2",
            Title = "Change failed",
            Action = "an opaque, unparseable action label",
            RetryKind = RetryKinds.MacAndIp,
            RetryGenerateRandomMac = true
        };
        Check(entry.HasStructuredRetry, "a new notification should expose structured retry metadata");
        Check(RetryKinds.IncludesMac(entry.RetryKind) && RetryKinds.IncludesIp(entry.RetryKind),
            "structured retry should not depend on the display text");

        // The display text is deliberately meaningless; the structured kind must win.
        Check(entry.Action.IndexOf("MAC", StringComparison.OrdinalIgnoreCase) < 0,
            "the fixture action text should not contain MAC so the test is meaningful");
    }

    private static void RandomRetryDropsStaleManualMac()
    {
        var store = InvokeNormalizeNotifications(new List<MacRando.NotificationHistoryEntry>
        {
            new MacRando.NotificationHistoryEntry
            {
                NotificationId = "n-3",
                RetryKind = RetryKinds.Mac,
                RetryGenerateRandomMac = true,
                RetryRequestedMac = "02-00-00-00-00-09"
            }
        });
        Check(store.Count == 1, "expected one normalized notification");
        Check(string.IsNullOrEmpty(store[0].RetryRequestedMac),
            "a random retry must not keep a stale manual MAC address");
    }

    private static void StateNormalizationCapsAndSanitizes()
    {
        var records = new List<MacRando.IpChangeRecord>();
        for (int i = 0; i < 80; i++)
        {
            records.Add(new MacRando.IpChangeRecord
            {
                RecordId = "r" + i,
                AdapterName = "Adapter " + i,
                OriginalAddress = "192.168.1.10",
                ProposedAddress = "192.168.1.99",
                Outcome = "something-unknown"
            });
        }
        List<MacRando.IpChangeRecord> normalized = InvokeNormalizeIpChangeHistory(records);
        Check(normalized.Count == 50, "IP change history should be capped at 50 entries, got " + normalized.Count);
        // The cap keeps the newest entries.
        Check(normalized[normalized.Count - 1].RecordId == "r79", "the cap must keep the newest records");
        foreach (MacRando.IpChangeRecord record in normalized)
        {
            Check(record.Outcome == MacRando.IpChangeOutcomes.Failed,
                "an unrecognized outcome should normalize to Failed, got " + record.Outcome);
            Check(!record.OriginalAddress.Contains("192.168.1.10"),
                "stored addresses must be sanitized, got " + record.OriginalAddress);
        }
    }

    private static void StateRoundTripKeepsNewCollections()
    {
        var state = new MacRando.AppState();
        state.IpChangeHistory.Add(new MacRando.IpChangeRecord
        {
            RecordId = "rec-1",
            AdapterName = "Ethernet",
            OriginalAddress = "masked-original",
            ProposedAddress = "masked-proposed",
            Outcome = MacRando.IpChangeOutcomes.Verified
        });
        state.Notifications.Add(new MacRando.NotificationHistoryEntry
        {
            NotificationId = "n-9",
            Title = "t",
            RetryKind = RetryKinds.Ip
        });
        string json = new JavaScriptSerializer().Serialize(state);
        var restored = new JavaScriptSerializer().Deserialize<MacRando.AppState>(json);
        Check(restored.IpChangeHistory != null && restored.IpChangeHistory.Count == 1, "IP change history must survive serialization");
        Check(restored.IpChangeHistory[0].RecordId == "rec-1", "the IP change record id must survive serialization");
        Check(restored.Notifications[0].RetryKind == RetryKinds.Ip, "retry metadata must survive serialization");
    }

    private static void OlderStateFileStillLoads()
    {
        // Exactly the shape a 1.2.0 state file would deserialize into: no new fields.
        string olderJson = "{\"SchemaVersion\":1,\"Backups\":{},\"Presets\":{}," +
            "\"PendingOperation\":{\"OperationId\":\"op\",\"AdapterKey\":\"{g}\",\"Action\":\"randomize the MAC address\",\"Automatic\":true,\"StartedAtUtc\":\"2026-09-01T00:00:00Z\"}," +
            "\"History\":[],\"Notifications\":[{\"NotificationId\":\"n\",\"Title\":\"t\",\"Action\":\"randomize the MAC address\",\"CanRetry\":true}]}";
        var state = new JavaScriptSerializer().Deserialize<MacRando.AppState>(olderJson);
        Check(state != null, "an older state file must deserialize");
        Check(state.IpChangeHistory == null || state.IpChangeHistory.Count == 0,
            "an older state file simply has no IP change history");
        Check(state.PendingOperation != null && string.IsNullOrEmpty(state.PendingOperation.Kind),
            "an older pending operation must load without structured metadata");
        Check(!state.Notifications[0].HasStructuredRetry,
            "an older notification must load without structured retry metadata");
    }

    private static void PreflightChangeLines()
    {
        var changed = new MacRando.PreflightChangeItem
        {
            Setting = "IPv4 address",
            Current = "masked-a",
            Planned = "masked-b",
            Changes = true,
            Note = "temporary"
        };
        string line = changed.ToDisplayLine();
        Check(line.Contains("CHANGE"), "a changed item should be marked CHANGE");
        Check(line.Contains("masked-a") && line.Contains("masked-b"), "a changed item should show both values");
        Check(line.Contains("(temporary)"), "a changed item should include its note");

        var kept = new MacRando.PreflightChangeItem { Setting = "Default routes", Current = "Existing", Planned = "Unchanged" };
        Check(kept.ToDisplayLine().Contains("keep"), "an unchanged item should be marked as kept");
        Check(!kept.ToDisplayLine().Contains("CHANGE"), "an unchanged item should not be marked CHANGE");
    }

    private static void InstallGuardRefusesUnsafeCases()
    {
        string verifiedFile = Path.Combine(Path.GetTempPath(), "MacRandoInstallTest-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(verifiedFile, new byte[] { 77, 90, 90, 0, 0, 0 });
        try
        {
            UpdateCheckResult verified = new UpdateCheckResult
            {
                IsVerified = true,
                IsUpdateAvailable = true,
                LocalDownloadPath = verifiedFile,
                StatusMessage = "downloaded",
                Manifest = new UpdateManifest
                {
                    Version = "9.9.9",
                    DownloadUrl = "https://example.invalid/MacRando-9.9.9.exe",
                    Sha256 = new string('A', 64),
                    SignerThumbprint = new string('B', 40)
                }
            };

            string reason;
            // The safety-critical case: never install while an adapter restore is pending,
            // because installing closes the app and the profile must be resolved first.
            Check(!MacRando.UpdateInstaller.CanInstall(verified, 1, false, "1.0.0", out reason),
                "install must be refused while a restore profile is pending");
            Check(reason.IndexOf("Restore", StringComparison.OrdinalIgnoreCase) >= 0,
                "the refusal should explain that a restore is pending, got: " + reason);

            Check(!MacRando.UpdateInstaller.CanInstall(verified, 0, true, "1.0.0", out reason),
                "install must be refused while an operation is running");

            // Downgrade and same-version must both be refused.
            UpdateCheckResult downgrade = CloneAs(verified, "0.9.0");
            Check(!MacRando.UpdateInstaller.CanInstall(downgrade, 0, false, "1.0.0", out reason),
                "install must refuse an older version");
            UpdateCheckResult sameVersion = CloneAs(verified, "1.0.0");
            Check(!MacRando.UpdateInstaller.CanInstall(sameVersion, 0, false, "1.0.0", out reason),
                "install must refuse the same version");

            // An unverified or missing download must never be installed.
            UpdateCheckResult unverified = CloneAs(verified, "9.9.9");
            unverified.IsVerified = false;
            Check(!MacRando.UpdateInstaller.CanInstall(unverified, 0, false, "1.0.0", out reason),
                "install must refuse a download that was not verified");

            UpdateCheckResult missingFile = CloneAs(verified, "9.9.9");
            missingFile.LocalDownloadPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
            Check(!MacRando.UpdateInstaller.CanInstall(missingFile, 0, false, "1.0.0", out reason),
                "install must refuse when the verified download is gone from disk");

            Check(!MacRando.UpdateInstaller.CanInstall(null, 0, false, "1.0.0", out reason),
                "install must refuse when no check has run");

            UpdateCheckResult unconfigured = new UpdateCheckResult
            {
                RequiresConfiguration = true,
                StatusMessage = "No update manifest URL is configured."
            };
            Check(!MacRando.UpdateInstaller.CanInstall(unconfigured, 0, false, "1.0.0", out reason),
                "install must refuse when the updater is not configured");
        }
        finally
        {
            try { File.Delete(verifiedFile); } catch { }
        }
    }

    private static void InstallGuardAllowsVerifiedNewerBuild()
    {
        string verifiedFile = Path.Combine(Path.GetTempPath(), "MacRandoInstallTest-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(verifiedFile, new byte[] { 77, 90, 90, 0, 0, 0 });
        try
        {
            UpdateCheckResult verified = new UpdateCheckResult
            {
                IsVerified = true,
                IsUpdateAvailable = true,
                LocalDownloadPath = verifiedFile,
                StatusMessage = "downloaded and verified",
                Manifest = new UpdateManifest
                {
                    Version = "2.0.1",
                    DownloadUrl = "https://example.invalid/MacRando-2.0.1.exe",
                    Sha256 = new string('A', 64),
                    SignerThumbprint = new string('B', 40)
                }
            };
            string reason;
            Check(MacRando.UpdateInstaller.CanInstall(verified, 0, false, "2.0.0", out reason),
                "a verified newer build with no pending restore should be installable, got: " + reason);
            Check(string.IsNullOrEmpty(reason), "an allowed install should not report a refusal reason");
        }
        finally
        {
            try { File.Delete(verifiedFile); } catch { }
        }
    }

    private static UpdateCheckResult CloneAs(UpdateCheckResult source, string version)
    {
        return new UpdateCheckResult
        {
            IsVerified = source.IsVerified,
            IsUpdateAvailable = source.IsUpdateAvailable,
            RequiresConfiguration = source.RequiresConfiguration,
            StatusMessage = source.StatusMessage,
            LocalDownloadPath = source.LocalDownloadPath,
            Manifest = source.Manifest == null
                ? null
                : new UpdateManifest
                {
                    Version = version,
                    DownloadUrl = source.Manifest.DownloadUrl,
                    Sha256 = source.Manifest.Sha256,
                    SignerThumbprint = source.Manifest.SignerThumbprint,
                    ReleaseNotesUrl = source.Manifest.ReleaseNotesUrl,
                    PublishedUtc = source.Manifest.PublishedUtc
                }
        };
    }

    private static void CorruptRestoreStateRaisesTheSpecificError()
    {
        string root = Path.Combine(Path.GetTempPath(), "MacRandoStateTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Garbage where the encrypted state should be, and no usable backup.
            File.WriteAllText(Path.Combine(root, "state.json"), "{ this is not valid json");
            File.WriteAllText(Path.Combine(root, "state.json.bak"), "also not valid");

            var store = new MacRando.StateStore(root);
            bool raised = false;
            try
            {
                store.Load();
            }
            catch (MacRando.RestoreStateUnreadableException error)
            {
                raised = true;
                _checks++;
                Check(error.Files.Count == 3,
                    "the error should describe every candidate file, got " + error.Files.Count);
                Check(error.InnerException != null, "the error should keep the underlying failure");
            }
            Check(raised, "an unreadable restore state must raise RestoreStateUnreadableException, " +
                "so it can be reported loudly instead of as a generic error");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void CorruptRestoreStateDetailsAreActionable()
    {
        string root = Path.Combine(Path.GetTempPath(), "MacRandoStateTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "state.json"), "not json");
            var store = new MacRando.StateStore(root);
            try
            {
                store.Load();
                Check(false, "expected an unreadable-state error");
            }
            catch (MacRando.RestoreStateUnreadableException error)
            {
                string details = error.BuildDetails();
                Check(details.Contains("state.json"), "details should name the unreadable file");
                Check(details.Contains("not present") || details.Contains("unreadable") || details.Contains(":"),
                    "details should state the status of each file");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void ReadableRestoreStateDoesNotRaiseTheError()
    {
        string root = Path.Combine(Path.GetTempPath(), "MacRandoStateTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new MacRando.StateStore(root);
            MacRando.AppState state = store.Load();
            _checks++;
            Check(state != null, "an absent state file should yield a fresh state, not an error");
            Check(state.Backups != null && state.Backups.Count == 0, "a fresh state should have no backups");

            // The backup copy is only created from the second save onward, because
            // File.Replace needs an existing destination to move aside.
            state.Backups["{guid}"] = new MacRando.AdapterBackup
            {
                AdapterKey = "{guid}",
                AdapterName = "Ethernet",
                InterfaceGuid = "{guid}",
                MacChanged = true
            };
            store.Save(state);
            store.Save(state);
            Check(File.Exists(Path.Combine(root, "state.json.bak")),
                "a second save should leave a backup copy behind");

            File.WriteAllText(Path.Combine(root, "state.json"), "corrupt now");
            MacRando.AppState recovered = store.Load();
            _checks++;
            Check(recovered.Backups.Count == 1,
                "a corrupt primary file should fall back to the backup without raising an error");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    // The normalization helpers are private, so reach them the same way the store does.
    private static List<MacRando.NotificationHistoryEntry> InvokeNormalizeNotifications(List<MacRando.NotificationHistoryEntry> entries)
    {
        MethodInfo method = typeof(MacRando.StateStore).GetMethod(
            "NormalizeNotifications", BindingFlags.Static | BindingFlags.NonPublic);
        Check(method != null, "StateStore.NormalizeNotifications was not found");
        return (List<MacRando.NotificationHistoryEntry>)method.Invoke(null, new object[] { entries });
    }

    private static List<MacRando.IpChangeRecord> InvokeNormalizeIpChangeHistory(List<MacRando.IpChangeRecord> records)
    {
        MethodInfo method = typeof(MacRando.StateStore).GetMethod(
            "NormalizeIpChangeHistory", BindingFlags.Static | BindingFlags.NonPublic);
        Check(method != null, "StateStore.NormalizeIpChangeHistory was not found");
        return (List<MacRando.IpChangeRecord>)method.Invoke(null, new object[] { records });
    }
}
