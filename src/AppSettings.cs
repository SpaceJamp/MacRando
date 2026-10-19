using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MacRando
{
    internal sealed class AppSettings
    {
        public bool StartMinimized { get; set; }
        public bool StartWithWindows { get; set; }
        public bool AutoRandomizeMacOnStartup { get; set; }
        public string AutoRandomizeAdapterKey { get; set; }
        public bool NotificationsEnabled { get; set; }
        public bool NotificationSoundEnabled { get; set; }
        public bool NotificationCollapseDuplicates { get; set; }
        public bool NotificationQuietHoursEnabled { get; set; }
        public int NotificationQuietHoursStartHour { get; set; }
        public int NotificationQuietHoursEndHour { get; set; }
        public int NotificationDurationSeconds { get; set; }
        public string UpdateManifestUrl { get; set; }
        public string ExpectedSignerThumbprint { get; set; }
        public System.Collections.Generic.List<string> FavoriteAdapters { get; set; }
        public bool ShowConnectedAdaptersOnly { get; set; }
        /// <summary>
        /// Whether to fetch and show public IP geolocation. Off by default for privacy.
        /// </summary>
        public bool ShowPublicIpLocation { get; set; }

        public AppSettings()
        {
            NotificationsEnabled = true;
            NotificationSoundEnabled = true;
            NotificationCollapseDuplicates = true;
            NotificationQuietHoursEnabled = false;
            NotificationQuietHoursStartHour = 22;
            NotificationQuietHoursEndHour = 7;
            NotificationDurationSeconds = 5;
            UpdateManifestUrl = string.Empty;
            ExpectedSignerThumbprint = string.Empty;
            FavoriteAdapters = new System.Collections.Generic.List<string>();
        }

        public bool IsFavorite(string adapterKey)
        {
            if (string.IsNullOrWhiteSpace(adapterKey) || FavoriteAdapters == null)
            {
                return false;
            }
            foreach (string key in FavoriteAdapters)
            {
                if (string.Equals(key, adapterKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public bool ToggleFavorite(string adapterKey)
        {
            if (string.IsNullOrWhiteSpace(adapterKey))
            {
                return false;
            }
            if (FavoriteAdapters == null)
            {
                FavoriteAdapters = new System.Collections.Generic.List<string>();
            }
            string existing = null;
            foreach (string key in FavoriteAdapters)
            {
                if (string.Equals(key, adapterKey, StringComparison.OrdinalIgnoreCase))
                {
                    existing = key;
                    break;
                }
            }
            if (existing != null)
            {
                FavoriteAdapters.Remove(existing);
                return false;
            }
            FavoriteAdapters.Add(adapterKey.Trim());
            return true;
        }
    }

    internal sealed class AppSettingsStore
    {
        private readonly string _settingsPath;
        private readonly string _updateTrustPath;
        private readonly object _sync = new object();

        public AppSettingsStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MacRando",
                "settings.json"))
        {
        }

        internal AppSettingsStore(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentNullException("settingsPath");
            }
            _settingsPath = settingsPath;
            _updateTrustPath = settingsPath + ".trust";
        }

        public string SettingsPath
        {
            get { return _settingsPath; }
        }

        /// <summary>
        /// Where the encrypted trust values live. Named after the settings file so a user
        /// who deletes one to start clean removes both, which is the desired outcome.
        /// </summary>
        public string UpdateTrustPath
        {
            get { return _updateTrustPath; }
        }

        public AppSettings Load()
        {
            lock (_sync)
            {
                // The trust file is read first and independently, because a user who
                // deletes the plain settings file to start clean has not asked to
                // discard where updates come from, and losing that silently disables
                // update checks with no visible cause.
                UpdateTrustPayload trust = ReadUpdateTrust();
                AppSettings settings;
                try
                {
                    settings = File.Exists(_settingsPath)
                        ? Normalize(new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(_settingsPath, Encoding.UTF8)))
                        : new AppSettings();
                }
                catch (Exception error)
                {
                    // Preferences that cannot be read fall back to defaults, but the trust
                    // values are still applied below. Dropping them here used to mean the
                    // next preference change wrote blank values over a trust file that was
                    // perfectly readable, erasing where updates come from.
                    AppLogger.Error("settings.json could not be read; using default preferences.", error);
                    settings = new AppSettings();
                }
                if (trust != null)
                {
                    settings.UpdateManifestUrl = trust.UpdateManifestUrl ?? string.Empty;
                    settings.ExpectedSignerThumbprint = trust.ExpectedSignerThumbprint ?? string.Empty;
                }
                return settings;
            }
        }

        /// <summary>
        /// Overlays the encrypted trust file onto the loaded settings.
        ///
        /// The trust file wins over settings.json, because that is where the values live
        /// now. A settings.json that still carries them is a pre-1.11 file, and its values
        /// are migrated on the next save; until then they are honoured so an upgrade does
        /// not silently disable update checks for someone who had configured them.
        /// </summary>
        private UpdateTrustPayload ReadUpdateTrust()
        {
            if (!File.Exists(UpdateTrustPath))
            {
                return null;
            }
            try
            {
                UpdateTrustPayload payload = UpdateTrustEnvelope.Unprotect(
                    File.ReadAllText(UpdateTrustPath, Encoding.UTF8));
                if (payload == null)
                {
                    // Worth saying out loud: update checks keep working from any values
                    // left in the plain file, but a user who edited the trust file by
                    // hand should know it is not being honoured.
                    AppLogger.Warning("The encrypted update trust file could not be read; " +
                        "falling back to any values in settings.json.");
                }
                return payload;
            }
            catch (Exception error)
            {
                AppLogger.Error("Could not read the encrypted update trust file.", error);
                return null;
            }
        }

        private static AppSettings Normalize(AppSettings settings)
        {
            AppSettings normalized = settings ?? new AppSettings();
            normalized.NotificationQuietHoursStartHour = Math.Max(0, Math.Min(23, normalized.NotificationQuietHoursStartHour));
            normalized.NotificationQuietHoursEndHour = Math.Max(0, Math.Min(23, normalized.NotificationQuietHoursEndHour));
            normalized.NotificationDurationSeconds = Math.Max(2, Math.Min(60, normalized.NotificationDurationSeconds));
            normalized.UpdateManifestUrl = normalized.UpdateManifestUrl ?? string.Empty;
            normalized.ExpectedSignerThumbprint = (normalized.ExpectedSignerThumbprint ?? string.Empty).Replace(" ", "").ToUpperInvariant();
            normalized.FavoriteAdapters = NormalizeFavorites(normalized.FavoriteAdapters);
            return normalized;
        }

        private static System.Collections.Generic.List<string> NormalizeFavorites(System.Collections.Generic.List<string> favorites)
        {
            var normalized = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (favorites == null)
            {
                return normalized;
            }
            foreach (string key in favorites)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }
                string trimmed = key.Trim();
                if (seen.Add(trimmed) && normalized.Count < 50)
                {
                    normalized.Add(trimmed);
                }
            }
            return normalized;
        }

        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }
            settings = Normalize(settings);

            lock (_sync)
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                Directory.CreateDirectory(directory);
                string temporaryPath = _settingsPath + ".tmp";

                // The trust file is written first, and the two values are cleared from the
                // clear text file only once it has been. Clearing them unconditionally lost
                // the update source whenever the encrypted write failed, while the log
                // claimed the values were still in settings.json.
                bool hadTrust = !string.IsNullOrWhiteSpace(settings.UpdateManifestUrl) ||
                    !string.IsNullOrWhiteSpace(settings.ExpectedSignerThumbprint);
                bool trustProtected = true;
                if (hadTrust || File.Exists(_updateTrustPath))
                {
                    trustProtected = WriteUpdateTrust(settings);
                }

                // Copied through the serializer rather than field by field, so a preference
                // added later cannot be left out. ShowPublicIpLocation was, and the opt-in
                // was written as false on every save, so it never survived a restart.
                var serializer = new JavaScriptSerializer();
                AppSettings preferences = serializer.Deserialize<AppSettings>(serializer.Serialize(settings));
                if (trustProtected)
                {
                    preferences.UpdateManifestUrl = string.Empty;
                    preferences.ExpectedSignerThumbprint = string.Empty;
                }
                File.WriteAllText(
                    temporaryPath,
                    serializer.Serialize(preferences),
                    new UTF8Encoding(false));
                if (File.Exists(_settingsPath))
                {
                    File.Delete(_settingsPath);
                }
                File.Move(temporaryPath, _settingsPath);
            }
        }

        private bool WriteUpdateTrust(AppSettings settings)
        {
            try
            {
                var envelope = new UpdateTrustEnvelope
                {
                    UpdateManifestUrl = settings.UpdateManifestUrl ?? string.Empty,
                    ExpectedSignerThumbprint = settings.ExpectedSignerThumbprint ?? string.Empty
                };
                string temporaryPath = _updateTrustPath + ".tmp";
                File.WriteAllText(temporaryPath, envelope.Protect(), new UTF8Encoding(false));
                if (File.Exists(_updateTrustPath))
                {
                    File.Delete(_updateTrustPath);
                }
                File.Move(temporaryPath, _updateTrustPath);
                return true;
            }
            catch (Exception error)
            {
                // A trust file that cannot be written is worth reporting, because the
                // values stay in clear text until it can be. Update checks still work;
                // they are simply not protected yet.
                AppLogger.Error("Could not write the encrypted update trust file; the values remain in settings.json.", error);
                return false;
            }
        }
    }
}
