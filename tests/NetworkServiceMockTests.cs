using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MacRando;

internal static class NetworkServiceMockTests
{
    private sealed class FakeRunner : IPowerShellRunner
    {
        public string LastScript;
        public int ExitCode;

        public Task<PowerShellResult> RunAsync(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            LastScript = script;
            return Task.FromResult(new PowerShellResult
            {
                ExitCode = ExitCode,
                StandardOutput = string.Empty,
                StandardError = ExitCode == 0 ? string.Empty : "mock provider failure"
            });
        }

        public Task<T> RunJsonAsync<T>(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            LastScript = script;
            object value;
            if (typeof(T) == typeof(List<AdapterInfo>))
            {
                value = new List<AdapterInfo>
                {
                    new AdapterInfo
                    {
                        Name = "Mock Ethernet",
                        InterfaceGuid = "{mock-guid}",
                        IsUp = true,
                        MacAddress = "02-00-00-00-00-01",
                        PermanentMacAddress = "02-00-00-00-00-02",
                        MacPropertySupported = true,
                        IpAddress = "192.168.1.20",
                        PrefixLength = 24,
                        DhcpEnabled = false
                    }
                };
            }
            else if (typeof(T) == typeof(NetworkState))
            {
                value = new NetworkState
                {
                    AdapterName = "Mock Ethernet",
                    InterfaceGuid = "{mock-guid}",
                    InterfaceIndex = 7,
                    CurrentMacAddress = "02-00-00-00-00-01",
                    MacOverrideValue = "020000000001",
                    MacOverridePresent = true,
                    MacPropertySupported = true,
                    IpAddress = "192.168.1.20",
                    PrefixLength = 24,
                    DhcpEnabled = false,
                    ConfigurationKnown = true,
                    DnsPolicyKnown = true,
                    IpAddresses = new[] { "192.168.1.20" }
                };
            }
            else
            {
                value = new List<VpnProfile>();
            }
            return Task.FromResult((T)value);
        }
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            var runner = new FakeRunner();
            var service = new NetworkService(runner);
            List<AdapterInfo> adapters = service.GetAdaptersAsync().GetAwaiter().GetResult();
            if (adapters.Count != 1 || adapters[0].Name != "Mock Ethernet") throw new Exception("adapter mock failed");
            NetworkState state = service.GetStateAsync(adapters[0]).GetAwaiter().GetResult();
            if (!state.MacPropertySupported || state.PrefixLength != 24) throw new Exception("state mock failed");
            IpPreflightReport preflight = new DiagnosticsService(service).RunIpPreflightAsync(adapters[0]).GetAwaiter().GetResult();
            if (preflight.ProposedIp == "Unavailable" || preflight.ToDisplayText().IndexOf("No adapter settings were changed", StringComparison.Ordinal) < 0) throw new Exception("IP preflight mock failed");
            UpdateCheckResult updateCheck = new UpdateService().CheckAsync(string.Empty, string.Empty).GetAwaiter().GetResult();
            if (!updateCheck.RequiresConfiguration || updateCheck.IsUpdateAvailable) throw new Exception("update configuration mock failed");
            service.ApplyChangesAsync(adapters[0], state, "02-00-00-00-00-03", null, true, false).GetAwaiter().GetResult();
            if (runner.LastScript.IndexOf("-RegistryValue", StringComparison.Ordinal) < 0 || runner.LastScript.IndexOf("-DisplayValue", StringComparison.Ordinal) >= 0) throw new Exception("MAC script safety check failed");
            service.ApplyChangesAsync(adapters[0], state, null, "192.168.1.123", false, true).GetAwaiter().GetResult();
            if (runner.LastScript.IndexOf("remainingOldAddresses", StringComparison.Ordinal) < 0 || runner.LastScript.IndexOf("ErrorAction SilentlyContinue", StringComparison.Ordinal) < 0 || runner.LastScript.IndexOf("New-NetRoute", StringComparison.Ordinal) >= 0 || runner.LastScript.IndexOf("$newAddress['DefaultGateway']", StringComparison.Ordinal) >= 0 || runner.LastScript.IndexOf("Remove-NetIPAddress -InterfaceIndex $index -IPAddress $env:MR_OLD_IP", StringComparison.Ordinal) >= 0) throw new Exception("IP removal safety check failed");
            service.RestoreAsync(new AdapterBackup
            {
                AdapterKey = "{mock-guid}",
                InterfaceGuid = "{mock-guid}",
                AdapterName = "Mock Ethernet",
                IpChanged = true,
                OriginalIpAddress = "192.168.1.20",
                OriginalIpAddresses = new[] { "192.168.1.20" },
                OriginalPrefixLength = 24,
                OriginalGateway = "192.168.1.1",
                OriginalDhcpEnabled = false,
                OriginalDnsServers = new[] { "192.168.1.1" }
            }).GetAwaiter().GetResult();
            if (runner.LastScript.IndexOf("New-NetRoute", StringComparison.Ordinal) >= 0 || runner.LastScript.IndexOf("$restoredAddress['DefaultGateway']", StringComparison.Ordinal) >= 0) throw new Exception("IP restore route safety check failed");
            runner.ExitCode = 1;
            try
            {
                service.ApplyChangesAsync(adapters[0], state, "02-00-00-00-00-03", null, true, false).GetAwaiter().GetResult();
                throw new Exception("provider failure was not surfaced");
            }
            catch (PowerShellCommandException)
            {
            }
            string stateRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MacRandoStateTest-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(stateRoot);
            try
            {
                var stateStore = new StateStore(stateRoot);
                var notificationState = new AppState();
                notificationState.Backups["{mock-guid}"] = new AdapterBackup
                {
                    AdapterKey = "{mock-guid}",
                    AdapterName = "Mock Ethernet",
                    InterfaceGuid = "{mock-guid}",
                    OriginalIpAddress = "192.168.1.20",
                    OriginalPrefixLength = 24,
                    OriginalDhcpEnabled = false
                };
                notificationState.Notifications.Add(new NotificationHistoryEntry
                {
                    NotificationId = "notification-test",
                    TimestampUtc = DateTime.UtcNow,
                    Title = "Test notification",
                    Message = "Test message",
                    Severity = NotificationKinds.Critical,
                    AdapterKey = "{mock-guid}",
                    Action = "Restore now",
                    Details = "test",
                    CanRestore = true,
                    CanRetry = true
                });
                stateStore.Save(notificationState);
                AppState loadedState = stateStore.Load();
                if (loadedState.Notifications == null || loadedState.Notifications.Count != 1 || loadedState.Notifications[0].Severity != NotificationKinds.Critical || !loadedState.Notifications[0].CanRestore) throw new Exception("notification state round-trip failed");
                if (loadedState.Backups == null || !loadedState.Backups.ContainsKey("{mock-guid}")) throw new Exception("notification state overwrote restore profiles");
                loadedState.Notifications.Add(new NotificationHistoryEntry { NotificationId = "notification-test-2", TimestampUtc = DateTime.UtcNow, Title = "Second", Message = "Second message", Severity = NotificationKinds.Info });
                stateStore.Save(loadedState);
                AppState afterNotificationState = stateStore.Load();
                if (afterNotificationState.Backups == null || !afterNotificationState.Backups.ContainsKey("{mock-guid}") || afterNotificationState.Notifications.Count != 2) throw new Exception("notification persistence lost restore state");
            }
            finally
            {
                try { System.IO.Directory.Delete(stateRoot, true); } catch { }
            }
            string settingsRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MacRandoSettingsTest-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(settingsRoot);
            try
            {
                var settingsStore = new AppSettingsStore(System.IO.Path.Combine(settingsRoot, "settings.json"));
                settingsStore.Save(new AppSettings
                {
                    StartMinimized = true,
                    StartWithWindows = false,
                    AutoRandomizeMacOnStartup = true,
                    AutoRandomizeAdapterKey = "{mock-guid}",
                    NotificationsEnabled = true,
                    NotificationSoundEnabled = false,
                    NotificationCollapseDuplicates = true,
                    NotificationQuietHoursEnabled = true,
                    NotificationQuietHoursStartHour = 21,
                    NotificationQuietHoursEndHour = 6,
                    NotificationDurationSeconds = 9,
                    UpdateManifestUrl = "https://example.invalid/macrando.json",
                    ExpectedSignerThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567",
                    ShowConnectedAdaptersOnly = true,
                    FavoriteAdapters = new System.Collections.Generic.List<string> { "{fav-1}", "{fav-1}", "  ", "{fav-2}" }
                });
                AppSettings settings = settingsStore.Load();
                if (!settings.StartMinimized || settings.StartWithWindows || !settings.AutoRandomizeMacOnStartup || settings.AutoRandomizeAdapterKey != "{mock-guid}") throw new Exception("settings round-trip failed");
                if (!settings.NotificationsEnabled || settings.NotificationSoundEnabled || !settings.NotificationCollapseDuplicates || !settings.NotificationQuietHoursEnabled || settings.NotificationQuietHoursStartHour != 21 || settings.NotificationQuietHoursEndHour != 6 || settings.NotificationDurationSeconds != 9 || settings.UpdateManifestUrl != "https://example.invalid/macrando.json" || settings.ExpectedSignerThumbprint != "0123456789ABCDEF0123456789ABCDEF01234567") throw new Exception("notification settings round-trip failed");

                if (!settings.ShowConnectedAdaptersOnly) throw new Exception("connected-only filter was not persisted");
                if (settings.FavoriteAdapters == null || settings.FavoriteAdapters.Count != 2) throw new Exception("favorites were not normalized (duplicates and blanks should be dropped)");
                if (settings.FavoriteAdapters[0] != "{fav-1}" || settings.FavoriteAdapters[1] != "{fav-2}") throw new Exception("favorites lost their order");
                if (!settings.IsFavorite("{FAV-1}") || settings.IsFavorite("{fav-3}") || settings.IsFavorite(null)) throw new Exception("favorite lookup is not case-insensitive or rejected nulls");
                if (!settings.ToggleFavorite("{fav-3}")) throw new Exception("toggling a new favorite should report that it is now a favorite");
                if (!settings.IsFavorite("{fav-3}")) throw new Exception("favorite was not added by toggle");
                if (settings.ToggleFavorite("{FAV-3}")) throw new Exception("toggling an existing favorite should report that it is no longer a favorite");
                if (settings.IsFavorite("{fav-3}")) throw new Exception("favorite was not removed by toggling again");

                System.IO.File.WriteAllText(System.IO.Path.Combine(settingsRoot, "legacy.json"), "{\"StartMinimized\":true}");
                var legacyStore = new AppSettingsStore(System.IO.Path.Combine(settingsRoot, "legacy.json"));
                AppSettings legacySettings = legacyStore.Load();
                if (!legacySettings.NotificationsEnabled || !legacySettings.NotificationSoundEnabled || legacySettings.NotificationDurationSeconds != 5) throw new Exception("notification defaults failed");

                // A 1.2.0-era settings file has no favorites or connected-only key at all.
                if (legacySettings.FavoriteAdapters == null || legacySettings.FavoriteAdapters.Count != 0) throw new Exception("legacy favorites did not default to an empty list");
                if (legacySettings.ShowConnectedAdaptersOnly) throw new Exception("legacy connected-only did not default to false");
            }
            finally
            {
                try { System.IO.Directory.Delete(settingsRoot, true); } catch { }
            }
            Console.WriteLine("mock-provider-tests=OK;settings-tests=OK");
            int upgradeResult = UpgradeRegressionChecks.Run();
            if (upgradeResult != 0)
            {
                return upgradeResult;
            }
            int contrastResult = ContrastTests.Run();
            if (contrastResult != 0)
            {
                return contrastResult;
            }
            int scriptResult = ScriptValidationTests.Run();
            if (scriptResult != 0)
            {
                return scriptResult;
            }
            int networkPresetResult = NetworkPresetTests.Run();
            if (networkPresetResult != 0)
            {
                return networkPresetResult;
            }
            int deviceTrackingResult = DeviceTrackingTests.Run();
            if (deviceTrackingResult != 0)
            {
                return deviceTrackingResult;
            }
            int retentionResult = RetentionTests.Run();
            if (retentionResult != 0)
            {
                return retentionResult;
            }
            int accessibilityResult = AccessibilityTests.Run();
            if (accessibilityResult != 0)
            {
                return accessibilityResult;
            }
            int layoutResult = LayoutTests.Run();
            if (layoutResult != 0)
            {
                return layoutResult;
            }
            int updateTrustResult = UpdateTrustTests.Run();
            if (updateTrustResult != 0)
            {
                return updateTrustResult;
            }
            int keepChangeResult = KeepChangeTests.Run();
            if (keepChangeResult != 0)
            {
                return keepChangeResult;
            }
            int adapterKindResult = AdapterKindTests.Run();
            if (adapterKindResult != 0)
            {
                return adapterKindResult;
            }
            int trayThemeResult = TrayThemeTests.Run();
            if (trayThemeResult != 0)
            {
                return trayThemeResult;
            }
            int installedVersionResult = InstalledVersionTests.Run();
            if (installedVersionResult != 0)
            {
                return installedVersionResult;
            }
            int licenseResult = LicenseTests.Run();
            if (licenseResult != 0)
            {
                return licenseResult;
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
