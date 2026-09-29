using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MacRando
{
    internal sealed class StateStore
    {
        private sealed class ProtectedStateEnvelope
        {
            public string Format { get; set; }
            public int SchemaVersion { get; set; }
            public string ProtectedData { get; set; }
        }

        private const string ProtectedFormat = "MacRando.DPAPI.v1";
        private static readonly byte[] ProtectionEntropy = Encoding.UTF8.GetBytes("MacRando.RestoreState.v1");
        private readonly string _statePath;
        private readonly string _backupPath;
        private readonly object _sync = new object();

        public StateStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MacRando"))
        {
        }

        internal StateStore(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentNullException("root");
            }
            _statePath = Path.Combine(root, "state.json");
            _backupPath = Path.Combine(root, "state.json.bak");
        }

        public string StatePath
        {
            get { return _statePath; }
        }

        public string DataDirectory
        {
            get { return Path.GetDirectoryName(_statePath); }
        }

        public AppState Load()
        {
            lock (_sync)
            {
                string temporaryPath = _statePath + ".tmp";
                string[] candidates = { _statePath, temporaryPath, _backupPath };
                DateTime bestWriteTime = DateTime.MinValue;
                string bestPath = null;
                AppState bestState = null;
                bool bestNeedsMigration = false;
                Exception firstError = null;
                bool foundFile = false;

                foreach (string candidate in candidates)
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    foundFile = true;
                    try
                    {
                        bool needsMigration;
                        AppState candidateState = ReadState(candidate, out needsMigration);
                        DateTime writeTime = File.GetLastWriteTimeUtc(candidate);
                        if (bestState == null || writeTime > bestWriteTime)
                        {
                            bestState = candidateState;
                            bestPath = candidate;
                            bestWriteTime = writeTime;
                            bestNeedsMigration = needsMigration;
                        }
                    }
                    catch (Exception error)
                    {
                        if (firstError == null)
                        {
                            firstError = error;
                        }
                    }
                }

                if (bestState == null)
                {
                    if (foundFile)
                    {
                        throw new InvalidDataException(
                            "MacRando could not read any valid saved restore data.",
                            firstError);
                    }

                    return new AppState();
                }

                if (!string.Equals(bestPath, _statePath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Copy(bestPath, _statePath, true);
                    }
                    catch
                    {
                        // Keep the valid in-memory profile even if automatic repair is not permitted.
                    }
                }

                if (bestNeedsMigration)
                {
                    try
                    {
                        WriteStateFile(_statePath, bestState);
                        AppLogger.Info("Legacy restore state migrated to DPAPI protection.");
                    }
                    catch
                    {
                        AppLogger.Warning("Legacy restore state could not be migrated automatically.");
                    }
                }

                AppLogger.Info("Restore state loaded; backup profiles=" + bestState.Backups.Count + ", presets=" + bestState.Presets.Count + ", pending=" + (bestState.PendingOperation == null ? 0 : 1) + ", notifications=" + (bestState.Notifications == null ? 0 : bestState.Notifications.Count));
                return bestState;
            }
        }

        public void Save(AppState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            lock (_sync)
            {
                string directory = Path.GetDirectoryName(_statePath);
                Directory.CreateDirectory(directory);
                string temporaryPath = _statePath + ".tmp";
                WriteStateFile(temporaryPath, state);

                if (File.Exists(_statePath))
                {
                    try
                    {
                        File.Replace(temporaryPath, _statePath, _backupPath, true);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporaryPath, _statePath, true);
                        File.Delete(temporaryPath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        File.Copy(temporaryPath, _statePath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, _statePath);
                }

                AppLogger.Info("Restore state saved with DPAPI protection; backup profiles=" + (state.Backups == null ? 0 : state.Backups.Count) + ", presets=" + (state.Presets == null ? 0 : state.Presets.Count) + ", history=" + (state.History == null ? 0 : state.History.Count) + ", notifications=" + (state.Notifications == null ? 0 : state.Notifications.Count));
            }
        }

        private static AppState ReadState(string path, out bool legacy)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            var serializer = new JavaScriptSerializer();
            ProtectedStateEnvelope envelope = null;
            try
            {
                envelope = serializer.Deserialize<ProtectedStateEnvelope>(text);
            }
            catch
            {
                // It may be a legacy plaintext state file.
            }

            AppState state;
            if (envelope != null && string.Equals(envelope.Format, ProtectedFormat, StringComparison.Ordinal))
            {
                if (envelope.SchemaVersion > 1 || string.IsNullOrWhiteSpace(envelope.ProtectedData))
                {
                    throw new InvalidDataException("The protected state file is invalid or was created by a newer version of MacRando.");
                }

                byte[] protectedBytes = Convert.FromBase64String(envelope.ProtectedData);
                byte[] clearBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    ProtectionEntropy,
                    DataProtectionScope.CurrentUser);
                state = serializer.Deserialize<AppState>(Encoding.UTF8.GetString(clearBytes));
                legacy = false;
            }
            else
            {
                state = serializer.Deserialize<AppState>(text);
                legacy = true;
            }

            if (state == null)
            {
                throw new InvalidDataException("The state file is empty.");
            }

            if (state.SchemaVersion > 1)
            {
                throw new InvalidDataException("The state file was created by a newer version of MacRando.");
            }

            state.SchemaVersion = 1;
            state.Backups = NormalizeBackups(state.Backups);
            state.Presets = NormalizePresets(state.Presets);
            state.History = NormalizeHistory(state.History);
            state.Notifications = NormalizeNotifications(state.Notifications);
            state.IpChangeHistory = NormalizeIpChangeHistory(state.IpChangeHistory);
            return state;
        }

        private static Dictionary<string, AdapterBackup> NormalizeBackups(Dictionary<string, AdapterBackup> backups)
        {
            var normalized = new Dictionary<string, AdapterBackup>(StringComparer.OrdinalIgnoreCase);
            if (backups != null)
            {
                foreach (KeyValuePair<string, AdapterBackup> item in backups)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key) && item.Value != null)
                    {
                        normalized[item.Key] = item.Value;
                    }
                }
            }
            return normalized;
        }

        private static Dictionary<string, AdapterPreset> NormalizePresets(Dictionary<string, AdapterPreset> presets)
        {
            var normalized = new Dictionary<string, AdapterPreset>(StringComparer.OrdinalIgnoreCase);
            if (presets != null)
            {
                foreach (KeyValuePair<string, AdapterPreset> item in presets)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key) && item.Value != null)
                    {
                        item.Value.PresetKey = item.Value.PresetKey ?? item.Key;
                        normalized[item.Key] = item.Value;
                    }
                }
            }
            return normalized;
        }

        private static List<OperationHistoryEntry> NormalizeHistory(List<OperationHistoryEntry> history)
        {
            var normalized = new List<OperationHistoryEntry>();
            if (history != null)
            {
                foreach (OperationHistoryEntry entry in history)
                {
                    if (entry != null)
                    {
                        normalized.Add(entry);
                    }
                }
            }
            if (normalized.Count > 100)
            {
                normalized.RemoveRange(0, normalized.Count - 100);
            }
            return normalized;
        }

        private static List<NotificationHistoryEntry> NormalizeNotifications(List<NotificationHistoryEntry> notifications)
        {
            var normalized = new List<NotificationHistoryEntry>();
            if (notifications != null)
            {
                foreach (NotificationHistoryEntry entry in notifications)
                {
                    if (entry == null)
                    {
                        continue;
                    }
                    entry.NotificationId = string.IsNullOrWhiteSpace(entry.NotificationId)
                        ? Guid.NewGuid().ToString("N")
                        : entry.NotificationId;
                    entry.Title = AppLogger.Sanitize(entry.Title);
                    entry.Message = AppLogger.Sanitize(entry.Message);
                    entry.AdapterKey = AppLogger.Sanitize(entry.AdapterKey);
                    entry.Action = AppLogger.Sanitize(entry.Action);
                    entry.Details = AppLogger.Sanitize(entry.Details);
                    entry.Severity = NotificationKinds.Normalize(entry.Severity);
                    entry.RetryKind = RetryKinds.Normalize(entry.RetryKind);
                    if (entry.RetryGenerateRandomMac)
                    {
                        // A random retry must not carry a stale manual address.
                        entry.RetryRequestedMac = null;
                    }
                    else
                    {
                        entry.RetryRequestedMac = AppLogger.Sanitize(entry.RetryRequestedMac);
                    }
                    normalized.Add(entry);
                }
            }
            if (normalized.Count > 100)
            {
                normalized.RemoveRange(0, normalized.Count - 100);
            }
            return normalized;
        }

        private static List<IpChangeRecord> NormalizeIpChangeHistory(List<IpChangeRecord> history)
        {
            var normalized = new List<IpChangeRecord>();
            if (history != null)
            {
                foreach (IpChangeRecord record in history)
                {
                    if (record == null)
                    {
                        continue;
                    }
                    record.RecordId = string.IsNullOrWhiteSpace(record.RecordId)
                        ? Guid.NewGuid().ToString("N")
                        : record.RecordId;
                    record.AdapterKey = AppLogger.Sanitize(record.AdapterKey);
                    record.AdapterName = AppLogger.Sanitize(record.AdapterName);
                    record.OriginalAddress = AppLogger.Sanitize(record.OriginalAddress);
                    record.ProposedAddress = AppLogger.Sanitize(record.ProposedAddress);
                    record.OriginalPrefixLength = AppLogger.Sanitize(record.OriginalPrefixLength);
                    record.OriginalDhcp = AppLogger.Sanitize(record.OriginalDhcp);
                    record.OriginalGateway = AppLogger.Sanitize(record.OriginalGateway);
                    record.Notes = AppLogger.Sanitize(record.Notes);
                    record.Outcome = IpChangeOutcomes.Normalize(record.Outcome);
                    normalized.Add(record);
                }
            }
            if (normalized.Count > 50)
            {
                normalized.RemoveRange(0, normalized.Count - 50);
            }
            return normalized;
        }

        private static void WriteStateFile(string path, AppState state)
        {
            var serializer = new JavaScriptSerializer();
            string clearJson = serializer.Serialize(state);
            byte[] clearBytes = Encoding.UTF8.GetBytes(clearJson);
            byte[] protectedBytes = ProtectedData.Protect(
                clearBytes,
                ProtectionEntropy,
                DataProtectionScope.CurrentUser);
            var envelope = new ProtectedStateEnvelope
            {
                Format = ProtectedFormat,
                SchemaVersion = state.SchemaVersion,
                ProtectedData = Convert.ToBase64String(protectedBytes)
            };
            File.WriteAllText(path, serializer.Serialize(envelope), new UTF8Encoding(false));
        }
    }
}
