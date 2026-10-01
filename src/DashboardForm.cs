using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MacRando
{
    internal sealed class DashboardForm : Form
    {
        private sealed class ResponsiveActionGrid : TableLayoutPanel
        {
            private readonly List<Control> _controls;
            private readonly int _preferredColumns;
            private int _currentColumns;

            public ResponsiveActionGrid(int preferredColumns, params Control[] controls)
            {
                _preferredColumns = Math.Max(1, preferredColumns);
                _controls = new List<Control>(controls ?? new Control[0]);
                Dock = DockStyle.Top;
                AutoSize = false;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
                Padding = Padding.Empty;
                Margin = Padding.Empty;
                _currentColumns = 0;
                Reflow();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Reflow();
            }

            private bool Fits(int columns, int available)
            {
                for (int start = 0; start < _controls.Count; start += columns)
                {
                    int required = 0;
                    int end = Math.Min(start + columns, _controls.Count);
                    for (int index = start; index < end; index++)
                    {
                        required += _controls[index].MinimumSize.Width + 8;
                    }
                    if (required > available)
                    {
                        return false;
                    }
                }
                return true;
            }

            private void Reflow()
            {
                int available = ClientSize.Width;
                int columns = _preferredColumns;
                if (_preferredColumns > 1 && available > 0)
                {
                    while (columns > 1 && !Fits(columns, available))
                    {
                        columns--;
                    }
                }
                if (columns == _currentColumns)
                {
                    return;
                }

                int rowHeight = 36;
                foreach (Control control in _controls)
                {
                    rowHeight = Math.Max(rowHeight, control.MinimumSize.Height);
                }

                _currentColumns = columns;
                SuspendLayout();
                try
                {
                    Controls.Clear();
                    ColumnStyles.Clear();
                    RowStyles.Clear();
                    ColumnCount = columns;
                    RowCount = (_controls.Count + columns - 1) / columns;
                    for (int column = 0; column < columns; column++)
                    {
                        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
                    }
                    for (int row = 0; row < RowCount; row++)
                    {
                        RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight + 8));
                    }
                    for (int index = 0; index < _controls.Count; index++)
                    {
                        Control control = _controls[index];
                        control.Dock = DockStyle.Fill;
                        control.Margin = new Padding(4);
                        Controls.Add(control, index % columns, index / columns);
                    }
                    int cellCount = RowCount * columns;
                    for (int index = _controls.Count; index < cellCount; index++)
                    {
                        Controls.Add(new Label { Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = Color.Transparent }, index % columns, index / columns);
                    }
                }
                finally
                {
                    ResumeLayout(true);
                }

                Height = Math.Max(1, RowCount * (rowHeight + 8));
            }
        }

        private readonly ListView _adapterList;
        private readonly TextBox _macTextBox;
        private readonly ComboBox _vpnCombo;
        private readonly CheckBox _allowDhcpIpCheckBox;
        private readonly CheckBox _showPublicIpLocationCheckBox;
        private readonly CheckBox _darkModeCheckBox;
        private readonly ToolTip _toolTip;

        private readonly Button _refreshButton;
        private readonly Button _randomizeBothButton;
        private readonly Button _randomizeMacButton;
        private readonly Button _applyMacButton;
        private readonly Button _restorePermanentButton;
        private readonly Button _randomizeIpButton;
        private readonly Button _restoreButton;
        private readonly Button _connectVpnButton;
        private readonly Button _disconnectVpnButton;
        private readonly ComboBox _presetCombo;
        private readonly Button _savePresetButton;
        private readonly Button _applyPresetButton;
        private readonly Button _startupRandomizeButton;
        private readonly Button _notificationCenterButton;
        private readonly Button _ipPreflightButton;

        private readonly Panel _headerPanel;
        private readonly Panel _listPanel;
        private readonly Panel _detailsPanel;
        private readonly Label _titleLabel;
        private readonly Label _subtitleLabel;
        private readonly Label _headerPublicIpLabel;
        private readonly Label _adapterCountLabel;
        private readonly Label _listHintLabel;
        private Label _licenseLabel;
        private TextBox _adapterSearchBox;
        private CheckBox _connectedOnlyCheckBox;
        private Button _favoriteButton;
        private Panel _pendingBanner;
        private Label _pendingBannerLabel;
        private Label _driverLabel;
        private Label _interfaceIndexLabel;
        private Label _macPropertyLabel;
        private Label _guidLabel;
        private List<AdapterInfo> _allAdapters = new List<AdapterInfo>();
        private List<string> _favoriteKeys = new List<string>();
        private bool _suppressViewEvents;
        // Not readonly: created by BuildRootLayout, which is a method rather than part of
        // the constructor, and is not rebuilt on theme changes.
        private Button _keepChangeButton;
        private readonly Label _selectedTitleLabel;
        private readonly Label _selectedStatusLabel;
        private readonly Label _currentMacLabel;
        private readonly Label _permanentMacLabel;
        private readonly Label _networkIpLabel;
        private readonly Label _networkModeLabel;
        private readonly Label _networkLinkLabel;
        private readonly Label _publicIpLabel;
        private readonly Label _vpnHintLabel;
        private readonly Label _operationStatusLabel;
        private readonly Label _safetySummaryLabel;

        private AdapterInfo _selectedAdapter;
        private bool _allowClose;
        private bool _suppressAdapterSelectionEvents;
        private bool _isBusy;
        private bool _adapterDataStale;
        private bool _darkMode;
        private Color _listInputColor;
        private Color _listHeaderColor;
        private Color _listTextColor;
        /// <summary>
        /// Secondary colour for adapter rows MacRando will not change, so an off-limits
        /// adapter is visibly off limits in the list rather than only in its tooltip.
        /// </summary>
        private Color _listMutedColor;
        private Color _listSelectedColor;
        private Color _listSelectedTextColor;
        private string _displayedAdapterKey;
        private string _consentAdapterKey;
        private readonly string _themePath;

        public event EventHandler AdapterChanged;
        public event EventHandler RefreshRequested;
        public event EventHandler RandomizeBothRequested;
        public event EventHandler RandomizeMacRequested;
        public event EventHandler ManualMacRequested;
        public event EventHandler RestorePermanentRequested;
        public event EventHandler KeepChangeRequested;
        public event EventHandler DhcpIpConsentChanged;
        public event EventHandler PublicIpLocationConsentChanged;
        public event EventHandler RandomizeIpRequested;
        public event EventHandler RestoreRequested;
        public event EventHandler ConnectVpnRequested;
        public event EventHandler DisconnectVpnRequested;
        public event EventHandler SavePresetRequested;
        public event EventHandler ApplyPresetRequested;
        public event EventHandler StartupRandomizeToggleRequested;
        public event EventHandler NotificationCenterRequested;
        public event EventHandler IpPreflightRequested;
        public event EventHandler DarkModeChanged;
        public event EventHandler AdapterViewChanged;
        public event EventHandler RestoreAllPendingRequested;

        public AdapterInfo SelectedAdapter
        {
            get { return _selectedAdapter; }
        }

        public VpnProfile SelectedVpn
        {
            get { return _vpnCombo.SelectedItem as VpnProfile; }
        }

        public string ManualMacValue
        {
            get { return _macTextBox == null ? string.Empty : _macTextBox.Text; }
        }

        public bool AllowDhcpIpRandomization
        {
            get { return _allowDhcpIpCheckBox != null && _allowDhcpIpCheckBox.Checked; }
        }

        public bool ShowPublicIpLocation
        {
            get { return _showPublicIpLocationCheckBox != null && _showPublicIpLocationCheckBox.Checked; }
        }

        public bool DarkModeEnabled
        {
            get { return _darkModeCheckBox != null && _darkModeCheckBox.Checked; }
        }

        public bool AdapterDataStale
        {
            get { return _adapterDataStale; }
        }

        public string DhcpConsentAdapterKey
        {
            get { return _consentAdapterKey; }
        }

        public bool IsBackupAvailable { get; set; }

        public string SelectedPresetKey
        {
            get
            {
                AdapterPreset preset = _presetCombo == null ? null : _presetCombo.SelectedItem as AdapterPreset;
                return preset == null ? null : preset.PresetKey;
            }
        }

        public DashboardForm()
        {
            Text = AppInfo.ProductName + " " + AppInfo.Version;
            Icon = LoadApplicationIcon();
            ClientSize = new Size(1120, 720);
            MinimumSize = new Size(980, 620);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _themePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MacRando",
                "theme.txt");
            _toolTip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 350,
                ReshowDelay = 150
            };
            bool darkMode = LoadThemePreference();

            _headerPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
            _listPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            _detailsPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };

            _titleLabel = MakeTitle(AppInfo.ProductName + " " + AppInfo.Version);
            _titleLabel.AutoSize = false;
            _titleLabel.Size = new Size(300, 30);
            _toolTip.SetToolTip(_titleLabel, AppInfo.ProductName + " " + AppInfo.DisplayVersion);
            _subtitleLabel = MakeSubtitle("A safer, TMAC-inspired network adapter changer");
            _subtitleLabel.AutoSize = false;
            _subtitleLabel.Size = new Size(360, 22);
            _toolTip.SetToolTip(_subtitleLabel, "Version " + AppInfo.DisplayVersion);
            _headerPublicIpLabel = MakeMutedLabel("Public IP: checking...");
            _adapterCountLabel = MakeMutedLabel("0 adapters");
            _listHintLabel = MakeMutedLabel("Select an adapter to view its details.");
            _selectedTitleLabel = MakeTitle("No adapter selected");
            _selectedTitleLabel.AutoSize = false;
            _selectedTitleLabel.AutoEllipsis = true;
            _selectedTitleLabel.Size = new Size(700, 30);
            _selectedTitleLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold, GraphicsUnit.Point);
            _selectedStatusLabel = MakeMutedLabel("Connect or enable a physical network adapter.");
            _currentMacLabel = MakeValueLabel("—");
            _permanentMacLabel = MakeValueLabel("—");
            _networkIpLabel = MakeValueLabel("—");
            _networkModeLabel = MakeValueLabel("—");
            _networkLinkLabel = MakeMutedLabel("—");
            _publicIpLabel = MakeValueLabel("Unavailable");
            _vpnHintLabel = MakeMutedLabel("No VPN profiles configured in Windows Settings.");
            _operationStatusLabel = MakeMutedLabel("Ready.");
            _safetySummaryLabel = MakeMutedLabel("MAC and IP changes are saved with a restore profile before they are applied. Verified changes are restored automatically when you exit.");
            _toolTip.SetToolTip(_safetySummaryLabel, _safetySummaryLabel.Text);

            _darkModeCheckBox = new CheckBox { Text = "Dark mode", AutoSize = true, UseVisualStyleBackColor = false };
            _darkModeCheckBox.Checked = darkMode;
            _darkModeCheckBox.CheckedChanged += (sender, args) =>
            {
                ApplyTheme(_darkModeCheckBox.Checked);
                if (DarkModeChanged != null)
                {
                    DarkModeChanged(this, EventArgs.Empty);
                }
            };

            _allowDhcpIpCheckBox = new CheckBox { Text = "Allow DHCP IP randomization (risky)", AutoSize = true, UseVisualStyleBackColor = false };
            _allowDhcpIpCheckBox.CheckedChanged += (sender, args) =>
            {
                UpdateSelectionDetails();
                if (DhcpIpConsentChanged != null)
                {
                    DhcpIpConsentChanged(this, EventArgs.Empty);
                }
            };

            _showPublicIpLocationCheckBox = new CheckBox { Text = "Show public IP location (sends IP to ipinfo.io)", AutoSize = true, UseVisualStyleBackColor = false };
            _showPublicIpLocationCheckBox.CheckedChanged += (sender, args) =>
            {
                if (PublicIpLocationConsentChanged != null)
                {
                    PublicIpLocationConsentChanged(this, EventArgs.Empty);
                }
            };

            _adapterList = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                ShowItemToolTips = true,
                Dock = DockStyle.Fill,
                OwnerDraw = true
            };
            _adapterList.DrawColumnHeader += DrawAdapterColumnHeader;
            _adapterList.DrawItem += DrawAdapterItem;
            _adapterList.Resize += (sender, args) => AdjustAdapterColumns();
            _adapterList.Columns.Add("Adapter", 190);
            _adapterList.Columns.Add("Status", 85);
            _adapterList.Columns.Add("IPv4", 130);
            _adapterList.SelectedIndexChanged += AdapterSelectionChanged;

            _macTextBox = new TextBox { CharacterCasing = CharacterCasing.Upper, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            _vpnCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                DrawMode = DrawMode.OwnerDrawFixed
            };
            _vpnCombo.DrawItem += DrawVpnItem;

            _refreshButton = MakeButton("Refresh", false, new Size(100, 32));
            _randomizeBothButton = MakeButton("Randomize MAC + IP", true, new Size(150, 36));
            _randomizeMacButton = MakeButton("Random MAC", true, new Size(115, 36));
            _applyMacButton = MakeButton("Apply MAC", true, new Size(115, 36));
            _restorePermanentButton = MakeButton("Restore original", false, new Size(135, 36));
            _randomizeIpButton = MakeButton("Randomize local IP", true, new Size(170, 36));
            _restoreButton = MakeButton("Restore saved", false, new Size(125, 36));
            _connectVpnButton = MakeButton("Connect", true, new Size(100, 34));
            _disconnectVpnButton = MakeButton("Disconnect", false, new Size(110, 34));
            _presetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            _savePresetButton = MakeButton("Save preset", false, new Size(100, 30));
            _applyPresetButton = MakeButton("Apply preset", true, new Size(100, 30));
            _startupRandomizeButton = MakeButton("Enable startup MAC randomization", false, new Size(250, 34));
            _notificationCenterButton = MakeButton("Notification center", false, new Size(160, 34));
            _ipPreflightButton = MakeButton("IP preflight (read-only)", false, new Size(190, 34));

            BuildHeader();
            BuildListPane();
            BuildDetailsPane();
            BuildRootLayout();
            WireEvents();
            ApplyHeaderAccessibility();
            ApplyActionAccessibility();
            ApplyTabOrder();
            ApplyTheme(darkMode);
            UpdateActionStates();
        }

        public bool SelectAdapterByKey(string adapterKey)
        {
            if (string.IsNullOrWhiteSpace(adapterKey))
            {
                return false;
            }
            foreach (ListViewItem item in _adapterList.Items)
            {
                AdapterInfo adapter = item.Tag as AdapterInfo;
                if (adapter != null && string.Equals(adapter.Key, adapterKey, StringComparison.OrdinalIgnoreCase))
                {
                    _suppressAdapterSelectionEvents = true;
                    try
                    {
                        item.Selected = true;
                        item.EnsureVisible();
                        _selectedAdapter = adapter;
                    }
                    finally
                    {
                        _suppressAdapterSelectionEvents = false;
                    }
                    UpdateSelectionDetails();
                    if (AdapterChanged != null)
                    {
                        AdapterChanged(this, EventArgs.Empty);
                    }
                    return true;
                }
            }
            return false;
        }

        public void SetAdapters(IList<AdapterInfo> adapters)
        {
            _allAdapters = new List<AdapterInfo>();
            if (adapters != null)
            {
                foreach (AdapterInfo adapter in adapters)
                {
                    if (adapter != null)
                    {
                        _allAdapters.Add(adapter);
                    }
                }
            }
            RebuildAdapterList();
        }

        public void SetVpnProfiles(IList<VpnProfile> profiles)
        {
            VpnProfile selected = SelectedVpn;
            _vpnCombo.Items.Clear();
            if (profiles != null)
            {
                foreach (VpnProfile profile in profiles)
                {
                    _vpnCombo.Items.Add(profile);
                }
            }

            if (_vpnCombo.Items.Count > 0)
            {
                int index = 0;
                if (selected != null)
                {
                    for (int i = 0; i < _vpnCombo.Items.Count; i++)
                    {
                        VpnProfile candidate = (VpnProfile)_vpnCombo.Items[i];
                        if (string.Equals(candidate.Name, selected.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                _vpnCombo.SelectedIndex = index;
            }

            _vpnHintLabel.Text = _vpnCombo.Items.Count == 0
                ? "No VPN profiles configured in Windows Settings."
                : "Select a profile, then connect or disconnect.";
            _toolTip.SetToolTip(_vpnCombo, SelectedVpn == null ? _vpnHintLabel.Text : SelectedVpn.ToString());
            UpdateVpnDropDownWidth();
            UpdateSelectionDetails();
        }

        private void UpdateVpnDropDownWidth()
        {
            int width = _vpnCombo.Width;
            for (int index = 0; index < _vpnCombo.Items.Count; index++)
            {
                string text = Convert.ToString(_vpnCombo.Items[index], System.Globalization.CultureInfo.CurrentCulture);
                Size measured = TextRenderer.MeasureText(
                    text,
                    _vpnCombo.Font,
                    new Size(1000, 1000),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                width = Math.Max(width, measured.Width + 34);
            }
            _vpnCombo.DropDownWidth = Math.Min(480, Math.Max(_vpnCombo.Width, width));
        }

        public void SetPresets(IList<AdapterPreset> presets)
        {
            string selectedKey = SelectedPresetKey;
            _presetCombo.Items.Clear();
            if (presets != null)
            {
                foreach (AdapterPreset preset in presets)
                {
                    if (preset != null)
                    {
                        _presetCombo.Items.Add(preset);
                    }
                }
            }

            int index = 0;
            if (!string.IsNullOrWhiteSpace(selectedKey))
            {
                for (int i = 0; i < _presetCombo.Items.Count; i++)
                {
                    AdapterPreset preset = _presetCombo.Items[i] as AdapterPreset;
                    if (preset != null && string.Equals(preset.PresetKey, selectedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (_presetCombo.Items.Count > 0)
            {
                _presetCombo.SelectedIndex = index;
            }
            UpdateActionStates();
        }

        public void SetStartupRandomizationEnabled(bool enabled)
        {
            _startupRandomizeButton.Text = enabled ? "Disable startup MAC randomization" : "Enable startup MAC randomization";
        }

        public void SetShowPublicIpLocationEnabled(bool enabled)
        {
            if (_showPublicIpLocationCheckBox != null)
            {
                _showPublicIpLocationCheckBox.Checked = enabled;
            }
        }

        public void SetBackupAvailable(bool available)
        {
            IsBackupAvailable = available;
            UpdateActionStates();
        }

        public void SetAdapterDataStale(bool stale)
        {
            _adapterDataStale = stale;
            UpdateSelectionDetails();
        }

        public void SetPublicIp(string value, NetworkIdentity networkIdentity = null)
        {
            string display = string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
            _publicIpLabel.Text = display;
            _headerPublicIpLabel.Text = "Public IP: " + display;

            string tooltip = "Public IP: " + display;
            if (networkIdentity != null)
            {
                var geoParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(networkIdentity.PublicIpCountry))
                    geoParts.Add("Country: " + networkIdentity.PublicIpCountry);
                if (!string.IsNullOrWhiteSpace(networkIdentity.PublicIpRegion))
                    geoParts.Add("Region: " + networkIdentity.PublicIpRegion);
                if (!string.IsNullOrWhiteSpace(networkIdentity.PublicIpCity))
                    geoParts.Add("City: " + networkIdentity.PublicIpCity);
                if (!string.IsNullOrWhiteSpace(networkIdentity.PublicIpIsp))
                    geoParts.Add("ISP: " + networkIdentity.PublicIpIsp);
                if (!string.IsNullOrWhiteSpace(networkIdentity.PublicIpAsn))
                    geoParts.Add("ASN: " + networkIdentity.PublicIpAsn);
                if (!string.IsNullOrWhiteSpace(networkIdentity.PublicIpTimezone))
                    geoParts.Add("Timezone: " + networkIdentity.PublicIpTimezone);

                if (geoParts.Count > 0)
                {
                    tooltip += Environment.NewLine + string.Join(Environment.NewLine, geoParts);
                }
            }

            _toolTip.SetToolTip(_publicIpLabel, tooltip);
            _toolTip.SetToolTip(_headerPublicIpLabel, tooltip);
        }

        public void SetStatus(string value)
        {
            string status = value ?? string.Empty;
            _operationStatusLabel.Text = status;
            _toolTip.SetToolTip(_operationStatusLabel, status);
        }

        public void ClearManualMacInput()
        {
            _macTextBox.Clear();
        }

        public void SetBusy(bool busy)
        {
            _isBusy = busy;
            UpdateActionStates();
            UseWaitCursor = busy;
        }

        public void ShowDashboard()
        {
            if (!Visible)
            {
                Show();
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
            BringToFront();
        }

        public void CloseForExit()
        {
            _allowClose = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ClampToWorkingArea();
            AdjustAdapterColumns();
        }

        private void ClampToWorkingArea()
        {
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(Width, workingArea.Width);
            int height = Math.Min(Height, workingArea.Height);
            if (width < MinimumSize.Width || height < MinimumSize.Height)
            {
                MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
            }
            if (width != Width || height != Height)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(
                    workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2),
                    workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2));
                Size = new Size(width, height);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void BuildRootLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(_headerPanel, 0, 0);

            _pendingBanner = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false, Padding = new Padding(18, 8, 18, 8), Margin = Padding.Empty, Tag = Color.Goldenrod };
            var bannerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _pendingBannerLabel = MakeMutedLabel(string.Empty);
            _pendingBannerLabel.Dock = DockStyle.Fill;
            _pendingBannerLabel.TextAlign = ContentAlignment.MiddleLeft;
            _pendingBannerLabel.Margin = new Padding(0, 0, 12, 0);
            // Kept separate from Restore, and hidden unless the pending change is a MAC
            // change, because keeping is the one action here that MacRando cannot undo.
            _keepChangeButton = MakeButton("Keep change", false, new Size(104, 28));
            _keepChangeButton.AutoSize = true;
            _keepChangeButton.Anchor = AnchorStyles.Right;
            _keepChangeButton.Margin = new Padding(0, 0, 8, 0);
            _keepChangeButton.Visible = false;
            _keepChangeButton.Click += (sender, args) => Raise(KeepChangeRequested);
            _toolTip.SetToolTip(
                _keepChangeButton,
                "Leave the changed MAC address in place instead of restoring it on exit. " +
                "The original address is recorded so you can still get back to it.");
            var restoreAllButton = MakeButton("Restore all", false, new Size(96, 28));
            restoreAllButton.AutoSize = true;
            restoreAllButton.Anchor = AnchorStyles.Right;
            restoreAllButton.Margin = new Padding(0, 0, 8, 0);
            restoreAllButton.Click += (sender, args) =>
            {
                if (RestoreAllPendingRequested != null)
                {
                    RestoreAllPendingRequested(this, EventArgs.Empty);
                }
            };
            var bannerDismissButton = MakeButton("Dismiss", false, new Size(84, 28));
            bannerDismissButton.AutoSize = true;
            bannerDismissButton.Anchor = AnchorStyles.Right;
            bannerDismissButton.Click += (sender, args) => _pendingBanner.Visible = false;
            _toolTip.SetToolTip(bannerDismissButton, "Hide this banner for the current session. The tray menu can still restore pending profiles.");
            bannerLayout.Controls.Add(_pendingBannerLabel, 0, 0);
            bannerLayout.Controls.Add(_keepChangeButton, 1, 0);
            bannerLayout.Controls.Add(restoreAllButton, 2, 0);
            bannerLayout.Controls.Add(bannerDismissButton, 3, 0);
            _pendingBanner.Controls.Add(bannerLayout);
            root.Controls.Add(_pendingBanner, 0, 1);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            body.Controls.Add(_listPanel, 0, 0);
            body.Controls.Add(_detailsPanel, 1, 0);
            root.Controls.Add(body, 0, 2);
            Controls.Add(root);
        }

        /// <summary>
        /// Says whether the pending change could be kept rather than restored. Called
        /// separately from the count so the button appears only when there is something it
        /// can actually do, and never for a change that is only an address change.
        /// </summary>
        public void SetKeepChangeAvailable(bool available)
        {
            if (_keepChangeButton != null)
            {
                _keepChangeButton.Visible = available;
            }
        }

        public void SetPendingRestoreCount(int count)
        {
            if (_pendingBanner == null || _pendingBannerLabel == null)
            {
                return;
            }
            if (count > 0)
            {
                _pendingBannerLabel.Text = count == 1
                    ? "1 adapter has a pending restore profile. Restoring returns it to the saved configuration."
                    : count + " adapters have pending restore profiles. Restoring returns them to the saved configuration.";
                _pendingBanner.Visible = true;
                // The banner's buttons are named on first use, and the banner is hidden
                // until a restore is outstanding, so this has to be re-run as it appears.
                NameOnDemandButtons(_pendingBanner, "Restore all", "Restore all pending adapters",
                    "Restores every adapter that still has a pending restore profile.", true);
                NameOnDemandButtons(_pendingBanner, "Keep change", "Keep the changed MAC address",
                    "Leaves the changed MAC address in place instead of restoring it on exit. " +
                    "The original address is recorded so you can still get back to it.", true);                NameOnDemandButtons(_pendingBanner, "Dismiss", "Dismiss the pending restore banner",
                    "Hides this banner. The pending changes are not affected.", true);
                Accessibility.Describe(_pendingBannerLabel, "Pending restore summary", AccessibleRole.Text,
                    count == 1
                        ? "One adapter has a pending restore profile."
                        : count + " adapters have pending restore profiles.");
            }
            else
            {
                _pendingBanner.Visible = false;
            }
        }

        /// <summary>
        /// Names the header controls. A screen reader otherwise reaches the public IP
        /// label, the dark mode toggle, and Refresh with nothing but their values, so
        /// "203.0.113.4" and "Refresh" are both announced as bare text.
        /// </summary>
        private void ApplyHeaderAccessibility()
        {
            // Named here rather than with the on-demand banner buttons, because this one is
            // hidden until a keepable change exists, so a naming pass driven by the banner
            // appearing would leave it unlabelled in the state where it first shows.
            Accessibility.Describe(_keepChangeButton, "Keep the changed MAC address", AccessibleRole.PushButton,
                "Leaves the changed MAC address in place instead of restoring it on exit. " +
                "The original address is recorded so you can still get back to it.");
            Accessibility.Describe(_headerPublicIpLabel, "Public IP address", AccessibleRole.Text,
                "The public IP address seen by websites from this machine.");
            Accessibility.Describe(_titleLabel, "MacRando", AccessibleRole.Text,
                "Application title and version.");
            Accessibility.Describe(_subtitleLabel, "Adapter changer description", AccessibleRole.Text,
                "A one line description of what this application does.");
            Accessibility.Describe(_darkModeCheckBox, "Dark mode", AccessibleRole.CheckButton,
                "Switches between the dark and light appearance.");
            Accessibility.Describe(_refreshButton, "Refresh adapters", AccessibleRole.PushButton,
                "Re-reads the adapter list, network state, and public IP address.");
        }

        private void BuildHeader()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(24, 10, 24, 8),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var titleLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _titleLabel.Dock = DockStyle.Top;
            _titleLabel.AutoSize = true;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _titleLabel.Margin = Padding.Empty;
            _subtitleLabel.Dock = DockStyle.Top;
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _subtitleLabel.Margin = Padding.Empty;
            titleLayout.Controls.Add(_titleLabel, 0, 0);
            titleLayout.Controls.Add(_subtitleLabel, 0, 1);
            layout.Controls.Add(titleLayout, 0, 0);
            _headerPublicIpLabel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _headerPublicIpLabel.Size = new Size(180, 28);
            _headerPublicIpLabel.TextAlign = ContentAlignment.MiddleRight;
            layout.Controls.Add(_headerPublicIpLabel, 1, 0);
            _darkModeCheckBox.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            layout.Controls.Add(_darkModeCheckBox, 2, 0);
            _refreshButton.Anchor = AnchorStyles.Top;
            _refreshButton.Dock = DockStyle.Top;
            _refreshButton.Margin = Padding.Empty;
            layout.Controls.Add(_refreshButton, 3, 0);
            _headerPanel.Controls.Add(layout);
        }

        /// <summary>
        /// Names the action controls and the inputs they act on.
        ///
        /// The dangerous ones carry a description explaining the consequence, because a
        /// button labelled only "Randomize local IPv4 address" does not convey that it
        /// changes the machine's address, and the DHCP consent box does not convey that it
        /// is per operation and never stored. That is exactly the information a screen
        /// reader user needs and currently has no route to.
        /// </summary>
        private void ApplyActionAccessibility()
        {
            Accessibility.Describe(_randomizeBothButton, "Randomize MAC address and local IP address", AccessibleRole.PushButton,
                "Changes both the MAC address and the local IPv4 address. A restore profile is saved first.");
            Accessibility.Describe(_randomizeMacButton, "Randomize MAC address", AccessibleRole.PushButton,
                "Applies a random locally administered unicast MAC address. A restore profile is saved first.");
            Accessibility.Describe(_randomizeIpButton, "Randomize local IPv4 address", AccessibleRole.PushButton,
                "Applies a temporary static IPv4 address in the current subnet. A restore profile is saved first.");
            Accessibility.Describe(_applyMacButton, "Apply entered MAC address", AccessibleRole.PushButton,
                "Applies the MAC address typed in the box. A restore profile is saved first.");
            Accessibility.Describe(_macTextBox, "MAC address to apply", AccessibleRole.Text,
                "Type a unicast MAC address in the form 02-00-00-00-00-03, then use Apply.");
            Accessibility.Describe(_restoreButton, "Restore this session's changes", AccessibleRole.PushButton,
                "Restores the adapter to the state saved when this session began.");
            Accessibility.Describe(_restorePermanentButton, "Restore the adapter's permanent MAC address", AccessibleRole.PushButton,
                "Restores the MAC address the hardware reports as permanent. Use after a forced shutdown.");
            Accessibility.Describe(_connectVpnButton, "Connect VPN profile", AccessibleRole.PushButton,
                "Connects the VPN profile chosen in the list. A VPN is the supported way to change the public IP address.");
            Accessibility.Describe(_disconnectVpnButton, "Disconnect VPN profile", AccessibleRole.PushButton,
                "Disconnects the VPN profile chosen in the list.");
            Accessibility.Describe(_vpnCombo, "VPN profile", AccessibleRole.ComboBox,
                "Windows VPN profiles already configured in Settings. The list is empty if none exist.");
            Accessibility.Describe(_presetCombo, "Adapter preset", AccessibleRole.ComboBox,
                "Saved presets for the selected adapter. The list shows any network a preset is bound to.");
            Accessibility.Describe(_savePresetButton, "Save preset", AccessibleRole.PushButton,
                "Saves the current options as a named preset, optionally bound to the current network.");
            Accessibility.Describe(_applyPresetButton, "Apply preset", AccessibleRole.PushButton,
                "Applies the selected preset through the same backup and verification workflow as the manual actions.");
            Accessibility.Describe(_startupRandomizeButton, "Startup MAC randomization", AccessibleRole.PushButton,
                "Enables or disables randomizing the MAC address when Windows starts. Never runs while a restore profile is pending.");
            Accessibility.Describe(_notificationCenterButton, "Notification center", AccessibleRole.PushButton,
                "Opens the notification history and settings.");
            Accessibility.Describe(_ipPreflightButton, "IP preflight", AccessibleRole.PushButton,
                "A read-only report of what an IP change would alter. Changes nothing.");
            Accessibility.Describe(_allowDhcpIpCheckBox, "Allow DHCP IP randomization", AccessibleRole.CheckButton,
                "Risky. Consent is required for each operation and is never stored. An adapter using DHCP cannot be randomized without it.");
            Accessibility.Describe(_showPublicIpLocationCheckBox, "Show public IP location", AccessibleRole.CheckButton,
                "Sends your public IP to ipinfo.io to fetch country, city, ISP, and ASN. Off by default for privacy. You can enable it to see where your IP is located.");
            Accessibility.Describe(_connectedOnlyCheckBox, "Connected adapters only", AccessibleRole.CheckButton,
                "Hides adapters that are not currently connected.");
            Accessibility.Describe(_adapterSearchBox, "Search adapters", AccessibleRole.Text,
                "Filters the adapter list by name or hardware description.");
            Accessibility.Describe(_adapterList, "Physical network adapters", AccessibleRole.List,
                "Use the up and down arrows to move between adapters. The current adapter's details appear in the panel to the right.");
            Accessibility.Describe(_adapterCountLabel, "Adapter count", AccessibleRole.Text,
                "How many adapters are shown and how many exist in total.");
            Accessibility.Describe(_listHintLabel, "Adapter list help", AccessibleRole.Text,
                "How to move between and select adapters.");
            Accessibility.Describe(_pendingBanner, "Pending restore", AccessibleRole.Text,
                "Shown when an adapter still has changes that have not been restored.");
            Accessibility.Describe(_operationStatusLabel, "Operation status", AccessibleRole.Text,
                "The most recent operation and its result.");
            Accessibility.Describe(_safetySummaryLabel, "Safety summary", AccessibleRole.Text,
                "How MacRando saves and restores adapter changes.");

            // Created on demand by the layout helpers, so they cannot be named in the
            // field initialisers. The pending banner's Restore all is the one that
            // matters most: it is the button a user reaches for when an adapter is left
            // changed, and it is only present when something needs restoring.
            NameOnDemandButtons(this, "Star selected", "Favorite the selected adapter",
                "Marks this adapter as a favorite. Favorites are stored per interface GUID.", true);
            // Both of these live in the pending banner, which is hidden until a restore is
            // actually outstanding, so they are absent until then and the lookup is
            // retried whenever the banner appears.
            NameOnDemandButtons(this, "Restore all", "Restore all pending adapters",
                "Restores every adapter that still has a pending restore profile.", false);
            NameOnDemandButtons(this, "Dismiss", "Dismiss the pending restore banner",
                "Hides this banner. The pending changes are not affected.", false);
        }

        /// <summary>
        /// <summary>
        /// Names buttons the layout helpers create at runtime. Matching on visible text is
        /// the only handle available, so the result is asserted: a renamed button would
        /// otherwise fail silently and leave an unlabelled control on screen, which is the
        /// exact defect this exists to prevent.
        /// </summary>
        private void NameOnDemandButtons(Control root, string text, string name, string description, bool required)
        {
            bool found = false;
            foreach (Control control in EnumerateControls(root))
            {
                Button button = control as Button;
                if (button != null && string.Equals(button.Text, text, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(button.AccessibleName))
                {
                    Accessibility.Describe(button, name, AccessibleRole.PushButton, description);
                    found = true;
                }
            }
            if (required && !found)
            {
                AppLogger.Warning("Accessibility: expected to find a button labelled \"" + text +
                    "\" to name, but it was not present.");
            }
        }

        internal static List<Control> EnumerateControls(Control root)
        {
            var found = new List<Control>();
            if (root == null)
            {
                return found;
            }
            foreach (Control child in root.Controls)
            {
                found.Add(child);
                found.AddRange(EnumerateControls(child));
            }
            return found;
        }

        /// <summary>
        /// Sets an explicit tab order following the reading order of the page: search and
        /// filter, the adapter list, then the actions for the selected adapter in the order
        /// they appear.
        ///
        /// Without this, traversal follows the order the controls happened to be created
        /// and added, which is layout-plumbing order rather than reading order, so a
        /// keyboard user can land on Apply before the box it applies. Ordering is asserted
        /// by test, because a tab order that silently reverts to z-order is exactly the kind
        /// of regression nothing else would catch.
        /// </summary>
        private void ApplyTabOrder()
        {
            // The pending banner's Restore all is the highest-value target when it is
            // present, so it leads. It is created on demand, hence the lookup.
            Control restoreAll = null;
            foreach (Control control in EnumerateControls(this))
            {
                Button button = control as Button;
                if (button != null && string.Equals(button.Text, "Restore all", StringComparison.Ordinal) &&
                    button.Visible)
                {
                    restoreAll = button;
                    break;
                }
            }

            var sequence = new List<Control>();
            if (restoreAll != null)
            {
                sequence.Add(restoreAll);
            }
            sequence.AddRange(new Control[]
            {
                _darkModeCheckBox,
                _refreshButton,
                _adapterSearchBox,
                _connectedOnlyCheckBox,
                _adapterList,
                _macTextBox,
                _vpnCombo,
                _presetCombo,
                _applyMacButton,
                _randomizeMacButton,
                _randomizeBothButton,
                _randomizeIpButton,
                _savePresetButton,
                _applyPresetButton,
                _connectVpnButton,
                _disconnectVpnButton,
                _restorePermanentButton,
                _restoreButton,
                _startupRandomizeButton,
                _ipPreflightButton,
                _notificationCenterButton
            });
            for (int index = 0; index < sequence.Count; index++)
            {
                if (sequence[index] != null)
                {
                    sequence[index].TabIndex = index;
                }
            }
        }

        private void BuildListPane()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(18, 18, 14, 14) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label title = MakeMutedLabel("PHYSICAL ADAPTERS");
            title.AutoSize = false;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            layout.Controls.Add(title, 0, 0);

            _adapterSearchBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 4) };
            SetCueBanner(_adapterSearchBox, "Search adapters...");
            _toolTip.SetToolTip(_adapterSearchBox, "Filter adapters by name or hardware description.");
            _adapterSearchBox.TextChanged += (sender, args) =>
            {
                if (_suppressViewEvents)
                {
                    return;
                }
                RebuildAdapterList();
                RaiseAdapterViewChanged();
            };
            layout.Controls.Add(_adapterSearchBox, 0, 1);

            var filterRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            _connectedOnlyCheckBox = new CheckBox { Text = "Connected only", AutoSize = true, Margin = new Padding(0, 4, 12, 0) };
            _connectedOnlyCheckBox.CheckedChanged += (sender, args) =>
            {
                if (_suppressViewEvents)
                {
                    return;
                }
                RebuildAdapterList();
                RaiseAdapterViewChanged();
            };
            _favoriteButton = MakeButton("Star selected", false, new Size(120, 26));
            _favoriteButton.AutoSize = true;
            _favoriteButton.Margin = new Padding(0, 0, 0, 0);
            _favoriteButton.Anchor = AnchorStyles.Left;
            _toolTip.SetToolTip(_favoriteButton, "Mark or unmark the selected adapter as a favorite. Favorites are listed first.");
            _favoriteButton.Click += (sender, args) =>
            {
                if (SelectedAdapter != null && AdapterViewChanged != null)
                {
                    ToggleFavoriteRequested(this, EventArgs.Empty);
                }
            };
            filterRow.Controls.Add(_connectedOnlyCheckBox);
            filterRow.Controls.Add(_favoriteButton);
            layout.Controls.Add(filterRow, 0, 2);

            layout.Controls.Add(_adapterList, 0, 3);
            _listHintLabel.Dock = DockStyle.Fill;
            _listHintLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(_listHintLabel, 0, 4);
            _licenseLabel = MakeMutedLabel(LicenseInfo.Notice);
            _licenseLabel.Dock = DockStyle.Fill;
            _licenseLabel.TextAlign = ContentAlignment.MiddleLeft;
            _licenseLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            _toolTip.SetToolTip(_licenseLabel, "Open the tray menu and choose License to read the full " + LicenseInfo.Name + ".");
            layout.Controls.Add(_licenseLabel, 0, 5);
            _listPanel.Controls.Add(layout);
        }

        private void RaiseAdapterViewChanged()
        {
            if (AdapterViewChanged != null)
            {
                AdapterViewChanged(this, EventArgs.Empty);
            }
        }

        public event EventHandler ToggleFavoriteRequested;

        public string AdapterSearchText
        {
            get { return _adapterSearchBox == null ? string.Empty : _adapterSearchBox.Text; }
        }

        public bool ConnectedOnly
        {
            get { return _connectedOnlyCheckBox != null && _connectedOnlyCheckBox.Checked; }
        }

        public void SetAdapterView(List<string> favoriteKeys, bool connectedOnly)
        {
            _suppressViewEvents = true;
            try
            {
                _favoriteKeys = favoriteKeys == null ? new List<string>() : new List<string>(favoriteKeys);
                if (_connectedOnlyCheckBox != null)
                {
                    _connectedOnlyCheckBox.Checked = connectedOnly;
                }
            }
            finally
            {
                _suppressViewEvents = false;
            }
            RebuildAdapterList();
        }

        public void UpdateFavoriteButton()
        {
            if (_favoriteButton == null)
            {
                return;
            }
            AdapterInfo adapter = _selectedAdapter;
            bool isFavorite = false;
            if (adapter != null)
            {
                foreach (string key in _favoriteKeys)
                {
                    if (string.Equals(key, adapter.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        isFavorite = true;
                        break;
                    }
                }
            }
            _favoriteButton.Text = isFavorite ? "Unstar selected" : "Star selected";
            _favoriteButton.Enabled = adapter != null;
        }

        private void RebuildAdapterList()
        {
            string selectedKey = _selectedAdapter == null ? null : _selectedAdapter.Key;
            string search = AdapterSearchText == null ? string.Empty : AdapterSearchText.Trim();
            bool connectedOnly = ConnectedOnly;

            var visible = new List<AdapterInfo>();
            foreach (AdapterInfo adapter in _allAdapters)
            {
                if (adapter == null)
                {
                    continue;
                }
                if (connectedOnly && !adapter.IsUp)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(search) &&
                    (adapter.Name ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (adapter.Description ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                visible.Add(adapter);
            }

            // Favorites first, then connected adapters, then the original order.
            var ordered = new List<AdapterInfo>();
            foreach (AdapterInfo adapter in visible)
            {
                if (IsFavorite(adapter.Key))
                {
                    ordered.Add(adapter);
                }
            }
            foreach (AdapterInfo adapter in visible)
            {
                if (adapter.IsUp && !IsFavorite(adapter.Key))
                {
                    ordered.Add(adapter);
                }
            }
            foreach (AdapterInfo adapter in visible)
            {
                if (!adapter.IsUp && !IsFavorite(adapter.Key))
                {
                    ordered.Add(adapter);
                }
            }

            PopulateAdapterList(ordered, selectedKey);
            _adapterCountLabel.Text = BuildAdapterCountText(ordered.Count);
            UpdateFavoriteButton();
        }

        private bool IsFavorite(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }
            foreach (string favorite in _favoriteKeys)
            {
                if (string.Equals(favorite, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private string BuildAdapterCountText(int visibleCount)
        {
            int total = _allAdapters.Count;
            if (visibleCount == total)
            {
                return total + " adapter" + (total == 1 ? string.Empty : "s");
            }
            return visibleCount + " of " + total + " adapters";
        }

        private void PopulateAdapterList(IList<AdapterInfo> adapters, string selectedKey)
        {
            _suppressAdapterSelectionEvents = true;
            try
            {
                _adapterList.BeginUpdate();
                try
                {
                    _adapterList.Items.Clear();
                    ListViewItem selectedItem = null;
                    ListViewItem firstUpItem = null;
                    if (adapters != null)
                    {
                        foreach (AdapterInfo adapter in adapters)
                        {
                            // The kind is shown in the list rather than the adapter being
                            // hidden, because a missing adapter gives a user no way to tell
                            // that it was deliberately excluded or why.
                            string kindLabel = AdapterClassification.DescribeKind(adapter.Kind);
                            ListViewItem item = new ListViewItem(
                                (IsFavorite(adapter.Key) ? "★ " : string.Empty) + (adapter.Name ?? "Unknown adapter") +
                                (adapter.IsChangeable ? string.Empty : "  [" + kindLabel + "]"));
                            item.SubItems.Add(string.IsNullOrWhiteSpace(adapter.Status) ? "Unknown" : adapter.Status);
                            item.SubItems.Add(string.IsNullOrWhiteSpace(adapter.IpAddress) ? "—" : adapter.IpAddress);
                            item.ToolTipText = adapter.ToString() + "  •  " + (string.IsNullOrWhiteSpace(adapter.IpAddress) ? "No IPv4" : adapter.IpAddress) +
                                (adapter.IsChangeable ? string.Empty : "  •  " + adapter.Restriction);
                            item.Tag = adapter;
                            _adapterList.Items.Add(item);
                            if (firstUpItem == null && adapter.IsUp)
                            {
                                firstUpItem = item;
                            }
                            if (!string.IsNullOrWhiteSpace(selectedKey) && string.Equals(adapter.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedItem = item;
                            }
                        }
                    }

                    if (selectedItem == null)
                    {
                        selectedItem = firstUpItem ?? (_adapterList.Items.Count > 0 ? _adapterList.Items[0] : null);
                    }
                    if (selectedItem != null)
                    {
                        selectedItem.Selected = true;
                        _selectedAdapter = selectedItem.Tag as AdapterInfo;
                    }
                    else
                    {
                        _selectedAdapter = null;
                    }
                }
                finally
                {
                    _adapterList.EndUpdate();
                }
            }
            finally
            {
                _suppressAdapterSelectionEvents = false;
            }

            if (_adapterList.SelectedItems.Count == 1)
            {
                _adapterList.SelectedItems[0].EnsureVisible();
            }
            UpdateSelectionDetails();
        }

        private void BuildDetailsPane()
        {
            TableLayoutPanel page = CreatePageLayout();
            AddPageHeading(page, _selectedTitleLabel, _selectedStatusLabel);

            _driverLabel = MakeValueLabel("—");
            _interfaceIndexLabel = MakeValueLabel("—");
            _macPropertyLabel = MakeValueLabel("—");
            TableLayoutPanel adapterBody;
            TableLayoutPanel adapterCard = CreateCard("Adapter details", 2, 4, out adapterBody);
            SetBodyRows(adapterBody, 22, 30, 22, 30);
            AddCellLabel(adapterBody, "Driver", 0, 0);
            AddCellLabel(adapterBody, "Interface index", 1, 0);
            ConfigureValueCell(_driverLabel);
            ConfigureValueCell(_interfaceIndexLabel);
            adapterBody.Controls.Add(_driverLabel, 0, 1);
            adapterBody.Controls.Add(_interfaceIndexLabel, 1, 1);
            AddCellLabel(adapterBody, "NetworkAddress property", 0, 2);
            AddCellLabel(adapterBody, "Interface GUID", 1, 2);
            ConfigureValueCell(_macPropertyLabel);
            adapterBody.Controls.Add(_macPropertyLabel, 0, 3);
            _guidLabel = MakeValueLabel("—");
            ConfigureValueCell(_guidLabel);
            adapterBody.Controls.Add(_guidLabel, 1, 3);
            AddPageRow(page, adapterCard);

            TableLayoutPanel macBody;
            TableLayoutPanel macCard = CreateCard("MAC address", 2, 6, out macBody);
            SetBodyRows(macBody, 22, 30, 22, 34, 38, -1);
            AddCellLabel(macBody, "Current MAC", 0, 0);
            AddCellLabel(macBody, "Permanent MAC", 1, 0);
            ConfigureValueCell(_currentMacLabel);
            ConfigureValueCell(_permanentMacLabel);
            macBody.Controls.Add(_currentMacLabel, 0, 1);
            macBody.Controls.Add(_permanentMacLabel, 1, 1);
            AddCellLabel(macBody, "Manual MAC", 0, 2);
            var manualRow = CreateTwoColumnRow();
            ConfigureInputCell(_macTextBox);
            Label macHint = MakeMutedLabel("12 hex digits; separators are optional.");
            macHint.Dock = DockStyle.Fill;
            macHint.TextAlign = ContentAlignment.MiddleLeft;
            macHint.Margin = Padding.Empty;
            manualRow.Controls.Add(_macTextBox, 0, 0);
            manualRow.Controls.Add(macHint, 1, 0);
            macBody.Controls.Add(manualRow, 0, 3);
            macBody.SetColumnSpan(manualRow, 2);
            TableLayoutPanel presetRow = CreatePresetRow();
            macBody.Controls.Add(presetRow, 0, 4);
            macBody.SetColumnSpan(presetRow, 2);
            TableLayoutPanel actionRow = CreateActionGrid(2,
                _randomizeMacButton,
                _applyMacButton,
                _randomizeBothButton,
                _restorePermanentButton,
                _restoreButton);
            macBody.Controls.Add(actionRow, 0, 5);
            macBody.SetColumnSpan(actionRow, 2);
            AddPageRow(page, macCard);

            TableLayoutPanel networkBody;
            TableLayoutPanel networkCard = CreateCard("Local network", 2, 4, out networkBody);
            SetBodyRows(networkBody, 22, 30, 30, -1);
            AddCellLabel(networkBody, "IPv4 address", 0, 0);
            AddCellLabel(networkBody, "Mode", 1, 0);
            ConfigureValueCell(_networkIpLabel);
            ConfigureValueCell(_networkModeLabel);
            networkBody.Controls.Add(_networkIpLabel, 0, 1);
            networkBody.Controls.Add(_networkModeLabel, 1, 1);
            AddCellLabel(networkBody, "Link", 0, 2);
            ConfigureValueCell(_networkLinkLabel);
            networkBody.Controls.Add(_networkLinkLabel, 1, 2);
            networkBody.Controls.Add(CreateActionGrid(1, _randomizeIpButton), 1, 3);
            AddPageRow(page, networkCard);

            TableLayoutPanel vpnBody;
            TableLayoutPanel vpnCard = CreateCard("Public IP and VPN", 2, 4, out vpnBody);
            SetBodyRows(vpnBody, 22, 32, 40, -1);
            AddCellLabel(vpnBody, "Public IP", 0, 0);
            Label vpnProfileLabel = AddCellLabel(vpnBody, "Windows VPN profile", 1, 0);
            vpnProfileLabel.Dock = DockStyle.Fill;
            ConfigureValueCell(_publicIpLabel);
            _vpnCombo.Dock = DockStyle.Fill;
            _vpnCombo.Margin = Padding.Empty;
            _vpnHintLabel.Dock = DockStyle.Fill;
            _vpnHintLabel.TextAlign = ContentAlignment.TopLeft;
            _vpnHintLabel.Margin = new Padding(0, 8, 0, 0);
            vpnBody.Controls.Add(_publicIpLabel, 0, 1);
            vpnBody.Controls.Add(_vpnCombo, 1, 1);
            vpnBody.Controls.Add(_vpnHintLabel, 1, 2);
            vpnBody.Controls.Add(CreateActionGrid(2, _connectVpnButton, _disconnectVpnButton), 0, 3);
            AddPageRow(page, vpnCard);

            TableLayoutPanel safetyBody;
            TableLayoutPanel safetyCard = CreateCard("Safety and status", 1, 6, out safetyBody);
            ConfigureSafetyBody(safetyBody);

            var detailsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // A clipping scroll host keeps the cards from overlapping the Safety card.
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            scrollHost.Controls.Add(page);
            detailsLayout.Controls.Add(scrollHost, 0, 0);
            safetyCard.Dock = DockStyle.Top;
            safetyCard.Margin = Padding.Empty;
            detailsLayout.Controls.Add(safetyCard, 0, 1);
            _detailsPanel.Controls.Add(detailsLayout);
        }

        private void WireEvents()
        {
            _refreshButton.Click += (sender, args) => Raise(RefreshRequested);
            _randomizeBothButton.Click += (sender, args) => Raise(RandomizeBothRequested);
            _randomizeMacButton.Click += (sender, args) => Raise(RandomizeMacRequested);
            _applyMacButton.Click += (sender, args) => Raise(ManualMacRequested);
            _restorePermanentButton.Click += (sender, args) => Raise(RestorePermanentRequested);
            _randomizeIpButton.Click += (sender, args) => Raise(RandomizeIpRequested);
            _restoreButton.Click += (sender, args) => Raise(RestoreRequested);
            _connectVpnButton.Click += (sender, args) => Raise(ConnectVpnRequested);
            _disconnectVpnButton.Click += (sender, args) => Raise(DisconnectVpnRequested);
            _savePresetButton.Click += (sender, args) => Raise(SavePresetRequested);
            _applyPresetButton.Click += (sender, args) => Raise(ApplyPresetRequested);
            _startupRandomizeButton.Click += (sender, args) => Raise(StartupRandomizeToggleRequested);
            _notificationCenterButton.Click += (sender, args) => Raise(NotificationCenterRequested);
            _ipPreflightButton.Click += (sender, args) => Raise(IpPreflightRequested);
        }

        private void AdapterSelectionChanged(object sender, EventArgs e)
        {
            if (_suppressAdapterSelectionEvents)
            {
                return;
            }

            if (_adapterList.SelectedItems.Count == 1)
            {
                _selectedAdapter = _adapterList.SelectedItems[0].Tag as AdapterInfo;
            }
            else
            {
                _selectedAdapter = null;
            }
            UpdateSelectionDetails();
            if (AdapterChanged != null)
            {
                AdapterChanged(this, EventArgs.Empty);
            }
        }

        private void UpdateSelectionDetails()
        {
            AdapterInfo adapter = _selectedAdapter;
            if (adapter == null)
            {
                _displayedAdapterKey = null;
                _consentAdapterKey = null;
                if (_allowDhcpIpCheckBox.Checked)
                {
                    _allowDhcpIpCheckBox.Checked = false;
                }
                _macTextBox.Clear();
                _selectedTitleLabel.Text = "No adapter selected";
                _selectedStatusLabel.Text = "Connect or enable a physical network adapter.";
                _currentMacLabel.Text = "—";
                _permanentMacLabel.Text = "—";
                _networkIpLabel.Text = "—";
                _networkModeLabel.Text = "—";
                _networkLinkLabel.Text = "—";
                _driverLabel.Text = "—";
                _interfaceIndexLabel.Text = "—";
                _macPropertyLabel.Text = "—";
                _guidLabel.Text = "—";
                _toolTip.SetToolTip(_selectedTitleLabel, _selectedTitleLabel.Text);
                _toolTip.SetToolTip(_selectedStatusLabel, _selectedStatusLabel.Text);
                _randomizeBothButton.Enabled = false;
                _randomizeMacButton.Enabled = false;
                _applyMacButton.Enabled = false;
                _randomizeIpButton.Enabled = false;
                _restoreButton.Enabled = false;
                _restorePermanentButton.Enabled = false;
                _macTextBox.Enabled = false;
                UpdateActionStates();
                return;
            }

            if (!string.Equals(_displayedAdapterKey, adapter.Key, StringComparison.OrdinalIgnoreCase))
            {
                _macTextBox.Clear();
                _displayedAdapterKey = adapter.Key;
            }
            if (!string.Equals(_consentAdapterKey, adapter.Key, StringComparison.OrdinalIgnoreCase))
            {
                _consentAdapterKey = adapter.Key;
                if (_allowDhcpIpCheckBox.Checked)
                {
                    _allowDhcpIpCheckBox.Checked = false;
                }
            }

            _selectedTitleLabel.Text = adapter.Name ?? "Unnamed adapter";
            string adapterStatus = string.IsNullOrWhiteSpace(adapter.Status) ? "Status unknown" : adapter.Status;
            string staleSuffix = _adapterDataStale ? "  •  Last known values — refresh required" : string.Empty;
            // The restriction is stated in the detail pane, not only in the list and the
            // tooltip. An adapter that MacRando will refuse to change should say so
            // before the user picks a button, rather than after it fails.
            string kindSuffix = adapter.IsChangeable
                ? string.Empty
                : "  •  " + AdapterClassification.DescribeKind(adapter.Kind) + " adapter — read-only here";
            _selectedStatusLabel.Text = adapterStatus + "  •  " + (adapter.IsUp ? "Connected" : "Disconnected") + staleSuffix + kindSuffix;
            _toolTip.SetToolTip(
                _selectedStatusLabel,
                _selectedStatusLabel.Text + (adapter.IsChangeable ? string.Empty : "  •  " + adapter.Restriction));
            _currentMacLabel.Text = DisplayMac(adapter.MacAddress);
            _permanentMacLabel.Text = DisplayMac(adapter.PermanentMacAddress);
            _networkIpLabel.Text = string.IsNullOrWhiteSpace(adapter.IpAddress) ? "—" : adapter.IpAddress + "/" + adapter.PrefixLength;
            _networkModeLabel.Text = adapter.DhcpEnabled ? "DHCP" : "Static";
            _networkLinkLabel.Text = string.IsNullOrWhiteSpace(adapter.LinkSpeed) ? "—" : adapter.LinkSpeed;
            _driverLabel.Text = string.IsNullOrWhiteSpace(adapter.Description) ? "—" : adapter.Description;
            _interfaceIndexLabel.Text = adapter.InterfaceIndex > 0 ? adapter.InterfaceIndex.ToString() : "—";
            _macPropertyLabel.Text = adapter.MacPropertySupported ? "Available" : "Not exposed";
            _guidLabel.Text = string.IsNullOrWhiteSpace(adapter.InterfaceGuid) ? "—" : adapter.InterfaceGuid;
            _toolTip.SetToolTip(_driverLabel, "Driver: " + _driverLabel.Text);
            _toolTip.SetToolTip(_guidLabel, "Interface GUID: " + _guidLabel.Text);
            UpdateFavoriteButton();
            _toolTip.SetToolTip(_selectedTitleLabel, _selectedTitleLabel.Text);
            _toolTip.SetToolTip(_currentMacLabel, "Current MAC: " + _currentMacLabel.Text);
            _toolTip.SetToolTip(_permanentMacLabel, "Permanent MAC: " + _permanentMacLabel.Text);
            _toolTip.SetToolTip(_networkIpLabel, "IPv4 address: " + _networkIpLabel.Text);
            _toolTip.SetToolTip(_networkModeLabel, "Mode: " + _networkModeLabel.Text);
            _toolTip.SetToolTip(_networkLinkLabel, "Link: " + _networkLinkLabel.Text);
            UpdateActionStates();
        }

        private void UpdateActionStates()
        {
            bool canUseMac = CanUseMac();
            bool canUseLocalIp = CanUseLocalIp();
            _adapterList.Enabled = !_isBusy;
            _refreshButton.Enabled = !_isBusy;
            _darkModeCheckBox.Enabled = !_isBusy;
            _allowDhcpIpCheckBox.Enabled = !_isBusy;
            _macTextBox.Enabled = !_isBusy && canUseMac;
            _applyMacButton.Enabled = !_isBusy && canUseMac;
            _randomizeMacButton.Enabled = !_isBusy && canUseMac;
            _randomizeBothButton.Enabled = !_isBusy && canUseMac && canUseLocalIp;
            _randomizeIpButton.Enabled = !_isBusy && canUseLocalIp;
            _restoreButton.Enabled = !_isBusy && _selectedAdapter != null && IsBackupAvailable;
            _restorePermanentButton.Enabled = !_isBusy && CanRestorePermanent();
            _vpnCombo.Enabled = !_isBusy;
            _connectVpnButton.Enabled = !_isBusy && SelectedVpn != null;
            _disconnectVpnButton.Enabled = !_isBusy && SelectedVpn != null;
            _presetCombo.Enabled = !_isBusy && _selectedAdapter != null;
            _savePresetButton.Enabled = !_isBusy && _selectedAdapter != null;
            _applyPresetButton.Enabled = !_isBusy && _selectedAdapter != null && SelectedPresetKey != null;
            _startupRandomizeButton.Enabled = !_isBusy;
            _notificationCenterButton.Enabled = !_isBusy;
            _ipPreflightButton.Enabled = !_isBusy;
        }

        private bool CanUseMac()
        {
            return !_adapterDataStale && _selectedAdapter != null &&
                _selectedAdapter.MacPropertySupported && _selectedAdapter.IsChangeable;
        }

        private bool CanRestorePermanent()
        {
            if (_adapterDataStale || _selectedAdapter == null || !_selectedAdapter.MacPropertySupported || string.IsNullOrWhiteSpace(_selectedAdapter.PermanentMacAddress))
            {
                return false;
            }
            return !string.Equals(NetworkService.NormalizeMac(_selectedAdapter.MacAddress), NetworkService.NormalizeMac(_selectedAdapter.PermanentMacAddress), StringComparison.OrdinalIgnoreCase);
        }

        private bool CanUseLocalIp()
        {
            return !_adapterDataStale && IsLocalIpEligible(_selectedAdapter, AllowDhcpIpRandomization);
        }

        /// <summary>
        /// Guards the auto-apply path. A preset bound to a network names an adapter, and
        /// that adapter may since have been replaced, or may turn out to be a tunnel. The
        /// dashboard's disabled buttons are not a defence here because nothing in the
        /// user interface is involved: the network-change watcher calls this directly.
        /// </summary>
        internal static bool CanAutoApplyTo(AdapterInfo adapter)
        {
            return adapter != null && adapter.IsChangeable && adapter.IsUp;
        }

        internal static bool IsLocalIpEligible(AdapterInfo adapter, bool allowDhcpIp)
        {
            // Changeable, not just connected: a tunnel or virtual machine adapter is up
            // with a perfectly good IPv4 address, and randomising it would break the
            // thing it belongs to.
            return adapter != null && adapter.IsUp && adapter.IsChangeable &&
                !string.IsNullOrWhiteSpace(adapter.IpAddress) &&
                adapter.PrefixLength >= 1 && adapter.PrefixLength <= 30 &&
                !adapter.IpAddress.StartsWith("169.254.", StringComparison.OrdinalIgnoreCase) &&
                (!adapter.DhcpEnabled || allowDhcpIp);
        }

        private static string DisplayMac(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : NetworkService.FormatMac(value);
        }

        private bool LoadThemePreference()
        {
            try
            {
                return File.Exists(_themePath) && string.Equals(File.ReadAllText(_themePath).Trim(), "dark", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool SaveThemePreference(bool dark)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_themePath));
                File.WriteAllText(_themePath, dark ? "dark" : "light");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AdjustAdapterColumns()
        {
            if (_adapterList.Columns.Count != 3)
            {
                return;
            }

            int available = _adapterList.ClientSize.Width;
            if (available < 300)
            {
                return;
            }

            int adapterWidth = Math.Min(190, Math.Max(140, available / 3));
            int statusWidth = Math.Min(100, Math.Max(75, available / 6));
            int ipWidth = Math.Max(80, available - adapterWidth - statusWidth);
            _adapterList.Columns[0].Width = adapterWidth;
            _adapterList.Columns[1].Width = statusWidth;
            _adapterList.Columns[2].Width = ipWidth;
        }

        private void DrawVpnItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _vpnCombo.Items.Count)
            {
                return;
            }

            bool selected = e.Index == _vpnCombo.SelectedIndex;
            Color background = selected ? _listSelectedColor : _listInputColor;
            Color foreground = selected ? _listSelectedTextColor : _listTextColor;
            string text = Convert.ToString(_vpnCombo.Items[e.Index], System.Globalization.CultureInfo.CurrentCulture);
            using (SolidBrush brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    _vpnCombo.Font,
                    Rectangle.Inflate(e.Bounds, -4, 0),
                    foreground,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            }
        }

        private static void DrawCardBorder(object sender, PaintEventArgs e)
        {
            Panel card = (Panel)sender;
            Color color = card.Tag is Color ? (Color)card.Tag : Color.Gray;
            using (Pen pen = new Pen(color))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, card.ClientSize.Width - 1, card.ClientSize.Height - 1);
            }
        }

        private void DrawAdapterColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_listHeaderColor))
            using (Pen pen = new Pen(_listHeaderColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Header.Text,
                    _adapterList.Font,
                    Rectangle.Inflate(e.Bounds, -4, 0),
                    _listTextColor,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawAdapterItem(object sender, DrawListViewItemEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }

            bool selected = e.Item.Selected;
            Color background = selected ? _listSelectedColor : _listInputColor;
            // Read from the item rather than the theme, because the list is owner drawn:
            // setting ListViewItem.ForeColor has no effect when DrawAdapterItem paints
            // every cell itself, which is how an off-limits adapter first came out looking
            // identical to a real one.
            AdapterInfo drawn = e.Item.Tag as AdapterInfo;
            bool changeable = drawn == null || drawn.IsChangeable;
            Color foreground = selected
                ? _listSelectedTextColor
                : (changeable ? _listTextColor : _listMutedColor);
            using (SolidBrush brush = new SolidBrush(background))
            using (Pen pen = new Pen(_listHeaderColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                int x = e.Bounds.Left;
                for (int index = 0; index < _adapterList.Columns.Count; index++)
                {
                    ColumnHeader column = _adapterList.Columns[index];
                    int width = index == _adapterList.Columns.Count - 1 ? e.Bounds.Right - x : column.Width;
                    Rectangle cell = new Rectangle(x, e.Bounds.Top, Math.Max(0, width), e.Bounds.Height);
                    string text = index == 0 ? e.Item.Text : (index < e.Item.SubItems.Count ? e.Item.SubItems[index].Text : string.Empty);
                    TextRenderer.DrawText(
                        e.Graphics,
                        text,
                        _adapterList.Font,
                        Rectangle.Inflate(cell, -4, 0),
                        foreground,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                    x += width;
                }
            }
        }

        private bool _highContrastActive;

        private void ApplyTheme(bool dark)
        {
            bool themeSaved = SaveThemePreference(dark);
            _darkMode = dark;

            // High contrast wins over the dark mode toggle. The user has told the system
            // they need a specific palette, and MacRando's own colours would override that
            // decision with a preference they cannot see.
            bool highContrast = Accessibility.ShouldUseHighContrast();
            HighContrastPalette palette = Accessibility.BuildPalette(highContrast, dark);

            Color background = highContrast
                ? palette.Surface
                : (dark ? Color.FromArgb(15, 23, 42) : Color.FromArgb(245, 247, 250));
            Color surface = highContrast ? palette.Surface : (dark ? Color.FromArgb(30, 41, 59) : Color.White);
            Color input = highContrast ? palette.Surface : (dark ? Color.FromArgb(15, 23, 42) : Color.White);
            Color text = highContrast ? palette.Text : (dark ? Color.FromArgb(226, 232, 240) : Color.FromArgb(30, 41, 59));
            // Muted text is folded back to full-contrast text in high contrast, because a
            // deliberately low-contrast secondary is the first thing to become unreadable.
            Color secondary = highContrast ? palette.Text : (dark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(71, 85, 105));
            Color accent = highContrast ? palette.Accent : (dark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(37, 99, 235));
            Color border = highContrast ? palette.Border : (dark ? Color.FromArgb(71, 85, 105) : Color.FromArgb(203, 213, 225));
            Color listHeader = highContrast ? palette.SurfaceAlt : (dark ? Color.FromArgb(51, 65, 85) : Color.FromArgb(226, 232, 240));
            _highContrastActive = highContrast;
            _listInputColor = input;
            _listHeaderColor = listHeader;
            _listTextColor = text;
            _listMutedColor = secondary;
            _listSelectedColor = accent;
            _listSelectedTextColor = dark ? Color.FromArgb(15, 23, 42) : Color.White;
            BackColor = background;
            _detailsPanel.BackColor = background;
            _headerPanel.BackColor = surface;
            _listPanel.BackColor = dark ? Color.FromArgb(15, 23, 42) : Color.FromArgb(248, 250, 252);
            ApplyControlTheme(this, background, surface, input, text, secondary, accent, border, listHeader);
            _detailsPanel.BackColor = background;
            _headerPanel.BackColor = surface;
            _listPanel.BackColor = dark ? Color.FromArgb(15, 23, 42) : Color.FromArgb(248, 250, 252);
            _titleLabel.ForeColor = text;
            _subtitleLabel.ForeColor = secondary;
            _licenseLabel.ForeColor = secondary;
            _headerPublicIpLabel.ForeColor = secondary;
            _currentMacLabel.ForeColor = accent;
            _publicIpLabel.ForeColor = accent;
            _operationStatusLabel.ForeColor = text;
            if (!themeSaved && IsHandleCreated)
            {
                _operationStatusLabel.Text = "Theme changed for this session, but the preference could not be saved.";
                _toolTip.SetToolTip(_operationStatusLabel, _operationStatusLabel.Text);
            }
        }

        private void ApplyControlTheme(Control control, Color background, Color surface, Color input, Color text, Color secondary, Color accent, Color border, Color listHeader)
        {
            if (_highContrastActive)
            {
                // Under high contrast the OS draws the borders, so the flat custom styling
                // is undone rather than reinforced, and every colour comes from the
                // user's own SystemColors choice.
                if (control is Button || control is TextBox || control is ComboBox || control is CheckBox || control is ListView || control is NumericUpDown)
                {
                    control.BackColor = SystemColors.Window;
                    control.ForeColor = SystemColors.WindowText;
                }
                if (control is ButtonBase)
                {
                    ButtonBase button = (ButtonBase)control;
                    button.FlatStyle = FlatStyle.Standard;
                    button.UseVisualStyleBackColor = true;
                }
                if (control is Panel || control is TableLayoutPanel || control is Form)
                {
                    control.BackColor = SystemColors.Window;
                    control.ForeColor = SystemColors.WindowText;
                }
                if (control is Label)
                {
                    control.BackColor = Color.Transparent;
                    control.ForeColor = SystemColors.WindowText;
                }

                // Returned rather than falling through: the custom styling below would
                // immediately put the flat style and the app's own colours back.
                return;
            }
            if (control is Form)
            {
                control.BackColor = background;
                control.ForeColor = text;
            }
            else if (control is Label)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = secondary;
            }
            else if (control is Button)
            {
                Button button = (Button)control;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = border;
                button.FlatAppearance.MouseOverBackColor = button.Tag is bool && (bool)button.Tag
                    ? (_darkMode ? Color.FromArgb(30, 64, 175) : Color.FromArgb(29, 78, 216))
                    : (_darkMode ? Color.FromArgb(51, 65, 85) : Color.FromArgb(219, 234, 254));
                button.FlatAppearance.MouseDownBackColor = button.Tag is bool && (bool)button.Tag
                    ? (_darkMode ? Color.FromArgb(30, 64, 175) : Color.FromArgb(30, 64, 175))
                    : (_darkMode ? Color.FromArgb(71, 85, 105) : Color.FromArgb(191, 219, 254));
                button.UseVisualStyleBackColor = false;
                button.BackColor = button.Tag is bool && (bool)button.Tag ? accent : surface;
                button.ForeColor = button.Tag is bool && (bool)button.Tag ? _listSelectedTextColor : text;
            }
            else if (control is CheckBox)
            {
                CheckBox check = (CheckBox)control;
                check.BackColor = Color.Transparent;
                check.ForeColor = text;
                check.UseVisualStyleBackColor = false;
            }
            else if (control is TextBox)
            {
                TextBox box = (TextBox)control;
                box.BackColor = input;
                box.ForeColor = text;
                box.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ComboBox)
            {
                ComboBox combo = (ComboBox)control;
                combo.BackColor = input;
                combo.ForeColor = text;
            }
            else if (control is ListView)
            {
                ListView list = (ListView)control;
                list.BackColor = input;
                list.ForeColor = text;
                list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            }
            else if (control is TableLayoutPanel || control is FlowLayoutPanel)
            {
                control.BackColor = Color.Transparent;
                if (control.Tag is Color)
                {
                    Panel card = (Panel)control;
                    card.Tag = border;
                    card.BorderStyle = BorderStyle.None;
                    card.Invalidate();
                }
            }
            else if (control is Panel)
            {
                control.BackColor = surface;
                control.ForeColor = text;
                if (control.Tag is Color)
                {
                    Panel card = (Panel)control;
                    card.Tag = border;
                    card.BorderStyle = BorderStyle.None;
                    card.Invalidate();
                }
            }
            else
            {
                control.BackColor = surface;
                control.ForeColor = text;
            }
            foreach (Control child in control.Controls)
            {
                ApplyControlTheme(child, background, surface, input, text, secondary, accent, border, listHeader);
            }
        }

        private static Label MakeTitle(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point), ForeColor = Color.FromArgb(30, 41, 59) };
        }

        private static Label MakeSubtitle(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point), ForeColor = Color.FromArgb(71, 85, 105) };
        }

        private static Label MakeMutedLabel(string text)
        {
            return new Label { Text = text, AutoSize = false, AutoEllipsis = true, ForeColor = Color.FromArgb(100, 116, 139) };
        }

        private static Label MakeValueLabel(string text)
        {
            return new Label { Text = text, AutoSize = false, AutoEllipsis = true, Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point), ForeColor = Color.FromArgb(30, 64, 175) };
        }

        /// <summary>
        /// Shared so the ad-hoc dialogs in the tray context get the same icon as the
        /// dashboard rather than the blank default a bare Form starts with.
        /// </summary>
        internal static Icon LoadApplicationIcon()
        {
            // Prefer the executable's embedded icon so title bars and shell
            // notifications use the same Windows-native icon resource.
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
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MacRando.ico");
                if (File.Exists(path))
                {
                    return new Icon(path, new Size(32, 32));
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

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            if (box == null || box.IsDisposed)
            {
                return;
            }
            try
            {
                if (!box.IsHandleCreated)
                {
                    box.HandleCreated += (sender, args) => SetCueBanner(box, text);
                    return;
                }
                // EM_SETCUEBANNER keeps the box self-explanatory without a separate label.
                SendMessage(box.Handle, 0x1501, IntPtr.Zero, text ?? string.Empty);
            }
            catch
            {
            }
        }

        private static Button MakeButton(string text, bool primary, Size size)
        {
            Button button = new Button
            {
                Text = text,
                Tag = primary,
                UseVisualStyleBackColor = false,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                MinimumSize = size,
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(0, 0, 8, 0)
            };
            button.Size = size;
            return button;
        }

        private static TableLayoutPanel CreatePageLayout()
        {
            // The page lives inside a scroll host. AutoScroll on a TableLayoutPanel does not
            // clip its children, so without the host the cards below simply draw on top of
            // the Safety and status card on a short window.
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 0,
                Padding = new Padding(0, 0, 12, 12),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return page;
        }

        private static void AddPageHeading(TableLayoutPanel page, Label title, Label subtitle)
        {
            var heading = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            title.Dock = DockStyle.Top;
            title.AutoSize = true;
            title.AutoEllipsis = true;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Margin = Padding.Empty;
            subtitle.Dock = DockStyle.Top;
            subtitle.AutoSize = true;
            subtitle.AutoEllipsis = true;
            subtitle.TextAlign = ContentAlignment.MiddleLeft;
            subtitle.Margin = Padding.Empty;
            heading.Controls.Add(title, 0, 0);
            heading.Controls.Add(subtitle, 0, 1);
            AddPageRow(page, heading);
        }

        private static void AddPageRow(TableLayoutPanel page, Control control)
        {
            int row = page.RowCount++;
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, 12);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Controls.Add(control, 0, row);
        }

        private static void AddPageFixedRow(TableLayoutPanel page, Control control, int height)
        {
            int row = page.RowCount++;
            control.Dock = DockStyle.Fill;
            control.Margin = Padding.Empty;
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            page.Controls.Add(control, 0, row);
        }

        private static TableLayoutPanel CreateBodyLayout(int columns, int rows)
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = columns,
                RowCount = rows,
                Padding = new Padding(16, 8, 16, 12),
                Margin = Padding.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            for (int index = 0; index < columns; index++)
            {
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            }
            return body;
        }

        private static void SetBodyRows(TableLayoutPanel body, params int[] heights)
        {
            body.RowStyles.Clear();
            for (int index = 0; index < heights.Length; index++)
            {
                body.RowStyles.Add(heights[index] > 0
                    ? new RowStyle(SizeType.Absolute, heights[index])
                    : new RowStyle(SizeType.AutoSize));
            }
        }

        private static TableLayoutPanel CreateTwoColumnRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            return row;
        }

        private static void ConfigureValueCell(Control control)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = Padding.Empty;
            Label label = control as Label;
            if (label != null)
            {
                label.AutoSize = false;
                label.AutoEllipsis = true;
                label.TextAlign = ContentAlignment.MiddleLeft;
            }
        }

        private static void ConfigureInputCell(Control control)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = Padding.Empty;
        }

        private static Label AddCellLabel(TableLayoutPanel parent, string text, int column, int row)
        {
            Label label = MakeMutedLabel(text);
            label.Dock = DockStyle.Fill;
            label.Margin = Padding.Empty;
            label.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(label, column, row);
            return label;
        }

        private TableLayoutPanel CreatePresetRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            _presetCombo.Dock = DockStyle.Fill;
            _savePresetButton.Dock = DockStyle.Fill;
            _applyPresetButton.Dock = DockStyle.Fill;
            row.Controls.Add(_presetCombo, 0, 0);
            row.Controls.Add(_savePresetButton, 1, 0);
            row.Controls.Add(_applyPresetButton, 2, 0);
            return row;
        }

        private static TableLayoutPanel CreateActionGrid(int columns, params Control[] controls)
        {
            return new ResponsiveActionGrid(columns, controls);
        }

        private void ConfigureSafetyBody(TableLayoutPanel body)
        {
            SetBodyRows(body, 28, 24, 65, 38, 38, 38, 38);
            _allowDhcpIpCheckBox.AutoSize = false;
            _allowDhcpIpCheckBox.Dock = DockStyle.Fill;
            _allowDhcpIpCheckBox.TextAlign = ContentAlignment.MiddleLeft;
            _allowDhcpIpCheckBox.Margin = Padding.Empty;
            _showPublicIpLocationCheckBox.AutoSize = false;
            _showPublicIpLocationCheckBox.Dock = DockStyle.Fill;
            _showPublicIpLocationCheckBox.TextAlign = ContentAlignment.MiddleLeft;
            _showPublicIpLocationCheckBox.Margin = Padding.Empty;
            _safetySummaryLabel.Dock = DockStyle.Fill;
            _safetySummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            _safetySummaryLabel.Margin = Padding.Empty;
            _operationStatusLabel.Dock = DockStyle.Fill;
            _operationStatusLabel.TextAlign = ContentAlignment.TopLeft;
            _operationStatusLabel.Margin = Padding.Empty;
            _startupRandomizeButton.Dock = DockStyle.Fill;
            _startupRandomizeButton.Margin = Padding.Empty;
            _notificationCenterButton.Dock = DockStyle.Fill;
            _notificationCenterButton.Margin = Padding.Empty;
            _ipPreflightButton.Dock = DockStyle.Fill;
            _ipPreflightButton.Margin = Padding.Empty;
            body.Controls.Add(_allowDhcpIpCheckBox, 0, 0);
            body.Controls.Add(_showPublicIpLocationCheckBox, 0, 1);
            body.Controls.Add(_safetySummaryLabel, 0, 2);
            body.Controls.Add(_operationStatusLabel, 0, 3);
            body.Controls.Add(_startupRandomizeButton, 0, 4);
            body.Controls.Add(_notificationCenterButton, 0, 5);
            body.Controls.Add(_ipPreflightButton, 0, 6);
        }

        private static TableLayoutPanel CreateCard(string title, int bodyColumns, int bodyRows, out TableLayoutPanel body)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, 12),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BorderStyle = BorderStyle.None,
                Tag = Color.Gray,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Paint += DrawCardBorder;
            body = CreateBodyLayout(bodyColumns, bodyRows);
            Label titleLabel = MakeMutedLabel(title.ToUpperInvariant());
            titleLabel.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.Margin = Padding.Empty;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(titleLabel, 0, 0);
            card.Controls.Add(body, 0, 1);
            return card;
        }

        private static void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }
    }
}
