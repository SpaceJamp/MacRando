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
        }

        public string SettingsPath
        {
            get { return _settingsPath; }
        }

        public AppSettings Load()
        {
            lock (_sync)
            {
                try
                {
                    if (!File.Exists(_settingsPath))
                    {
                        return new AppSettings();
                    }
                    AppSettings settings = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(_settingsPath, Encoding.UTF8));
                    return Normalize(settings);
                }
                catch
                {
                    return new AppSettings();
                }
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
                File.WriteAllText(
                    temporaryPath,
                    new JavaScriptSerializer().Serialize(settings),
                    new UTF8Encoding(false));
                if (File.Exists(_settingsPath))
                {
                    File.Delete(_settingsPath);
                }
                File.Move(temporaryPath, _settingsPath);
            }
        }
    }
}
