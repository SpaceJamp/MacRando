using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Security.Principal;
using Microsoft.Win32;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MacRando
{
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NetworkService _network;
        private readonly StateStore _stateStore;
        private readonly DiagnosticsService _diagnostics;
        private readonly AppSettingsStore _settingsStore;
        private readonly AppSettings _settings;
        private readonly bool _startupLaunch;
        private readonly DashboardForm _form;
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _publicIpMenuItem;
        private readonly ToolStripMenuItem _adaptersMenu;
        private readonly ToolStripMenuItem _restoreMenu;
        private readonly ToolStripMenuItem _vpnMenu;
        private readonly ToolStripMenuItem _statusMenuItem;
        private readonly ToolStripMenuItem _refreshMenuItem;
        private readonly ToolStripMenuItem _diagnosticsMenuItem;
        private readonly ToolStripMenuItem _notificationCenterMenuItem;
        private readonly ToolStripMenuItem _startMinimizedMenuItem;
        private readonly ToolStripMenuItem _startWithWindowsMenuItem;
        private readonly ToolStripMenuItem _autoRandomizeMenuItem;
        private readonly ToolStripMenuItem _exitMenuItem;
        private List<AdapterInfo> _adapters = new List<AdapterInfo>();
        private List<VpnProfile> _vpnProfiles = new List<VpnProfile>();
        private bool _vpnProfilesStale;
        private bool _startupRandomizeBlocked;
        private bool _startupRandomizeAttempted;
        private bool _stateLoadedSuccessfully;
        private AppState _state = new AppState();
        private string _publicIp = "Unavailable";
        private bool _busy;
        private bool _confirmationOpen;
        private NotificationCenterForm _notificationCenterForm;
        private readonly HashSet<string> _changedAdapterKeysThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<NotificationPopup> _notificationPopups = new List<NotificationPopup>();

        public TrayContext()
            : this(false)
        {
        }

        internal TrayContext(bool startupLaunch)
        {
            _startupLaunch = startupLaunch;
            _network = new NetworkService();
            _stateStore = new StateStore();
            try
            {
                _state = _stateStore.Load();
                _stateLoadedSuccessfully = true;
            }
            catch (Exception error)
            {
                AppLogger.Error("Initial restore-state load failed; persistence is temporarily disabled.", error);
            }
            _diagnostics = new DiagnosticsService(_network);
            _settingsStore = new AppSettingsStore();
            _settings = _settingsStore.Load();
            if (_settings.StartWithWindows && !IsStartupRegistrationPresent())
            {
                _settings.StartWithWindows = false;
                _settingsStore.Save(_settings);
            }
            _form = new DashboardForm();
            _form.SetStartupRandomizationEnabled(_settings.AutoRandomizeMacOnStartup);
            _menu = new ContextMenuStrip();
            _publicIpMenuItem = new ToolStripMenuItem("Public IP: checking...");
            _adaptersMenu = new ToolStripMenuItem("Adapters");
            _restoreMenu = new ToolStripMenuItem("Restore saved adapter");
            _vpnMenu = new ToolStripMenuItem("Windows VPN profiles");
            _statusMenuItem = new ToolStripMenuItem("Status: starting...");
            _refreshMenuItem = new ToolStripMenuItem("Refresh");
            _diagnosticsMenuItem = new ToolStripMenuItem("Run read-only diagnostics");
            _notificationCenterMenuItem = new ToolStripMenuItem("Notification center");
            _startMinimizedMenuItem = new ToolStripMenuItem("Start minimized to tray") { Checked = _settings.StartMinimized };
            _startWithWindowsMenuItem = new ToolStripMenuItem("Start with Windows") { Checked = _settings.StartWithWindows };
            _autoRandomizeMenuItem = new ToolStripMenuItem("Randomize MAC on startup (risky)") { Checked = _settings.AutoRandomizeMacOnStartup };
            _exitMenuItem = new ToolStripMenuItem("Exit");

            _publicIpMenuItem.Enabled = false;
            _adaptersMenu.Enabled = false;
            _restoreMenu.Enabled = false;
            _vpnMenu.Enabled = false;
            _statusMenuItem.Enabled = false;

            BuildMenu();
            _trayIcon = LoadTrayIcon();
            _notifyIcon = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "MacRando",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (sender, args) => _form.ShowDashboard();

            WireFormEvents();

            // Wait until Application.Run has installed the WinForms synchronization context
            // before refreshing controls from asynchronous PowerShell operations.
            AppLogger.Info("MacRando " + AppInfo.DisplayVersion + " started.");
            Application.Idle += StartInitialRefresh;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.Idle -= StartInitialRefresh;
                List<NotificationPopup> popups = new List<NotificationPopup>(_notificationPopups);
                _notificationPopups.Clear();
                foreach (NotificationPopup popup in popups)
                {
                    popup.Close();
                    popup.Dispose();
                }
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _trayIcon.Dispose();
                if (_notificationCenterForm != null)
                {
                    _notificationCenterForm.CloseForDispose();
                    _notificationCenterForm.Dispose();
                    _notificationCenterForm = null;
                }
                _menu.Dispose();
                _form.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildMenu()
        {
            ToolStripMenuItem open = new ToolStripMenuItem("Open dashboard");
            open.Click += (sender, args) => _form.ShowDashboard();

            _refreshMenuItem.Click += async (sender, args) => await SafeRefreshAsync();
            _diagnosticsMenuItem.Click += async (sender, args) => await RunDiagnosticsAsync();
            _notificationCenterMenuItem.Click += (sender, args) => ShowNotificationCenter();
            _startMinimizedMenuItem.Click += (sender, args) => ToggleStartMinimized();
            _startWithWindowsMenuItem.Click += (sender, args) => ToggleStartWithWindows();
            _autoRandomizeMenuItem.Click += (sender, args) => ToggleStartupRandomization();

            ToolStripMenuItem stateFolder = new ToolStripMenuItem("Open restore-data folder");
            stateFolder.Click += (sender, args) => OpenStateFolder();

            ToolStripMenuItem logsFolder = new ToolStripMenuItem("Open logs folder");
            logsFolder.Click += (sender, args) => OpenLogsFolder();

            ToolStripMenuItem about = new ToolStripMenuItem("About MacRando");
            about.Click += (sender, args) => ShowAbout();

            _exitMenuItem.Click += async (sender, args) => await ExitApplicationAsync();

            _menu.Items.Add(open);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_statusMenuItem);
            _menu.Items.Add(_publicIpMenuItem);
            _menu.Items.Add(_adaptersMenu);
            _menu.Items.Add(_restoreMenu);
            _menu.Items.Add(_vpnMenu);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_refreshMenuItem);
            _menu.Items.Add(_diagnosticsMenuItem);
            _menu.Items.Add(_notificationCenterMenuItem);
            _menu.Items.Add(stateFolder);
            _menu.Items.Add(logsFolder);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_startMinimizedMenuItem);
            _menu.Items.Add(_startWithWindowsMenuItem);
            _menu.Items.Add(_autoRandomizeMenuItem);
            _menu.Items.Add(about);
            _menu.Items.Add(_exitMenuItem);
        }

        private void WireFormEvents()
        {
            _form.AdapterChanged += (sender, args) =>
            {
                UpdateFormBackupState();
                RefreshFormPresets();
            };
            _form.DhcpIpConsentChanged += (sender, args) => RefreshMenus();
            _form.RefreshRequested += async (sender, args) => await SafeRefreshAsync();
            _form.RandomizeBothRequested += async (sender, args) =>
                await RandomizeAsync(_form.SelectedAdapter, true, true);
            _form.RandomizeMacRequested += async (sender, args) =>
                await RandomizeAsync(_form.SelectedAdapter, true, false);
            _form.ManualMacRequested += async (sender, args) =>
                await ManualMacAsync(_form.SelectedAdapter, _form.ManualMacValue);
            _form.RestorePermanentRequested += async (sender, args) =>
                await RestorePermanentMacAsync(_form.SelectedAdapter);
            _form.RandomizeIpRequested += async (sender, args) =>
                await RandomizeAsync(_form.SelectedAdapter, false, true);
            _form.RestoreRequested += async (sender, args) =>
            {
                AdapterInfo adapter = _form.SelectedAdapter;
                AdapterBackup backup = adapter == null ? null : FindBackup(adapter.Key);
                if (backup == null)
                {
                    ShowError(new InvalidOperationException("There is no saved MacRando restore profile for this adapter."));
                    return;
                }

                await RestoreAsync(backup);
            };
            _form.ConnectVpnRequested += async (sender, args) =>
                await VpnActionAsync(_form.SelectedVpn, false);
            _form.DisconnectVpnRequested += async (sender, args) =>
                await VpnActionAsync(_form.SelectedVpn, true);
            _form.SavePresetRequested += (sender, args) => SavePreset();
            _form.ApplyPresetRequested += async (sender, args) => await ApplyPresetAsync();
            _form.StartupRandomizeToggleRequested += (sender, args) => ToggleStartupRandomization();
            _form.NotificationCenterRequested += (sender, args) => ShowNotificationCenter();
            _form.DarkModeChanged += (sender, args) =>
            {
                if (_notificationCenterForm != null && !_notificationCenterForm.IsDisposed)
                {
                    _notificationCenterForm.ApplyTheme(_form.DarkModeEnabled);
                }
            };
        }

        private void StartInitialRefresh(object sender, EventArgs e)
        {
            Application.Idle -= StartInitialRefresh;
            if (!_startupLaunch && !_settings.StartMinimized)
            {
                _form.ShowDashboard();
            }
            else
            {
                AppLogger.Info("Starting in the notification area; no network-changing action will run.");
            }
            InitialRefresh();
        }

        private async void InitialRefresh()
        {
            SetBusy(true);
            bool refreshSucceeded = false;
            try
            {
                await RefreshAllAsync(true);
                refreshSucceeded = true;
                AppLogger.Info("Initial refresh completed.");
            }
            catch (Exception error)
            {
                AppLogger.Error("Initial refresh failed.", error);
                ShowError(error);
            }
            finally
            {
                SetBusy(false);
            }

            if (refreshSucceeded)
            {
                await RunStartupRandomizationIfEnabledAsync();
                ShowNotification("MacRando is running", "Right-click the tray icon or open the dashboard.", ToolTipIcon.Info);
            }
        }

        private async Task RunStartupRandomizationIfEnabledAsync()
        {
            if (_startupRandomizeAttempted || !_settings.AutoRandomizeMacOnStartup || string.IsNullOrWhiteSpace(_settings.AutoRandomizeAdapterKey))
            {
                return;
            }
            _startupRandomizeAttempted = true;
            if (_startupRandomizeBlocked || _state.PendingOperation != null)
            {
                _form.SetStatus("Startup MAC randomization was skipped because a previous operation needs recovery.");
                ShowNotification("Startup MAC randomization skipped", "A previous network operation needs recovery.", ToolTipIcon.Warning);
                return;
            }

            AdapterInfo target = null;
            foreach (AdapterInfo adapter in _adapters)
            {
                if (string.Equals(adapter.Key, _settings.AutoRandomizeAdapterKey, StringComparison.OrdinalIgnoreCase))
                {
                    target = adapter;
                    break;
                }
            }
            if (target == null)
            {
                _form.SetStatus("Startup MAC randomization was skipped because its adapter is unavailable.");
                ShowNotification("Startup MAC randomization skipped", "The configured adapter is unavailable.", ToolTipIcon.Warning);
                AppLogger.Warning("Startup MAC randomization skipped: configured adapter is unavailable.");
                return;
            }
            if (FindBackup(target.Key) != null)
            {
                _form.SetStatus("Startup MAC randomization was skipped because a restore profile is already pending.");
                ShowNotification("Startup MAC randomization skipped", "A restore profile is already pending for this adapter.", ToolTipIcon.Warning);
                AppLogger.Warning("Startup MAC randomization skipped: restore profile already exists.");
                return;
            }
            if (!target.MacPropertySupported)
            {
                _form.SetStatus("Startup MAC randomization was skipped because the adapter does not support NetworkAddress.");
                ShowNotification("Startup MAC randomization skipped", "The selected adapter does not expose NetworkAddress.", ToolTipIcon.Warning);
                AppLogger.Warning("Startup MAC randomization skipped: NetworkAddress is unavailable.");
                return;
            }

            RecordNotification(
                "Startup MAC randomization",
                "Starting the explicitly enabled startup action for " + target.Name + ".",
                NotificationKinds.Info,
                target.Key,
                "startup MAC randomization",
                false,
                false,
                null);
            AppLogger.Info("Applying explicitly enabled startup MAC randomization for adapter " + AppLogger.MaskMac(target.MacAddress) + ".");
            await ChangeNetworkAsync(target, true, false, null, true, true);
        }

        private async Task SafeRefreshAsync()
        {
            if (_busy || _confirmationOpen)
            {
                return;
            }

            SetBusy(true);
            try
            {
                await RefreshAllAsync(true);
                AppLogger.Info("Manual refresh completed.");
            }
            catch (Exception error)
            {
                AppLogger.Error("Manual refresh failed.", error);
                ShowError(error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RunDiagnosticsAsync()
        {
            if (_busy || _confirmationOpen)
            {
                return;
            }

            SetBusy(true);
            _form.SetStatus("Running read-only diagnostics...");
            try
            {
                DiagnosticReport report = await _diagnostics.RunAsync(_form.SelectedAdapter);
                string text = report.ToDisplayText();
                AppLogger.Info("Read-only diagnostics completed. " + string.Join(" | ", report.Checks.ToArray()));
                try
                {
                    File.WriteAllText(
                        Path.Combine(_stateStore.DataDirectory, "diagnostics-latest.txt"),
                        text,
                        new UTF8Encoding(false));
                }
                catch
                {
                }
                ShowDiagnosticsReport(text);
                _form.SetStatus("Read-only diagnostics completed.");
            }
            catch (Exception error)
            {
                AppLogger.Error("Read-only diagnostics failed.", error);
                _form.SetStatus("Diagnostics failed: " + AppLogger.Sanitize(error.Message));
                ShowError(error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowDiagnosticsReport(string text)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "MacRando read-only diagnostics";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(760, 520);
                dialog.MinimumSize = new Size(520, 360);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                var output = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill,
                    Text = text
                };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
                var copy = new Button { Text = "Copy report", AutoSize = true };
                copy.Click += (sender, args) =>
                {
                    try
                    {
                        Clipboard.SetText(text);
                        copy.Text = "Copied";
                    }
                    catch
                    {
                        copy.Text = "Copy failed";
                    }
                };
                buttons.Controls.Add(close);
                buttons.Controls.Add(copy);
                layout.Controls.Add(output, 0, 0);
                layout.Controls.Add(buttons, 0, 1);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = close;
                dialog.ShowDialog(_form);
            }
        }

        private async Task RefreshAllAsync(bool refreshPublicIp)
        {
            try
            {
                await RefreshAllCoreAsync(refreshPublicIp);
                _form.SetAdapterDataStale(false);
                RefreshMenus();
            }
            catch
            {
                _form.SetAdapterDataStale(true);
                RefreshMenus();
                throw;
            }
        }

        private async Task RefreshAllCoreAsync(bool refreshPublicIp)
        {
            _stateLoadedSuccessfully = false;
            AppState loadedState = _stateStore.Load();
            _state = loadedState;
            _stateLoadedSuccessfully = true;
            _startupRandomizeBlocked = loadedState.PendingOperation != null;
            RefreshNotificationCenter();
            List<AdapterInfo> adapters = await _network.GetAdaptersAsync();
            List<VpnProfile> vpnProfiles;
            bool vpnRefreshFailed = false;
            try
            {
                vpnProfiles = await _network.GetVpnProfilesAsync() ?? new List<VpnProfile>();
            }
            catch
            {
                vpnRefreshFailed = true;
                vpnProfiles = new List<VpnProfile>(_vpnProfiles ?? new List<VpnProfile>());
            }

            string publicIp = "Unavailable";
            if (refreshPublicIp)
            {
                try
                {
                    publicIp = await _network.GetPublicIpAsync();
                }
                catch
                {
                    publicIp = "Unavailable";
                }
            }
            else
            {
                publicIp = _publicIp;
            }

            _adapters = adapters ?? new List<AdapterInfo>();
            _vpnProfiles = vpnProfiles ?? new List<VpnProfile>();
            _vpnProfilesStale = vpnRefreshFailed;
            _publicIp = publicIp;

            _form.SetAdapters(_adapters);
            _form.SetVpnProfiles(_vpnProfiles);
            RefreshFormPresets();
            UpdateFormBackupState();
            _form.SetPublicIp(_publicIp);
            string status = IsAdministrator()
                ? "Ready. Changes restart the selected adapter and may briefly disconnect it."
                : "Administrator permission is required for network changes.";
            if (vpnRefreshFailed)
            {
                status += " VPN profiles could not be refreshed; showing the last known list.";
            }
            if (_startupRandomizeBlocked)
            {
                status += " A previous network operation needs recovery; automatic startup randomization is paused.";
            }
            _form.SetStatus(status);
            RefreshMenus();
        }

        private Task RestorePermanentMacAsync(AdapterInfo adapter)
        {
            if (adapter == null || string.IsNullOrWhiteSpace(adapter.PermanentMacAddress))
            {
                ShowError(new InvalidOperationException("The selected adapter does not expose a permanent MAC address."));
                return Task.FromResult(0);
            }

            if (string.Equals(
                NetworkService.NormalizeMac(adapter.MacAddress),
                NetworkService.NormalizeMac(adapter.PermanentMacAddress),
                StringComparison.OrdinalIgnoreCase))
            {
                _form.SetStatus("The adapter is already using its permanent MAC address.");
                return Task.FromResult(0);
            }

            return ManualMacAsync(adapter, adapter.PermanentMacAddress);
        }

        private Task RandomizeAsync(AdapterInfo adapter, bool changeMac, bool changeIp)
        {
            return ChangeNetworkAsync(adapter, changeMac, changeIp, null, true);
        }

        private Task ManualMacAsync(AdapterInfo adapter, string requestedMac)
        {
            return ChangeNetworkAsync(adapter, true, false, requestedMac, false);
        }

        private NotificationPopup BeginProgressNotification(AdapterInfo adapter, string action)
        {
            return BeginProgressNotification(
                adapter == null ? string.Empty : adapter.Key,
                adapter == null ? "the adapter" : adapter.Name,
                action);
        }

        private NotificationPopup BeginProgressNotification(string adapterKey, string adapterName, string action)
        {
            return ShowNotificationCore(
                "MacRando: " + action,
                "Preparing " + (string.IsNullOrWhiteSpace(adapterName) ? "the adapter" : adapterName) + "...",
                NotificationKinds.Info,
                adapterKey ?? string.Empty,
                Guid.NewGuid().ToString("N"),
                "Open dashboard",
                false,
                null,
                false,
                null,
                true,
                0,
                false,
                false,
                false);
        }

        private void UpdateProgressNotification(NotificationPopup popup, string title, string message, string severity)
        {
            if (popup == null || popup.IsDisposed)
            {
                return;
            }
            popup.UpdateContent(title, message, severity, _form.DarkModeEnabled);
            popup.SetPersistent(true, 0);
            RepositionNotifications();
        }

        private void CompleteProgressNotification(
            NotificationPopup popup,
            string adapterKey,
            string title,
            string message,
            string severity,
            string action,
            bool canRestore,
            Func<Task> restoreAction,
            bool canRetry,
            Func<Task> retryAction)
        {
            adapterKey = adapterKey ?? string.Empty;
            if (popup != null && !popup.IsDisposed)
            {
                popup.Configure(
                    popup.NotificationId,
                    adapterKey,
                    canRestore,
                    canRetry,
                    restoreAction,
                    retryAction,
                    false,
                    Math.Max(2000, _settings.NotificationDurationSeconds * 1000));
                popup.UpdateContent(title, message, severity, _form.DarkModeEnabled);
                popup.SetPersistent(false, Math.Max(2000, _settings.NotificationDurationSeconds * 1000));
                RepositionNotifications();
            }
            RecordNotification(title, message, severity, adapterKey, action, canRestore, canRetry, null);
            PlayNotificationSound(severity);
        }

        private void FailProgressNotification(
            NotificationPopup popup,
            string adapterKey,
            string title,
            string message,
            string action,
            bool canRestore,
            Func<Task> restoreAction,
            bool canRetry,
            Func<Task> retryAction)
        {
            adapterKey = adapterKey ?? string.Empty;
            string safeMessage = AppLogger.Sanitize(message);
            if (popup != null && !popup.IsDisposed)
            {
                popup.Configure(
                    popup.NotificationId,
                    adapterKey,
                    canRestore,
                    canRetry,
                    restoreAction,
                    retryAction,
                    true,
                    0);
                popup.UpdateContent(title, safeMessage, NotificationKinds.Critical, _form.DarkModeEnabled);
                popup.SetPersistent(true, 0);
                RepositionNotifications();
            }
            RecordNotification(title, safeMessage, NotificationKinds.Critical, adapterKey, action, canRestore, canRetry, safeMessage);
            PlayNotificationSound(NotificationKinds.Critical);
        }

        private async Task RetryNetworkOperationAsync(
            AdapterInfo adapter,
            bool changeMac,
            bool changeIp,
            string requestedMac,
            bool generateRandomMac,
            bool automated)
        {
            AdapterBackup backup = FindBackup(adapter == null ? null : adapter.Key);
            if (backup != null)
            {
                await RestoreAsync(backup, false);
                return;
            }
            await ChangeNetworkAsync(adapter, changeMac, changeIp, requestedMac, generateRandomMac, automated);
        }

        private async Task ChangeNetworkAsync(
            AdapterInfo adapter,
            bool changeMac,
            bool changeIp,
            string requestedMac,
            bool generateRandomMac,
            bool automated = false)
        {
            if (_busy || _confirmationOpen)
            {
                return;
            }

            if (adapter == null)
            {
                ShowError(new InvalidOperationException("Select a physical network adapter first."));
                return;
            }

            if (!automated)
            {
                _form.ShowDashboard();
            }

            if (!changeMac && !changeIp)
            {
                return;
            }

            if (changeIp && adapter.DhcpEnabled && !HasDhcpConsent(adapter))
            {
                ShowError(new InvalidOperationException(
                    "This adapter is using DHCP. Select it and enable the DHCP IP randomization checkbox only if you understand the collision risk."));
                return;
            }

            string validatedManualMac = null;
            if (changeMac && !generateRandomMac)
            {
                try
                {
                    validatedManualMac = ValidateManualMac(requestedMac);
                }
                catch (Exception error)
                {
                    ShowError(error);
                    return;
                }
            }

            string action = changeMac && changeIp
                ? "randomize the MAC and local IPv4 address"
                : (changeMac
                    ? (generateRandomMac ? "randomize the MAC address" : "apply the entered MAC address")
                    : "randomize the local IPv4 address");
            DialogResult confirmation = DialogResult.Yes;
            if (!automated)
            {
                _confirmationOpen = true;
                RefreshMenus();
                try
                {
                    confirmation = MessageBox.Show(
                        _form,
                        "MacRando will " + action + " on '" + adapter.Name + "'.\n\n" +
                        "The adapter will restart and the connection may briefly drop. A restore profile will be saved." +
                        (changeIp ? " Local IP randomization is best-effort and cannot guarantee protection from DHCP or device conflicts." : string.Empty) +
                        "\n\nContinue?",
                        "Confirm network change",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                }
                finally
                {
                    _confirmationOpen = false;
                    RefreshMenus();
                }
            }

            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            if (!EnsureAdministrator())
            {
                return;
            }

            AppLogger.Info("Network change requested: adapter=" + AppLogger.MaskMac(adapter.MacAddress) + ", mac=" + changeMac + ", ip=" + changeIp);
            SetBusy(true);
            NotificationPopup progressPopup = BeginProgressNotification(adapter, action);
            Exception operationError = null;
            bool changeVerified = false;
            bool refreshFailed = false;
            try
            {
                _form.SetStatus(changeMac && changeIp
                    ? "Reading adapter and selecting an apparently unused local address..."
                    : (changeMac
                        ? (generateRandomMac ? "Selecting a random MAC address..." : "Applying the entered MAC address...")
                        : "Selecting an apparently unused local IPv4 address..."));

                NetworkState state = await _network.GetStateAsync(adapter);
                UpdateProgressNotification(
                    progressPopup,
                    "MacRando: " + action,
                    "Adapter state read. Selecting the new configuration...",
                    NotificationKinds.Info);
                if (changeIp && state.DhcpEnabled && !HasDhcpConsent(adapter))
                {
                    throw new InvalidOperationException(
                        "This adapter is using DHCP. Select it and enable the DHCP IP randomization checkbox only if you understand the collision risk.");
                }

                if (changeMac && !state.MacPropertySupported)
                {
                    throw new NotSupportedException(
                        "This adapter does not expose a configurable Network Address property. " +
                        "Use Windows' built-in Wi-Fi/MAC randomization settings or choose another adapter.");
                }

                if (changeIp)
                {
                    if (!state.ConfigurationKnown || !state.DnsPolicyKnown)
                    {
                        throw new InvalidOperationException(
                            "The complete IP/DNS configuration could not be read; MacRando will not change it.");
                    }

                    if (!state.HasUsableIpv4 || state.PreferredAddressCount != 1)
                    {
                        throw new InvalidOperationException(
                            "MacRando can safely randomize this adapter only when it has one active IPv4 address. " +
                            "Connect the adapter or choose a different adapter.");
                    }
                }

                string newMac = changeMac
                    ? (generateRandomMac ? _network.CreateRandomMac() : validatedManualMac)
                    : null;
                string newIp = changeIp
                    ? (await Task.Run(() => _network.FindRandomLocalAddress(state))).ToString()
                    : null;

                AppState stateFile = _stateStore.Load();
                if (stateFile.PendingOperation != null)
                {
                    throw new InvalidOperationException(
                        "A previous MacRando operation was interrupted and needs recovery. Restore the saved adapter configuration before starting another change.");
                }
                AdapterBackup backup = FindBackupIn(stateFile, adapter.Key);
                if (backup == null)
                {
                    backup = new AdapterBackup
                    {
                        AdapterKey = adapter.Key,
                        AdapterName = adapter.Name,
                        AdapterDescription = adapter.Description,
                        InterfaceGuid = adapter.InterfaceGuid,
                        InterfaceIndex = state.InterfaceIndex,
                        OriginalMacAddress = state.CurrentMacAddress,
                        OriginalMacRegistryValue = state.MacOverrideValue ?? string.Empty,
                        OriginalMacOverrideValue = state.MacOverrideValue ?? string.Empty,
                        OriginalMacOverridePresent = state.MacOverridePresent,
                        OriginalMacDisplayValue = state.MacDisplayValue,
                        OriginalIpAddress = state.IpAddress,
                        OriginalPrefixLength = state.PrefixLength,
                        OriginalGateway = state.Gateway,
                        OriginalDhcpEnabled = state.DhcpEnabled,
                        OriginalIpAddresses = state.IpAddresses ?? new string[0],
                        OriginalStaticDnsServers = state.StaticDnsServers ?? new string[0],
                        OriginalDnsServers = state.DnsServers ?? new string[0],
                        SavedAtUtc = DateTime.UtcNow
                    };
                    stateFile.Backups[adapter.Key] = backup;
                }
                else
                {
                    // Keep the first saved values as the restore target if an adapter is randomized again.
                    backup.AdapterName = state.AdapterName;
                    backup.AdapterDescription = adapter.Description;
                    backup.InterfaceGuid = state.InterfaceGuid;
                    backup.InterfaceIndex = state.InterfaceIndex;
                }

                if (changeMac)
                {
                    backup.MacChanged = true;
                    backup.ModifiedMacAddress = newMac;
                }

                if (changeIp)
                {
                    backup.IpChanged = true;
                    backup.ModifiedIpAddress = newIp;
                }

                backup.LastOperationUtc = DateTime.UtcNow;
                stateFile.PendingOperation = new PendingOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    AdapterKey = adapter.Key,
                    Action = action,
                    Automatic = automated,
                    StartedAtUtc = DateTime.UtcNow
                };
                _stateStore.Save(stateFile);
                _state = stateFile;
                _changedAdapterKeysThisSession.Add(adapter.Key);
                UpdateFormBackupState();

                _form.SetStatus("Applying changes and restarting the adapter...");
                UpdateProgressNotification(
                    progressPopup,
                    "MacRando: " + action,
                    "Applying changes and restarting " + adapter.Name + "...",
                    NotificationKinds.Info);
                await _network.ApplyChangesAsync(adapter, state, newMac, newIp, changeMac, changeIp);
                UpdateProgressNotification(
                    progressPopup,
                    "MacRando: " + action,
                    "Changes applied. Verifying the adapter state...",
                    NotificationKinds.Info);
                await _network.VerifyAppliedAsync(adapter, state, newMac, newIp, changeMac, changeIp);
                changeVerified = true;
                ClearPendingOperation(adapter);
                try
                {
                    await RefreshAllAsync(true);
                }
                catch (Exception refreshError)
                {
                    refreshFailed = true;
                    _form.SetStatus("The change was verified, but the dashboard refresh failed.");
                    UpdateProgressNotification(
                        progressPopup,
                        "MacRando: " + action,
                        "The change was verified, but the dashboard refresh failed.",
                        NotificationKinds.Warning);
                    AppLogger.Error("Post-change dashboard refresh failed.", refreshError);
                }
                if (changeMac)
                {
                    _form.ClearManualMacInput();
                }

                string result = changeMac && changeIp
                    ? "MAC and local IPv4 randomized."
                    : (changeMac
                        ? (generateRandomMac ? "MAC randomized." : "MAC applied.")
                        : "Local IPv4 randomized.");
                _form.SetStatus(refreshFailed
                    ? "The change was verified, but the dashboard refresh failed."
                    : result + " Restore is available from the tray menu.");
                RecordOperation(adapter, action, automated, true, result);
                AppLogger.Info("Network change completed: " + result + (refreshFailed ? "; refresh failed" : string.Empty));
                AdapterBackup completedBackup = FindBackup(adapter.Key);
                Func<Task> restoreAction = completedBackup == null ? (Func<Task>)null : () => RestoreAsync(completedBackup, false);
                CompleteProgressNotification(
                    progressPopup,
                    adapter.Key,
                    "MacRando",
                    result,
                    refreshFailed ? NotificationKinds.Warning : NotificationKinds.Success,
                    action,
                    restoreAction != null,
                    restoreAction,
                    false,
                    null);
            }
            catch (Exception error)
            {
                operationError = error;
            }

            try
            {
                if (operationError != null)
                {
                    string failureStatus = changeVerified
                        ? "The change was verified, but the dashboard refresh failed."
                        : "The change did not complete. A saved restore profile may still be available.";
                    _form.SetStatus(failureStatus);
                    RecordOperation(adapter, action, automated, false, operationError.Message);
                    AdapterBackup failedBackup = FindBackup(adapter.Key);
                    Func<Task> failedRestoreAction = failedBackup == null ? (Func<Task>)null : () => RestoreAsync(failedBackup, false);
                    Func<Task> retryAction = () => RetryNetworkOperationAsync(adapter, changeMac, changeIp, requestedMac, generateRandomMac, automated);
                    FailProgressNotification(
                        progressPopup,
                        adapter.Key,
                        "MacRando needs attention",
                        operationError.Message,
                        action,
                        failedRestoreAction != null,
                        failedRestoreAction,
                        true,
                        retryAction);
                    if (progressPopup == null)
                    {
                        ShowError(operationError);
                    }
                    try
                    {
                        await RefreshAllAsync(false);
                    }
                    catch
                    {
                        // Preserve the original error shown to the user.
                    }

                    _form.SetStatus(failureStatus);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task<bool> RestoreAsync(AdapterBackup backup, bool askConfirmation = true, bool showDashboard = true)
        {
            if (_busy || _confirmationOpen || backup == null)
            {
                return false;
            }

            if (showDashboard)
            {
                _form.ShowDashboard();
            }
            DialogResult confirmation = DialogResult.Yes;
            if (askConfirmation)
            {
                _confirmationOpen = true;
                RefreshMenus();
                try
                {
                    confirmation = MessageBox.Show(
                        _form,
                        "Restore the saved MAC and IP configuration for '" + backup.DisplayName + "'?\n\n" +
                        "The adapter will restart and the connection may briefly drop.",
                        "Confirm restore",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);
                }
                finally
                {
                    _confirmationOpen = false;
                    RefreshMenus();
                }
            }

            if (confirmation != DialogResult.Yes)
            {
                return false;
            }

            if (!EnsureAdministrator())
            {
                return false;
            }

            AppLogger.Info("Restore requested for backup " + AppLogger.Sanitize(backup.DisplayName));
            SetBusy(true);
            NotificationPopup progressPopup = BeginProgressNotification(backup.AdapterKey, backup.AdapterName, "restore saved configuration");
            try
            {
                _form.SetStatus("Restoring the saved MAC and IP configuration...");
                UpdateProgressNotification(
                    progressPopup,
                    "MacRando: restoring adapter",
                    "Applying the saved MAC and IP configuration...",
                    NotificationKinds.Info);
                await _network.RestoreAsync(backup);
                UpdateProgressNotification(
                    progressPopup,
                    "MacRando: restoring adapter",
                    "Restore applied. Verifying the original configuration...",
                    NotificationKinds.Info);
                await _network.VerifyRestoreAsync(backup);
                AppState stateFile = _stateStore.Load();
                RemoveBackup(stateFile, backup);
                bool clearedPending = stateFile.PendingOperation == null ||
                    string.Equals(stateFile.PendingOperation.AdapterKey, backup.AdapterKey, StringComparison.OrdinalIgnoreCase);
                if (clearedPending)
                {
                    stateFile.PendingOperation = null;
                }
                _stateStore.Save(stateFile);
                _state = stateFile;
                if (clearedPending)
                {
                    _startupRandomizeBlocked = false;
                }
                _changedAdapterKeysThisSession.Remove(backup.AdapterKey);
                UpdateFormBackupState();
                bool refreshFailed = false;
                try
                {
                    await RefreshAllAsync(true);
                }
                catch (Exception refreshError)
                {
                    refreshFailed = true;
                    _form.SetStatus("Adapter restored, but the dashboard could not refresh.");
                    UpdateProgressNotification(
                        progressPopup,
                        "MacRando: restoring adapter",
                        "The adapter was restored, but the dashboard refresh failed.",
                        NotificationKinds.Warning);
                    AppLogger.Error("Post-restore dashboard refresh failed.", refreshError);
                }
                _form.SetStatus(refreshFailed
                    ? "Adapter restored, but the dashboard could not refresh."
                    : "The saved adapter configuration was restored.");
                RecordOperation(null, "restore saved configuration", false, true, "The saved adapter configuration was restored.");
                AppLogger.Info("Restore completed" + (refreshFailed ? "; refresh failed" : string.Empty));
                CompleteProgressNotification(
                    progressPopup,
                    backup.AdapterKey,
                    "MacRando",
                    refreshFailed ? "Adapter restored, but the dashboard could not refresh." : "Adapter restored.",
                    refreshFailed ? NotificationKinds.Warning : NotificationKinds.Success,
                    "restore saved configuration",
                    false,
                    null,
                    false,
                    null);
                return true;
            }
            catch (Exception error)
            {
                _form.SetStatus("Restore did not complete; the saved profile was kept.");
                Func<Task> restoreAction = () => RestoreAsync(backup, false);
                Func<Task> retryAction = () => RestoreAsync(backup, false);
                FailProgressNotification(
                    progressPopup,
                    backup.AdapterKey,
                    "MacRando needs attention",
                    error.Message,
                    "restore saved configuration",
                    true,
                    restoreAction,
                    true,
                    retryAction);
                if (progressPopup == null)
                {
                    ShowError(error);
                }
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task VpnActionAsync(VpnProfile profile, bool disconnect)
        {
            if (_busy || _confirmationOpen || profile == null)
            {
                return;
            }

            _form.ShowDashboard();
            AppLogger.Info("VPN action requested: " + (disconnect ? "disconnect" : "connect") + " " + AppLogger.Sanitize(profile.Name));
            SetBusy(true);
            NotificationPopup progressPopup = BeginProgressNotification(null, profile.Name, disconnect ? "disconnect VPN" : "connect VPN");
            bool actionSucceeded = false;
            bool refreshFailed = false;
            try
            {
                _form.SetStatus(disconnect ? "Disconnecting VPN..." : "Connecting VPN...");
                UpdateProgressNotification(
                    progressPopup,
                    "MacRando: " + (disconnect ? "disconnecting VPN" : "connecting VPN"),
                    "Waiting for Windows to update the VPN profile...",
                    NotificationKinds.Info);
                await _network.RunVpnActionAsync(profile, disconnect);
                actionSucceeded = true;
                UpdateProgressNotification(
                    progressPopup,
                    "MacRando: " + (disconnect ? "disconnecting VPN" : "connecting VPN"),
                    "VPN command completed. Refreshing dashboard and public IP...",
                    NotificationKinds.Info);
                // Give rasdial a moment to finish bringing the tunnel up or down.
                await Task.Delay(2500);
                try
                {
                    await RefreshAllAsync(true);
                }
                catch (Exception refreshError)
                {
                    refreshFailed = true;
                    UpdateProgressNotification(
                        progressPopup,
                        "MacRando: " + (disconnect ? "disconnecting VPN" : "connecting VPN"),
                        "VPN command completed, but the dashboard refresh failed.",
                        NotificationKinds.Warning);
                    AppLogger.Error("Post-VPN dashboard refresh failed.", refreshError);
                }

                _form.SetStatus(refreshFailed
                    ? "VPN action completed, but the dashboard refresh failed."
                    : (disconnect ? "VPN disconnected." : "VPN connected. Public IP refreshed."));
                AppLogger.Info("VPN action completed" + (refreshFailed ? "; refresh failed" : string.Empty));
                CompleteProgressNotification(
                    progressPopup,
                    string.Empty,
                    "MacRando",
                    disconnect ? "VPN disconnected." : "VPN connected.",
                    refreshFailed ? NotificationKinds.Warning : NotificationKinds.Success,
                    disconnect ? "disconnect VPN" : "connect VPN",
                    false,
                    null,
                    false,
                    null);
            }
            catch (Exception error)
            {
                _form.SetStatus(actionSucceeded
                    ? "VPN action completed, but the dashboard refresh failed."
                    : "The VPN action did not complete.");
                Func<Task> retryAction = () => VpnActionAsync(profile, disconnect);
                FailProgressNotification(
                    progressPopup,
                    string.Empty,
                    "MacRando needs attention",
                    error.Message,
                    disconnect ? "disconnect VPN" : "connect VPN",
                    false,
                    null,
                    true,
                    retryAction);
                if (progressPopup == null)
                {
                    ShowError(error);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ToggleStartMinimized()
        {
            bool previous = _settings.StartMinimized;
            _settings.StartMinimized = !previous;
            try
            {
                _settingsStore.Save(_settings);
                _startMinimizedMenuItem.Checked = _settings.StartMinimized;
                AppLogger.Info("Start minimized setting changed to " + _settings.StartMinimized + ".");
            }
            catch (Exception error)
            {
                _settings.StartMinimized = previous;
                _startMinimizedMenuItem.Checked = previous;
                AppLogger.Error("Could not save startup minimized setting.", error);
                ShowError(error);
            }
        }

        private void ToggleStartWithWindows()
        {
            bool enabled = !_settings.StartWithWindows;
            try
            {
                SetStartupRegistration(enabled);
                _settings.StartWithWindows = enabled;
                _settingsStore.Save(_settings);
                _startWithWindowsMenuItem.Checked = enabled;
                AppLogger.Info("Start with Windows setting changed to " + enabled + ".");
            }
            catch (Exception error)
            {
                _startWithWindowsMenuItem.Checked = _settings.StartWithWindows;
                AppLogger.Error("Could not change Start with Windows registration.", error);
                ShowError(error);
            }
        }

        private void ToggleStartupRandomization()
        {
            if (_settings.AutoRandomizeMacOnStartup)
            {
                _settings.AutoRandomizeMacOnStartup = false;
                _settings.AutoRandomizeAdapterKey = null;
                try
                {
                    _settingsStore.Save(_settings);
                    _autoRandomizeMenuItem.Checked = false;
                    _form.SetStartupRandomizationEnabled(false);
                    AppLogger.Info("Startup MAC randomization disabled.");
                }
                catch (Exception error)
                {
                    _settings.AutoRandomizeMacOnStartup = true;
                    _autoRandomizeMenuItem.Checked = true;
                    _form.SetStartupRandomizationEnabled(true);
                    ShowError(error);
                }
                return;
            }

            AdapterInfo adapter = _form.SelectedAdapter;
            if (adapter == null)
            {
                ShowError(new InvalidOperationException("Select the adapter that should be randomized on startup first."));
                return;
            }
            if (_startupRandomizeBlocked || _state.PendingOperation != null)
            {
                ShowError(new InvalidOperationException("Restore the pending adapter operation before enabling startup randomization."));
                return;
            }
            if (!adapter.MacPropertySupported)
            {
                ShowError(new NotSupportedException("The selected adapter does not expose a configurable NetworkAddress property."));
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                _form,
                "This will randomize the MAC address of '" + adapter.Name + "' every time MacRando starts.\n\n" +
                "The change requires administrator access and may interrupt the connection. " +
                "MacRando skips automatic randomization when a restore profile is already pending.\n\nEnable this behavior?",
                "Enable startup MAC randomization?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            _settings.AutoRandomizeMacOnStartup = true;
            _settings.AutoRandomizeAdapterKey = adapter.Key;
            try
            {
                _settingsStore.Save(_settings);
                _autoRandomizeMenuItem.Checked = true;
                _form.SetStartupRandomizationEnabled(true);
                AppLogger.Warning("Startup MAC randomization explicitly enabled for adapter " + AppLogger.MaskMac(adapter.MacAddress) + ".");
            }
            catch (Exception error)
            {
                _settings.AutoRandomizeMacOnStartup = false;
                _settings.AutoRandomizeAdapterKey = null;
                _autoRandomizeMenuItem.Checked = false;
                _form.SetStartupRandomizationEnabled(false);
                ShowError(error);
            }
        }

        private static void SetStartupRegistration(bool enabled)
        {
            const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, true) ??
                Registry.CurrentUser.CreateSubKey(runKeyPath))
            {
                if (enabled)
                {
                    string executable = Application.ExecutablePath;
                    key.SetValue("MacRando", "\"" + executable + "\" --startup", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue("MacRando", false);
                }
            }
        }

        private static bool IsStartupRegistrationPresent()
        {
            try
            {
                const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, false))
                {
                    return key != null && key.GetValue("MacRando") != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private void RefreshMenus()
        {
            _statusMenuItem.Text = IsAdministrator()
                ? "Status: ready (administrator)"
                : "Status: administrator permission required";
            _publicIpMenuItem.Text = "Public IP: " + _publicIp;
            _notifyIcon.Text = TruncateNotifyText("MacRando - " + _publicIp);

            _adaptersMenu.DropDownItems.Clear();
            bool allowDhcpIp = _form.AllowDhcpIpRandomization;
            string consentAdapterKey = _form.DhcpConsentAdapterKey;
            if (_adapters.Count == 0)
            {
                _adaptersMenu.DropDownItems.Add(new ToolStripMenuItem("No physical network adapter") { Enabled = false });
            }
            else
            {
                foreach (AdapterInfo adapter in _adapters)
                {
                    AdapterInfo selectedAdapter = adapter;
                    ToolStripMenuItem adapterMenu = new ToolStripMenuItem(TruncateMenuText(selectedAdapter.ToString(), 72));
                    string address = string.IsNullOrWhiteSpace(selectedAdapter.IpAddress)
                        ? "no IPv4"
                        : selectedAdapter.IpAddress;
                    string status = string.IsNullOrWhiteSpace(selectedAdapter.Status) ? "unknown" : selectedAdapter.Status;
                    string permanentMac = string.IsNullOrWhiteSpace(selectedAdapter.PermanentMacAddress)
                        ? "unknown"
                        : NetworkService.FormatMac(selectedAdapter.PermanentMacAddress);
                    adapterMenu.DropDownItems.Add(new ToolStripMenuItem(
                        TruncateMenuText("Status: " + status + "    IPv4: " + address, 92)) { Enabled = false });
                    adapterMenu.DropDownItems.Add(new ToolStripMenuItem(
                        TruncateMenuText("Current MAC: " + selectedAdapter.MacAddress + "    Permanent: " + permanentMac, 112)) { Enabled = false });
                    adapterMenu.DropDownItems.Add(new ToolStripSeparator());
                    bool allowDhcpForAdapter = allowDhcpIp && string.Equals(consentAdapterKey, selectedAdapter.Key, StringComparison.OrdinalIgnoreCase);
                    bool localIpEligible = DashboardForm.IsLocalIpEligible(selectedAdapter, allowDhcpForAdapter);
                    ToolStripMenuItem both = new ToolStripMenuItem("Randomize MAC + local IP");
                    both.Click += async (sender, args) => await RandomizeAsync(selectedAdapter, true, true);
                    both.Enabled = !_form.AdapterDataStale && selectedAdapter.MacPropertySupported && localIpEligible;
                    ToolStripMenuItem mac = new ToolStripMenuItem("Randomize MAC only");
                    mac.Click += async (sender, args) => await RandomizeAsync(selectedAdapter, true, false);
                    mac.Enabled = !_form.AdapterDataStale && selectedAdapter.MacPropertySupported;
                    ToolStripMenuItem ip = new ToolStripMenuItem("Randomize local IP only");
                    ip.Click += async (sender, args) => await RandomizeAsync(selectedAdapter, false, true);
                    ip.Enabled = !_form.AdapterDataStale && localIpEligible;
                    adapterMenu.DropDownItems.Add(both);
                    adapterMenu.DropDownItems.Add(mac);
                    adapterMenu.DropDownItems.Add(ip);
                    AdapterBackup backup = FindBackup(selectedAdapter.Key);
                    if (backup != null)
                    {
                        ToolStripMenuItem restore = new ToolStripMenuItem("Restore saved values");
                        restore.Click += async (sender, args) => await RestoreAsync(backup);
                        adapterMenu.DropDownItems.Add(restore);
                    }
                    _adaptersMenu.DropDownItems.Add(adapterMenu);
                }
            }

            _restoreMenu.DropDownItems.Clear();
            if (_state.Backups.Count == 0)
            {
                _restoreMenu.DropDownItems.Add(new ToolStripMenuItem("No saved adapter profiles") { Enabled = false });
            }
            else
            {
                foreach (AdapterBackup backup in _state.Backups.Values)
                {
                    AdapterBackup selectedBackup = backup;
                    ToolStripMenuItem restore = new ToolStripMenuItem(TruncateMenuText(selectedBackup.DisplayName, 72));
                    restore.Click += async (sender, args) => await RestoreAsync(selectedBackup);
                    _restoreMenu.DropDownItems.Add(restore);
                }
            }

            _vpnMenu.Text = _vpnProfilesStale ? "Windows VPN profiles (last known)" : "Windows VPN profiles";
            _vpnMenu.DropDownItems.Clear();
            if (_vpnProfiles.Count == 0)
            {
                _vpnMenu.DropDownItems.Add(new ToolStripMenuItem("No Windows VPN profiles configured") { Enabled = false });
            }
            else
            {
                foreach (VpnProfile profile in _vpnProfiles)
                {
                    VpnProfile selectedProfile = profile;
                    ToolStripMenuItem profileMenu = new ToolStripMenuItem(TruncateMenuText(selectedProfile.ToString(), 72));
                    ToolStripMenuItem connect = new ToolStripMenuItem("Connect");
                    connect.Click += async (sender, args) => await VpnActionAsync(selectedProfile, false);
                    ToolStripMenuItem disconnect = new ToolStripMenuItem("Disconnect");
                    disconnect.Click += async (sender, args) => await VpnActionAsync(selectedProfile, true);
                    profileMenu.DropDownItems.Add(connect);
                    profileMenu.DropDownItems.Add(disconnect);
                    _vpnMenu.DropDownItems.Add(profileMenu);
                }
            }

            bool interactionBusy = _busy || _confirmationOpen;
            _adaptersMenu.Enabled = _adapters.Count > 0 && !_form.AdapterDataStale && !interactionBusy;
            _restoreMenu.Enabled = _state.Backups.Count > 0 && !interactionBusy;
            _vpnMenu.Enabled = _vpnProfiles.Count > 0 && !interactionBusy;
            _refreshMenuItem.Enabled = !interactionBusy;
            _diagnosticsMenuItem.Enabled = !interactionBusy;
            int unreadNotifications = 0;
            if (_state.Notifications != null)
            {
                foreach (NotificationHistoryEntry notification in _state.Notifications)
                {
                    if (notification != null && !notification.Acknowledged)
                    {
                        unreadNotifications++;
                    }
                }
            }
            _notificationCenterMenuItem.Text = unreadNotifications > 0
                ? "Notification center (" + unreadNotifications + ")"
                : "Notification center";
            _notificationCenterMenuItem.Enabled = !interactionBusy;
            _startMinimizedMenuItem.Enabled = !interactionBusy;
            _startWithWindowsMenuItem.Enabled = !interactionBusy;
            _autoRandomizeMenuItem.Enabled = !interactionBusy;
            _startMinimizedMenuItem.Checked = _settings.StartMinimized;
            _startWithWindowsMenuItem.Checked = _settings.StartWithWindows;
            _autoRandomizeMenuItem.Checked = _settings.AutoRandomizeMacOnStartup;
            _exitMenuItem.Enabled = !interactionBusy;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _form.SetBusy(busy);
            RefreshMenus();
        }

        private bool HasDhcpConsent(AdapterInfo adapter)
        {
            return adapter != null && _form.AllowDhcpIpRandomization &&
                string.Equals(_form.DhcpConsentAdapterKey, adapter.Key, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateFormBackupState()
        {
            AdapterInfo adapter = _form.SelectedAdapter;
            _form.IsBackupAvailable = adapter != null && FindBackup(adapter.Key) != null;
            _form.SetBackupAvailable(_form.IsBackupAvailable);
        }

        private void RecordOperation(AdapterInfo adapter, string action, bool automatic, bool success, string result)
        {
            try
            {
                if (_state.History == null)
                {
                    _state.History = new List<OperationHistoryEntry>();
                }
                _state.History.Add(new OperationHistoryEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    AdapterKey = adapter == null ? string.Empty : adapter.Key,
                    Action = action ?? string.Empty,
                    Automatic = automatic,
                    Success = success,
                    Result = AppLogger.Sanitize(result)
                });
                if (_state.History.Count > 100)
                {
                    _state.History.RemoveRange(0, _state.History.Count - 100);
                }
                _stateStore.Save(_state);
            }
            catch (Exception error)
            {
                AppLogger.Warning("Could not record operation history: " + error.Message);
            }
        }

        private void ClearPendingOperation(AdapterInfo adapter)
        {
            if (_state.PendingOperation == null)
            {
                return;
            }
            if (adapter != null && !string.IsNullOrWhiteSpace(_state.PendingOperation.AdapterKey) &&
                !string.Equals(_state.PendingOperation.AdapterKey, adapter.Key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _state.PendingOperation = null;
            _startupRandomizeBlocked = false;
            _stateStore.Save(_state);
        }

        private void RefreshFormPresets()
        {
            AdapterInfo adapter = _form.SelectedAdapter;
            var presets = new List<AdapterPreset>();
            if (adapter != null && _state != null && _state.Presets != null)
            {
                string prefix = adapter.Key + "::";
                foreach (AdapterPreset preset in _state.Presets.Values)
                {
                    if (preset != null && !string.IsNullOrWhiteSpace(preset.PresetKey) &&
                        preset.PresetKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        presets.Add(preset);
                    }
                }
            }
            _form.SetPresets(presets);
        }

        private void SavePreset()
        {
            AdapterInfo adapter = _form.SelectedAdapter;
            if (adapter == null)
            {
                return;
            }

            AdapterPreset edited = ShowPresetEditor("Preset " + (_state.Presets.Count + 1));
            if (edited == null)
            {
                return;
            }

            string presetKey = adapter.Key + "::" + edited.Name;
            edited.PresetKey = presetKey;
            edited.UpdatedAtUtc = DateTime.UtcNow;
            _state.Presets[presetKey] = edited;
            try
            {
                _stateStore.Save(_state);
                RefreshFormPresets();
                _form.SetStatus("Preset saved for " + adapter.Name + ".");
                AppLogger.Info("Preset saved for adapter " + AppLogger.MaskMac(adapter.MacAddress) + ".");
            }
            catch (Exception error)
            {
                AppLogger.Error("Could not save preset.", error);
                ShowError(error);
            }
        }

        private async Task ApplyPresetAsync()
        {
            AdapterInfo adapter = _form.SelectedAdapter;
            string presetKey = _form.SelectedPresetKey;
            if (adapter == null || string.IsNullOrWhiteSpace(presetKey) || _state.Presets == null)
            {
                return;
            }

            AdapterPreset preset;
            if (!_state.Presets.TryGetValue(presetKey, out preset) || preset == null)
            {
                ShowError(new InvalidOperationException("The selected preset is no longer available."));
                RefreshFormPresets();
                return;
            }
            if (!preset.RandomizeMac && !preset.RandomizeIp)
            {
                ShowError(new InvalidOperationException("This preset has no enabled network action."));
                return;
            }

            await RandomizeAsync(adapter, preset.RandomizeMac, preset.RandomizeIp);
        }

        private AdapterPreset ShowPresetEditor(string defaultName)
        {
            using (var dialog = new Form())
            using (var name = new TextBox { Text = defaultName ?? string.Empty, Dock = DockStyle.Fill })
            using (var randomMac = new CheckBox { Text = "Randomize MAC address", Checked = true, AutoSize = true })
            using (var randomIp = new CheckBox { Text = "Randomize local IPv4 address", AutoSize = true })
            using (var warning = new Label { Text = "DHCP consent is never stored in a preset; you must opt in again for each operation.", AutoSize = true, ForeColor = Color.DimGray })
            {
                dialog.Text = "Save adapter preset";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(470, 210);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                layout.Controls.Add(new Label { Text = "Preset name", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                layout.Controls.Add(name, 0, 1);
                layout.Controls.Add(randomMac, 0, 2);
                layout.Controls.Add(randomIp, 0, 3);
                layout.Controls.Add(warning, 0, 4);
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.Add(save);
                buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 0, 5);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = save;
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(_form) != DialogResult.OK)
                {
                    return null;
                }

                string presetName = (name.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(presetName))
                {
                    MessageBox.Show(_form, "Enter a name for the preset.", "MacRando", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                if (!randomMac.Checked && !randomIp.Checked)
                {
                    MessageBox.Show(_form, "Select at least one action for the preset.", "MacRando", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                return new AdapterPreset
                {
                    Name = presetName,
                    RandomizeMac = randomMac.Checked,
                    RandomizeIp = randomIp.Checked,
                    AllowDhcpIpRandomization = false,
                    RestoreOnExit = false
                };
            }
        }

        private AdapterBackup FindBackup(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _state == null || _state.Backups == null)
            {
                return null;
            }

            return FindBackupIn(_state, key);
        }

        private static void RemoveBackup(AppState state, AdapterBackup backup)
        {
            if (state == null || state.Backups == null || backup == null)
            {
                return;
            }

            string keyToRemove = null;
            foreach (KeyValuePair<string, AdapterBackup> item in state.Backups)
            {
                if (object.ReferenceEquals(item.Value, backup) ||
                    string.Equals(item.Key, backup.AdapterKey, StringComparison.OrdinalIgnoreCase) ||
                    (item.Value != null && string.Equals(item.Value.AdapterKey, backup.AdapterKey, StringComparison.OrdinalIgnoreCase)))
                {
                    keyToRemove = item.Key;
                    break;
                }
            }

            if (keyToRemove != null)
            {
                state.Backups.Remove(keyToRemove);
            }
        }

        private static AdapterBackup FindBackupIn(AppState state, string key)
        {
            if (state == null || state.Backups == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            AdapterBackup exact;
            if (state.Backups.TryGetValue(key, out exact))
            {
                return exact;
            }

            foreach (KeyValuePair<string, AdapterBackup> item in state.Backups)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase) ||
                    (item.Value != null && string.Equals(item.Value.AdapterKey, key, StringComparison.OrdinalIgnoreCase)))
                {
                    return item.Value;
                }
            }

            return null;
        }

        private static string ValidateManualMac(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("Enter a MAC address first.");
            }

            var compact = new StringBuilder(12);
            foreach (char character in value.Trim())
            {
                if ((character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F'))
                {
                    compact.Append(character);
                }
                else if (character != '-' && character != ':' && !char.IsWhiteSpace(character))
                {
                    throw new FormatException("A MAC address may contain only hexadecimal digits, hyphens, colons, or spaces.");
                }
            }

            string normalized = compact.ToString().ToUpperInvariant();
            if (normalized.Length != 12)
            {
                throw new FormatException("A MAC address must contain exactly 12 hexadecimal digits.");
            }

            int firstOctet = Convert.ToInt32(normalized.Substring(0, 2), 16);
            if ((firstOctet & 1) != 0)
            {
                throw new FormatException("A MAC address must be unicast; its first octet must be even.");
            }

            return NetworkService.FormatMac(normalized);
        }

        private bool EnsureAdministrator()
        {
            if (IsAdministrator())
            {
                return true;
            }

            ShowError(new UnauthorizedAccessException(
                "MacRando must run as administrator to change adapter settings. Restart it with Run as administrator."));
            return false;
        }

        private static Icon LoadTrayIcon()
        {
            // Prefer the icon embedded in the executable. The external tray ICO
            // is retained as a fallback, but some Windows GDI+ versions render
            // PNG-compressed multi-size ICO entries incorrectly.
            try
            {
                Icon embedded = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (embedded != null)
                {
                    return embedded;
                }
            }
            catch
            {
            }
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MacRandoTray.ico");
                if (File.Exists(path))
                {
                    return new Icon(path, new Size(16, 16));
                }
            }
            catch
            {
            }
            using (Icon fallback = SystemIcons.Application)
            {
                return (Icon)fallback.Clone();
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private void OpenStateFolder()
        {
            try
            {
                string directory = Path.GetDirectoryName(_stateStore.StatePath);
                Directory.CreateDirectory(directory);
                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void OpenLogsFolder()
        {
            try
            {
                string directory = Path.GetDirectoryName(AppLogger.LogPath);
                Directory.CreateDirectory(directory);
                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                _form,
                AppInfo.ProductName + " " + AppInfo.DisplayVersion + "\n\n" +
                "TMAC-inspired adapter changer with random/manual MAC addresses, restore, local IP tools, and VPN support.\n\n" +
                "A local address is not your public Internet address. Use a Windows VPN profile to change the public address.\n\n" +
                "Restore profiles are encrypted for the current Windows user and stored under:\n" + _stateStore.StatePath,
                "About MacRando",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async Task ExitApplicationAsync()
        {
            if (_busy || _confirmationOpen)
            {
                return;
            }

            if (_changedAdapterKeysThisSession.Count > 0)
            {
                RecordNotification(
                    "Preparing automatic exit restore",
                    "Restoring saved adapter configuration before MacRando closes.",
                    NotificationKinds.Info,
                    string.Empty,
                    "automatic exit restore",
                    false,
                    false,
                    null);
                string[] keys = new string[_changedAdapterKeysThisSession.Count];
                _changedAdapterKeysThisSession.CopyTo(keys);
                foreach (string key in keys)
                {
                    AdapterBackup backup = FindBackup(key);
                    if (backup == null)
                    {
                        _changedAdapterKeysThisSession.Remove(key);
                        continue;
                    }

                    _form.SetStatus("Restoring the saved adapter configuration before exit...");
                    bool restored = await RestoreAsync(backup, false, false);
                    if (!restored)
                    {
                        _form.SetStatus("Exit cancelled because a restore did not complete.");
                        return;
                    }
                }
            }

            _form.CloseForExit();
            ExitThread();
        }

        private void ShowNotification(string title, string message, ToolTipIcon notificationType)
        {
            ShowNotification(title, message, notificationType, 0);
        }

        private void ShowNotification(string title, string message, ToolTipIcon notificationType, int timeoutMilliseconds)
        {
            ShowNotificationCore(
                title,
                message,
                ToNotificationSeverity(notificationType),
                null,
                null,
                "Open dashboard",
                false,
                null,
                false,
                null,
                false,
                timeoutMilliseconds,
                true,
                true,
                false);
        }

        private NotificationPopup ShowNotificationCore(
            string title,
            string message,
            string severity,
            string adapterKey,
            string notificationId,
            string actionLabel,
            bool canRestore,
            Func<Task> restoreAction,
            bool canRetry,
            Func<Task> retryAction,
            bool persistent,
            int timeoutMilliseconds,
            bool recordHistory,
            bool playSound,
            bool forceDisplay)
        {
            string normalizedSeverity = NotificationKinds.Normalize(severity);
            string safeTitle = AppLogger.Sanitize(title);
            if (string.IsNullOrWhiteSpace(safeTitle))
            {
                safeTitle = "MacRando";
            }
            string safeMessage = AppLogger.Sanitize(message);
            string safeAdapterKey = adapterKey ?? string.Empty;
            string safeNotificationId = string.IsNullOrWhiteSpace(notificationId)
                ? Guid.NewGuid().ToString("N")
                : notificationId;
            if (recordHistory)
            {
                RecordNotification(
                    safeTitle,
                    safeMessage,
                    normalizedSeverity,
                    safeAdapterKey,
                    actionLabel,
                    canRestore,
                    canRetry,
                    null);
            }
            if (!forceDisplay && !ShouldDisplayNotification(normalizedSeverity))
            {
                return null;
            }

            int effectiveTimeout = timeoutMilliseconds > 0
                ? timeoutMilliseconds
                : Math.Max(2000, _settings.NotificationDurationSeconds * 1000);
            if (_settings.NotificationCollapseDuplicates && !persistent)
            {
                foreach (NotificationPopup existing in _notificationPopups)
                {
                    if (existing == null || existing.IsDisposed)
                    {
                        continue;
                    }
                    if ((!string.IsNullOrWhiteSpace(safeNotificationId) &&
                         string.Equals(existing.NotificationId, safeNotificationId, StringComparison.OrdinalIgnoreCase)) ||
                        (string.Equals(existing.AdapterKey, safeAdapterKey, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(existing.TitleText, safeTitle, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(existing.MessageText, safeMessage, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Configure(
                            safeNotificationId,
                            safeAdapterKey,
                            canRestore,
                            canRetry,
                            restoreAction,
                            retryAction,
                            persistent,
                            effectiveTimeout);
                        existing.UpdateContent(safeTitle, safeMessage, normalizedSeverity, _form.DarkModeEnabled);
                        RepositionNotifications();
                        return existing;
                    }
                    if (!string.IsNullOrWhiteSpace(safeAdapterKey) &&
                        !NotificationKinds.IsCritical(normalizedSeverity) &&
                        string.Equals(existing.AdapterKey, safeAdapterKey, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.TitleText, safeTitle, StringComparison.OrdinalIgnoreCase) &&
                        !existing.IsPersistent)
                    {
                        existing.AppendGroupMessage(safeMessage);
                        existing.UpdateContent(existing.TitleText, existing.MessageText, normalizedSeverity, _form.DarkModeEnabled);
                        existing.SetPersistent(false, effectiveTimeout);
                        RepositionNotifications();
                        return existing;
                    }
                }
            }

            NotificationPopup popup = null;
            try
            {
                popup = new NotificationPopup(
                    _trayIcon,
                    safeTitle,
                    safeMessage,
                    _form.DarkModeEnabled,
                    normalizedSeverity,
                    effectiveTimeout,
                    persistent);
                popup.Configure(
                    safeNotificationId,
                    safeAdapterKey,
                    canRestore,
                    canRetry,
                    restoreAction,
                    retryAction,
                    persistent,
                    effectiveTimeout);
                NotificationPopup createdPopup = popup;
                popup.PopupClicked += (sender, args) => OpenAdapterFromNotification(createdPopup.AdapterKey);
                popup.ActionRequested += async (sender, args) => await HandlePopupActionAsync(createdPopup, args.Action);
                popup.PopupClosed += (sender, args) =>
                {
                    _notificationPopups.Remove(createdPopup);
                    RepositionNotifications();
                    createdPopup.Dispose();
                };
                _notificationPopups.Add(popup);
                RepositionNotifications();
                popup.Show();
                if (playSound)
                {
                    PlayNotificationSound(normalizedSeverity);
                }
                return popup;
            }
            catch
            {
                if (popup != null)
                {
                    _notificationPopups.Remove(popup);
                    popup.Dispose();
                }
                try
                {
                    _notifyIcon.ShowBalloonTip(
                        effectiveTimeout,
                        safeTitle,
                        TruncateBalloonText(safeMessage),
                        ToToolTipIcon(normalizedSeverity));
                }
                catch
                {
                    // A notification failure must never interrupt a network operation or exit workflow.
                }
                return null;
            }
        }

        private void RecordNotification(
            string title,
            string message,
            string severity,
            string adapterKey,
            string action,
            bool canRestore,
            bool canRetry,
            string details)
        {
            try
            {
                if (!_stateLoadedSuccessfully)
                {
                    AppLogger.Warning("Notification history was not persisted because restore state could not be loaded.");
                    return;
                }
                if (_state.Notifications == null)
                {
                    _state.Notifications = new List<NotificationHistoryEntry>();
                }
                _state.Notifications.Add(new NotificationHistoryEntry
                {
                    NotificationId = Guid.NewGuid().ToString("N"),
                    TimestampUtc = DateTime.UtcNow,
                    Title = AppLogger.Sanitize(title),
                    Message = AppLogger.Sanitize(message),
                    Severity = NotificationKinds.Normalize(severity),
                    AdapterKey = AppLogger.Sanitize(adapterKey),
                    Action = AppLogger.Sanitize(action),
                    Details = AppLogger.Sanitize(details),
                    CanRestore = canRestore,
                    CanRetry = canRetry,
                    Acknowledged = false
                });
                if (_state.Notifications.Count > 100)
                {
                    _state.Notifications.RemoveRange(0, _state.Notifications.Count - 100);
                }
                _stateStore.Save(_state);
                RefreshMenus();
                RefreshNotificationCenter();
            }
            catch (Exception error)
            {
                AppLogger.Warning("Could not record notification history: " + error.Message);
            }
        }

        private bool ShouldDisplayNotification(string severity)
        {
            if (!_settings.NotificationsEnabled)
            {
                return false;
            }
            if (_settings.NotificationQuietHoursEnabled &&
                !NotificationKinds.IsCritical(severity) &&
                IsQuietHours())
            {
                return false;
            }
            return true;
        }

        private bool IsQuietHours()
        {
            int start = Math.Max(0, Math.Min(23, _settings.NotificationQuietHoursStartHour));
            int end = Math.Max(0, Math.Min(23, _settings.NotificationQuietHoursEndHour));
            int now = DateTime.Now.Hour;
            if (start == end)
            {
                return false;
            }
            return start < end
                ? now >= start && now < end
                : now >= start || now < end;
        }

        private void PlayNotificationSound(string severity)
        {
            if (!_settings.NotificationSoundEnabled)
            {
                return;
            }
            if (!NotificationKinds.IsCritical(severity) && !ShouldDisplayNotification(severity))
            {
                return;
            }
            try
            {
                if (NotificationKinds.IsCritical(severity))
                {
                    SystemSounds.Hand.Play();
                }
                else if (severity == NotificationKinds.Warning)
                {
                    SystemSounds.Exclamation.Play();
                }
                else
                {
                    SystemSounds.Asterisk.Play();
                }
            }
            catch
            {
            }
        }

        private async Task HandlePopupActionAsync(NotificationPopup popup, NotificationPopupAction action)
        {
            if (popup == null)
            {
                return;
            }
            if (action == NotificationPopupAction.OpenDashboard)
            {
                OpenAdapterFromNotification(popup.AdapterKey);
                return;
            }
            if (action == NotificationPopupAction.Dismiss)
            {
                return;
            }
            if (action == NotificationPopupAction.Diagnostics)
            {
                OpenAdapterFromNotification(popup.AdapterKey);
                await RunDiagnosticsAsync();
                return;
            }
            Func<Task> callback = action == NotificationPopupAction.Restore
                ? popup.RestoreAction
                : popup.RetryAction;
            if (callback != null)
            {
                try
                {
                    await callback();
                }
                catch (Exception error)
                {
                    ShowError(error);
                }
            }
        }

        private void OpenAdapterFromNotification(string adapterKey)
        {
            if (!string.IsNullOrWhiteSpace(adapterKey))
            {
                _form.SelectAdapterByKey(adapterKey);
            }
            _form.ShowDashboard();
        }

        private static string ToNotificationSeverity(ToolTipIcon notificationType)
        {
            if (notificationType == ToolTipIcon.Error)
            {
                return NotificationKinds.Error;
            }
            if (notificationType == ToolTipIcon.Warning)
            {
                return NotificationKinds.Warning;
            }
            return NotificationKinds.Info;
        }

        private static ToolTipIcon ToToolTipIcon(string severity)
        {
            if (NotificationKinds.IsCritical(severity))
            {
                return ToolTipIcon.Error;
            }
            return NotificationKinds.Normalize(severity) == NotificationKinds.Warning
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info;
        }

        private void RepositionNotifications()
        {
            if (_notificationPopups.Count == 0)
            {
                return;
            }

            Rectangle workingArea;
            try
            {
                workingArea = Screen.FromControl(_form).WorkingArea;
            }
            catch
            {
                workingArea = Screen.PrimaryScreen.WorkingArea;
            }
            for (int index = 0; index < _notificationPopups.Count; index++)
            {
                NotificationPopup popup = _notificationPopups[index];
                if (popup != null && !popup.IsDisposed)
                {
                    popup.PositionAt(workingArea, index);
                }
            }
        }

        private void ShowNotificationCenter()
        {
            try
            {
                if (_notificationCenterForm == null || _notificationCenterForm.IsDisposed)
                {
                    _notificationCenterForm = new NotificationCenterForm(
                        _settings,
                        _state.Notifications,
                        _form.DarkModeEnabled);
                    _notificationCenterForm.OpenDashboardRequested += (sender, args) =>
                    {
                        if (args.Entry != null)
                        {
                            OpenAdapterFromNotification(args.Entry.AdapterKey);
                        }
                    };
                    _notificationCenterForm.RestoreRequested += async (sender, args) =>
                    {
                        await RestoreFromNotificationAsync(args.Entry);
                    };
                    _notificationCenterForm.RetryRequested += async (sender, args) =>
                    {
                        await RetryFromNotificationAsync(args.Entry);
                    };
                    _notificationCenterForm.DiagnosticsRequested += async (sender, args) =>
                    {
                        if (args.Entry != null)
                        {
                            OpenAdapterFromNotification(args.Entry.AdapterKey);
                        }
                        await RunDiagnosticsAsync();
                    };
                    _notificationCenterForm.ClearHistoryRequested += (sender, args) => ClearNotificationHistory();
                    _notificationCenterForm.RefreshRequested += (sender, args) => RefreshNotificationCenter();
                    _notificationCenterForm.SettingsChanged += (sender, args) => SaveNotificationSettings();
                }
                else
                {
                    _notificationCenterForm.SetEntries(_state.Notifications);
                }
                AcknowledgeNotifications();
                _notificationCenterForm.ApplyTheme(_form.DarkModeEnabled);
                _notificationCenterForm.ShowCentered(_form);
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void AcknowledgeNotifications()
        {
            if (_state.Notifications == null)
            {
                return;
            }
            bool changed = false;
            foreach (NotificationHistoryEntry notification in _state.Notifications)
            {
                if (notification != null && !notification.Acknowledged)
                {
                    notification.Acknowledged = true;
                    changed = true;
                }
            }
            if (changed)
            {
                _stateStore.Save(_state);
                RefreshMenus();
            }
        }

        private void RefreshNotificationCenter()
        {
            if (_notificationCenterForm != null && !_notificationCenterForm.IsDisposed)
            {
                _notificationCenterForm.SetEntries(_state.Notifications);
            }
        }

        private void ClearNotificationHistory()
        {
            try
            {
                if (_state.Notifications == null)
                {
                    _state.Notifications = new List<NotificationHistoryEntry>();
                }
                _state.Notifications.Clear();
                _stateStore.Save(_state);
                RefreshMenus();
                RefreshNotificationCenter();
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private void SaveNotificationSettings()
        {
            try
            {
                _settingsStore.Save(_settings);
                AppLogger.Info("Notification preferences updated.");
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        private async Task RestoreFromNotificationAsync(NotificationHistoryEntry entry)
        {
            if (entry == null)
            {
                return;
            }
            AdapterBackup backup = FindBackup(entry.AdapterKey);
            if (backup == null)
            {
                ShowError(new InvalidOperationException("No restore profile is available for this notification."));
                return;
            }
            if (backup.LastOperationUtc > entry.TimestampUtc.AddSeconds(2))
            {
                ShowError(new InvalidOperationException("This notification is stale; a newer adapter operation has replaced its restore action."));
                return;
            }
            await RestoreAsync(backup, false);
        }

        private async Task RetryFromNotificationAsync(NotificationHistoryEntry entry)
        {
            if (entry == null)
            {
                return;
            }
            if (entry.CanRestore)
            {
                await RestoreFromNotificationAsync(entry);
                return;
            }
            string action = entry.Action ?? string.Empty;
            bool changeMac = action.IndexOf("MAC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.IndexOf("startup", StringComparison.OrdinalIgnoreCase) >= 0;
            bool changeIp = action.IndexOf("IPv4", StringComparison.OrdinalIgnoreCase) >= 0 ||
                action.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0;
            AdapterInfo adapter = FindAdapterByKey(entry.AdapterKey);
            if (adapter == null || (!changeMac && !changeIp) || action.IndexOf("entered", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ShowError(new InvalidOperationException("This notification's retry action is no longer available. Open the dashboard to start a new operation."));
                return;
            }
            await ChangeNetworkAsync(adapter, changeMac, changeIp, null, true, false);
        }

        private AdapterInfo FindAdapterByKey(string adapterKey)
        {
            if (string.IsNullOrWhiteSpace(adapterKey))
            {
                return null;
            }
            foreach (AdapterInfo adapter in _adapters)
            {
                if (string.Equals(adapter.Key, adapterKey, StringComparison.OrdinalIgnoreCase))
                {
                    return adapter;
                }
            }
            return null;
        }

        private void ShowError(Exception error)
        {
            Exception actual = error;
            while (actual is AggregateException && actual.InnerException != null)
            {
                actual = actual.InnerException;
            }

            string message = actual.Message;
            AppLogger.Error("User-visible error.", actual);
            _form.SetStatus("Error: " + message);
            try
            {
                string adapterKey = _state.PendingOperation == null ? string.Empty : _state.PendingOperation.AdapterKey;
                AdapterBackup backup = FindBackup(adapterKey);
                Func<Task> restoreAction = backup == null ? (Func<Task>)null : () => RestoreAsync(backup, false);
                ShowNotificationCore(
                    "MacRando needs attention",
                    TruncateBalloonText(message),
                    NotificationKinds.Critical,
                    adapterKey,
                    null,
                    restoreAction == null ? "Open dashboard" : "Restore now",
                    restoreAction != null,
                    restoreAction,
                    false,
                    null,
                    true,
                    0,
                    true,
                    true,
                    true);
            }
            catch
            {
                // Error reporting must not mask the original operation failure.
            }
            if (_form.Visible)
            {
                MessageBox.Show(_form, message, "MacRando", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string TruncateNotifyText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "MacRando";
            }

            return value.Length <= 63 ? value : value.Substring(0, 63);
        }

        private static string TruncateMenuText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= maxLength ? value : value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private static string TruncateBalloonText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "An unknown error occurred.";
            }

            return value.Length <= 255 ? value : value.Substring(0, 252) + "...";
        }
    }
}
