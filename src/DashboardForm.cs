using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
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

            // Columns are equal width, so every control must fit one column's share; a row
            // whose total fitted still let one wide button run over its neighbour.
            private bool Fits(int columns, int available)
            {
                int cell = available / columns;
                foreach (Control control in _controls)
                {
                    if (control.MinimumSize.Width + 8 > cell)
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

        /// <summary>
        /// Shown in the Location row while the opt-in is off. The row is always present, so
        /// turning the setting on visibly replaces this text with the real location instead
        /// of the row silently appearing.
        /// </summary>
        private const string PublicIpLocationOptInHint =
            "Off. Enable \"Show public IP location\" on the Safety and settings page.";

        internal const string PublicIpLocationUnavailableHint =
            "The location service could not be reached. Press Refresh to try again.";

        private const string PublicIpLocationLookingUpHint =
            "Looking up your public IP location...";

        /// <summary>Header variant: 220px wide, so no full sentences.</summary>
        internal const string PublicIpLocationUnavailableShort = "Location unavailable";

        /// <summary>
        /// Used when the address itself could not be fetched. Blaming the location service
        /// in that case points the reader at the wrong thing: there is no location to look
        /// up yet, because there is no address.
        /// </summary>
        private const string PublicIpUnavailableHint =
            "The public IP address could not be fetched. Press Refresh to try again.";

        private const string PublicIpUnavailableShort = "Public IP unavailable";

        /// <summary>
        /// The sentinel the tray context writes when an address could not be fetched. Both
        /// sides compare against this, so the two can never drift apart.
        /// </summary>
        public const string PublicIpUnavailable = "Unavailable";

        private const string PublicIpLocationLookingUpShort = "Locating...";

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
        // Not readonly: built by BuildDetailsPane, a method rather than the constructor.
        // The tab strip over the detail pages, and the panel the pages are stacked in.
        private PivotBar _pivot;
        private Panel _pageHost;
        private FluentPalette _palette = FluentPalette.Build(false, false);
        private readonly Label _titleLabel;
        private readonly Label _subtitleLabel;
        private readonly Label _headerPublicIpLabel;
        private readonly Label _headerPublicIpLocationLabel;
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
        private readonly Label _currentMacVendorLabel;
        private readonly Label _networkIpLabel;
        private readonly Label _networkModeLabel;
        private readonly Label _networkLinkLabel;
        private readonly Label _networkDnsLabel;
        private readonly ComboBox _dnsCombo;
        private readonly Button _applyDnsButton;
        private string[] _pendingCustomDnsServers;
        private readonly Label _publicIpLabel;
        private readonly Label _publicIpLocationLabel;
        private TableLayoutPanel _publicIpCell;
        private readonly Label _vpnHintLabel;
        private readonly Label _operationStatusLabel;
        private readonly Label _safetySummaryLabel;

        private AdapterInfo _selectedAdapter;
        private string _lastPublicIpValue;
        private NetworkIdentity _lastNetworkIdentity;
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
        public event EventHandler DnsChangeRequested;
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

        /// <summary>
        /// The DNS servers to apply for the most recent Apply DNS request. Null means
        /// reset to automatic (DHCP). Set by the button click handler before the event
        /// is raised, so the handler can run the custom-entry dialog first.
        /// </summary>
        public string[] PendingDnsServers
        {
            get { return _pendingCustomDnsServers; }
        }

        public int SelectedDnsComboIndex
        {
            get { return _dnsCombo.SelectedIndex; }
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
            // Suspended for the whole constructor, so display scaling runs once, at the end,
            // over the finished layout. Without it the form scaled itself as soon as
            // AutoScaleMode was set, before any child existed, so at 150% the fonts grew but
            // every fixed row height, padding and size added afterwards stayed at 100%, and
            // text was squeezed into rows too short for it.
            SuspendLayout();
            Text = AppInfo.ProductName + " " + AppInfo.Version;
            Icon = LoadApplicationIcon();
            ClientSize = new Size(1120, 720);
            MinimumSize = new Size(980, 620);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            Font = new Font(FluentPalette.BodyFontFamily(), 9F, FontStyle.Regular, GraphicsUnit.Point);
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
            _titleLabel.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold, GraphicsUnit.Point);
            _titleLabel.AutoSize = false;
            _titleLabel.Size = new Size(300, 30);
            _toolTip.SetToolTip(_titleLabel, AppInfo.ProductName + " " + AppInfo.DisplayVersion);
            _subtitleLabel = MakeSubtitle("A safer, TMAC-inspired network adapter changer");
            _subtitleLabel.AutoSize = false;
            _subtitleLabel.Size = new Size(360, 22);
            _toolTip.SetToolTip(_subtitleLabel, "Version " + AppInfo.DisplayVersion);
            _headerPublicIpLabel = MakeMutedLabel("Public IP: checking...");
            // The header is the only part of the window that is never scrolled or collapsed,
            // and the "Public IP and VPN" card that also shows the address sits at the bottom
            // of a scroll area whose viewport is a fraction of its content. A location that
            // is only shown down there is a location nobody finds, so the header carries it
            // too, on the same stacked pattern the title and subtitle already use.
            _headerPublicIpLocationLabel = MakeMutedLabel(string.Empty);
            _headerPublicIpLocationLabel.AutoSize = false;
            _headerPublicIpLocationLabel.AutoEllipsis = true;
            _adapterCountLabel = MakeMutedLabel("0 adapters");
            _listHintLabel = MakeMutedLabel("Select an adapter to view its details.");
            _selectedTitleLabel = MakeTitle("No adapter selected");
            _selectedTitleLabel.AutoSize = false;
            _selectedTitleLabel.AutoEllipsis = true;
            _selectedTitleLabel.Size = new Size(700, 30);
            _selectedTitleLabel.Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point);
            _selectedStatusLabel = MakeMutedLabel("Connect or enable a physical network adapter.");
            _currentMacLabel = MakeValueLabel("-");
            _permanentMacLabel = MakeValueLabel("-");
            // Read-only vendor lookup for the adapter's current MAC. The OUI database is
            // bundled, so this needs no network and no consent.
            _currentMacVendorLabel = MakeMutedLabel("Vendor: —");
            _networkIpLabel = MakeValueLabel("—");
            _networkModeLabel = MakeValueLabel("—");
            _networkLinkLabel = MakeMutedLabel("—");
            _networkDnsLabel = MakeMutedLabel("—");
            _dnsCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _dnsCombo.Items.AddRange(new object[]
            {
                "DHCP (automatic)",
                "Google (8.8.8.8, 8.8.4.4)",
                "Cloudflare (1.1.1.1, 1.0.0.1)",
                "Quad9 (9.9.9.9, 149.112.112.112)",
                "OpenDNS (208.67.222.222, 208.67.220.220)",
                "Custom..."
            });
            _dnsCombo.SelectedIndex = 0;
            _applyDnsButton = MakeButton("Apply DNS", false, new Size(96, 28));
            _publicIpLabel = MakeValueLabel(PublicIpUnavailable);
            // The location used to exist only as a tooltip on the public IP label, which made
            // an opt-in feature look permanently broken: nothing on screen changed when it
            // was enabled. It is now a row of its own, in the same card as the address.
            _publicIpLocationLabel = MakeMutedLabel(PublicIpLocationOptInHint);
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
                // The row has to react in the same click. Leaving it showing the previous
                // wording until some later refresh lands is what made this read as broken:
                // a row that never visibly changes is indistinguishable from one that is
                // not wired to the checkbox at all.
                if (_showPublicIpLocationCheckBox.Checked)
                {
                    _publicIpLocationLabel.Text = PublicIpLocationLookingUpHint;
                    _headerPublicIpLocationLabel.Text = PublicIpLocationLookingUpShort;
                    _toolTip.SetToolTip(_publicIpLocationLabel, PublicIpLocationLookingUpHint);
                    _toolTip.SetToolTip(_headerPublicIpLocationLabel, PublicIpLocationLookingUpHint);
                }
                else
                {
                    RefreshPublicIpLocationRow();
                }

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
            // Taller rows, as in a Fluent list. A details ListView takes its row height from
            // the small image list, and an image list is not display-scaled, so the height is
            // scaled here by hand.
            _adapterList.SmallImageList = new ImageList
            {
                ImageSize = new Size(1, Math.Max(16, (int)Math.Round(30 * CurrentAutoScaleDimensions.Height / 96F)))
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
            foreach (Control control in EnumerateControls(this))
            {
                Button button = control as Button;
                if (button != null && !(button.Parent is PivotBar))
                {
                    button.EnabledChanged += delegate { StyleButton(button); };
                }
            }
            ApplyHeaderAccessibility();
            ApplyActionAccessibility();
            ApplyTabOrder();
            ApplyTheme(darkMode);
            UpdateActionStates();
            ResumeLayout(false);
            PerformLayout();
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
                // The Location row's wording depends on this setting, so restoring the
                // preference at startup has to re-render it or the row would keep the
                // "off" wording until the next refresh.
                RefreshPublicIpLocationRow();
            }
        }

        private void RefreshPublicIpLocationRow()
        {
            if (_publicIpLocationLabel == null)
            {
                return;
            }
            SetPublicIp(_lastPublicIpValue, _lastNetworkIdentity);
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
            _lastPublicIpValue = value;
            _lastNetworkIdentity = networkIdentity;
            string display = string.IsNullOrWhiteSpace(value) ? PublicIpUnavailable : value;
            _publicIpLabel.Text = display;
            _headerPublicIpLabel.Text = "Public IP: " + display;

            string tooltip = "Public IP: " + display;

            // The opt-in is read before the location data is used, and the data is only
            // shown while it is on. A refresh in flight when the box is unticked can still
            // hand over a populated identity, and displaying that would leave the location
            // sitting on screen after the user had asked for it to stop. Turning a privacy
            // setting off has to take effect immediately, not at the next refresh.
            bool optedIn = _showPublicIpLocationCheckBox != null && _showPublicIpLocationCheckBox.Checked;

            // Two different services are involved and they fail independently. If the
            // address never arrived there is nothing to look up, so reporting that the
            // location service failed sends the reader after the wrong one.
            bool addressMissing = display == PublicIpUnavailable;

            var geoParts = new List<string>();
            if (optedIn && networkIdentity != null)
            {
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
            }

            if (geoParts.Count > 0)
            {
                tooltip += Environment.NewLine + string.Join(Environment.NewLine, geoParts);
            }

            // "Not switched on" and "switched on but the lookup failed" are different
            // problems with different fixes, so the row must not collapse them into one
            // message.
            string locationText;
            if (geoParts.Count > 0)
            {
                locationText = DescribePublicIpLocation(networkIdentity, geoParts);
            }
            else if (addressMissing)
            {
                locationText = PublicIpUnavailableHint;
            }
            else if (optedIn)
            {
                locationText = PublicIpLocationUnavailableHint;
            }
            else
            {
                locationText = PublicIpLocationOptInHint;
            }

            _publicIpLocationLabel.Text = locationText;
            SizeTextLabelsToFit();
            // The header gets a shorter form: the same facts, minus the provider, because
            // the column is 220px and a truncated line helps nobody. The full text is on
            // the tooltip and in the card below.
            _headerPublicIpLocationLabel.Text = geoParts.Count > 0
                ? DescribePublicIpLocationShort(networkIdentity, geoParts)
                : (addressMissing ? PublicIpUnavailableShort
                    : optedIn ? PublicIpLocationUnavailableShort : string.Empty);
            _toolTip.SetToolTip(_publicIpLabel, tooltip);
            _toolTip.SetToolTip(_headerPublicIpLabel, tooltip);
            _toolTip.SetToolTip(_headerPublicIpLocationLabel, geoParts.Count > 0
                ? "Public IP location, from ipinfo.io." + Environment.NewLine
                    + string.Join(Environment.NewLine, geoParts)
                : locationText);
            _toolTip.SetToolTip(_publicIpLocationLabel, geoParts.Count > 0
                ? "Public IP location, from ipinfo.io." + Environment.NewLine
                    + string.Join(Environment.NewLine, geoParts)
                : locationText);
        }

        /// <summary>
        /// The header form of the location: place and network number, no provider name.
        /// </summary>
        internal static string DescribePublicIpLocationShort(NetworkIdentity identity, List<string> geoParts)
        {
            if (geoParts == null || geoParts.Count == 0)
            {
                return PublicIpLocationUnavailableShort;
            }

            string text = DescribePublicIpPlace(identity);
            if (text == "Location unknown")
            {
                text = "Location unknown";
            }

            string asn = identity == null ? null : identity.PublicIpAsn;
            if (!string.IsNullOrWhiteSpace(asn))
            {
                text += "  (" + asn + ")";
            }
            return text;
        }

        /// <summary>
        /// The text of the Location row.
        ///
        /// This exists because the feature used to report its result only as a tooltip, and
        /// a tooltip is not something a user reads looking for the answer. Enabling the
        /// setting produced no visible change on screen, which reads exactly like a broken
        /// feature. The row is always present and always says which of the three things is
        /// true: not opted in, opted in but the lookup failed, or the actual location.
        /// </summary>
        internal static string DescribePublicIpLocation(NetworkIdentity identity, List<string> geoParts)
        {
            if (geoParts == null || geoParts.Count == 0)
            {
                return PublicIpLocationUnavailableHint;
            }

            string text = DescribePublicIpPlace(identity);

            string isp = identity == null ? null : identity.PublicIpIsp;
            string asn = identity == null ? null : identity.PublicIpAsn;
            if (!string.IsNullOrWhiteSpace(isp))
            {
                text += "  -  " + (string.IsNullOrWhiteSpace(asn) ? isp : isp + " (" + asn + ")");
            }
            else if (!string.IsNullOrWhiteSpace(asn))
            {
                text += "  -  " + asn;
            }

            return text;
        }

        /// <summary>
        /// "City, Region, Country", skipping whatever the service did not return. Place comes
        /// first because that is what a person is actually asking for; the network operator
        /// is a detail, and in the header there is no room for it.
        /// </summary>
        internal static string DescribePublicIpPlace(NetworkIdentity identity)
        {
            var place = new List<string>();
            Action<string> add = delegate(string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    place.Add(value.Trim());
                }
            };
            add(identity == null ? null : identity.PublicIpCity);
            add(identity == null ? null : identity.PublicIpRegion);
            add(identity == null ? null : identity.PublicIpCountry);
            return place.Count > 0 ? string.Join(", ", place.ToArray()) : "Location unknown";
        }

        /// <summary>
        /// Gives the four public IP labels exactly the height their text needs.
        ///
        /// Heights are never written as literals. Two separate clipping bugs came from
        /// doing that: the header lines were given 20 and 16 pixels when the font wanted 21
        /// and 18, and the card cell was given a 24 pixel row when the value wanted 25. An
        /// overlap audit cannot see either, because a label clipped inside its own bounds
        /// still has a perfectly good rectangle.
        ///
        /// It is also not enough to size these labels where they are built. The value labels
        /// set their own font in their factory, but the header labels inherit the form
        /// default, which is not final until the theme has run. Measuring here means both
        /// kinds of label are measured against the font that is actually in effect.
        /// </summary>
        private void SizeTextLabelsToFit()
        {
            var labels = new[]
            {
                _headerPublicIpLabel, _headerPublicIpLocationLabel, _publicIpLabel, _publicIpLocationLabel,
                _selectedTitleLabel, _selectedStatusLabel, _titleLabel, _subtitleLabel
            };
            foreach (Label label in labels)
            {
                if (label == null || !label.Visible && label.Height == 0)
                {
                    continue;
                }
                int needed = label.PreferredHeight;
                if (needed > 0 && label.Height != needed)
                {
                    label.Height = needed;
                }
            }

            // The card's cell holds the two value labels stacked, so its rows have to
            // follow them or the second line is pushed out of the card.
            if (_publicIpCell != null && _publicIpCell.RowCount == 2)
            {
                for (int row = 0; row < 2; row++)
                {
                    Control control = _publicIpCell.GetControlFromPosition(0, row);
                    Label label = control as Label;
                    if (label == null)
                    {
                        continue;
                    }
                    int needed = label.PreferredHeight;
                    if (needed > 0 && _publicIpCell.RowStyles[row].Height != needed)
                    {
                        _publicIpCell.RowStyles[row] = new RowStyle(SizeType.Absolute, needed);
                    }
                }
            }
        }

        public void SetStatus(string value)
        {
            string status = value ?? string.Empty;
            _operationStatusLabel.Text = status;
            _toolTip.SetToolTip(_operationStatusLabel, status);
        }

        /// <summary>
        /// The DNS servers implied by the current selection of the DNS combo, or null
        /// for "DHCP (automatic)", which resets the adapter to automatic DNS. Returns
        /// null when the user cancels the custom entry dialog.
        /// </summary>
        public string[] GetSelectedDnsServers()
        {
            switch (_dnsCombo.SelectedIndex)
            {
                case 0: return null; // DHCP
                case 1: return new[] { "8.8.8.8", "8.8.4.4" };
                case 2: return new[] { "1.1.1.1", "1.0.0.1" };
                case 3: return new[] { "9.9.9.9", "149.112.112.112" };
                case 4: return new[] { "208.67.222.222", "208.67.220.220" };
                case 5:
                    string input = PromptForCustomDns();
                    if (input == null)
                    {
                        return null;
                    }
                    return ParseDnsList(input);
                default: return null;
            }
        }

        private string PromptForCustomDns()
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Custom DNS servers";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(420, 120);
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;

                Label label = new Label
                {
                    Text = "Enter one or more DNS server addresses, separated by commas or spaces.",
                    Dock = DockStyle.Top,
                    Height = 36,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(12, 8, 12, 0)
                };
                TextBox box = new TextBox { Dock = DockStyle.Top, Margin = new Padding(12, 4, 12, 4) };
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(8) };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                dialog.Controls.Add(buttons);
                dialog.Controls.Add(box);
                dialog.Controls.Add(label);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return null;
                }
                return box.Text;
            }
        }

        private static string[] ParseDnsList(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }
            var servers = new List<string>();
            foreach (string part in input.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                IPAddress parsed;
                if (IPAddress.TryParse(trimmed, out parsed))
                {
                    servers.Add(trimmed);
                }
            }
            return servers.Count == 0 ? null : servers.ToArray();
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

        private const int WM_ENDSESSION = 0x0016;

        /// <summary>
        /// Set while a restore runs because the Windows session is ending. That restore
        /// reports through this form, and WinForms disposes a form when the session ends,
        /// which made the restore fail before it reached the adapter. The process is ended
        /// by Windows straight afterwards in any case, so nothing is left open.
        /// </summary>
        internal bool HoldOpenAtSessionEnd { get; set; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ENDSESSION && HoldOpenAtSessionEnd)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
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
            // Two columns: the adapter list on the window background, and the content layer
            // holding, top to bottom, the pending banner, the selected adapter's header, the
            // page tabs, the pages, and the status line.
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _pendingBanner = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false, Padding = new Padding(14, 8, 12, 8), Margin = new Padding(24, 16, 24, 0) };
            // The message has a row of its own above the buttons: beside them, in the
            // narrower content column, it wrapped onto more lines than the row allowed.
            var bannerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            bannerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bannerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _pendingBannerLabel = MakeMutedLabel(string.Empty);
            // Sized from its text. A fixed-size label in this auto-sized row made the row
            // as tall as the label happened to be, and display scaling then enlarged that
            // arbitrary height instead of the text.
            _pendingBannerLabel.AutoSize = true;
            _pendingBannerLabel.AutoEllipsis = false;
            _pendingBannerLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            _pendingBannerLabel.TextAlign = ContentAlignment.MiddleLeft;
            _pendingBannerLabel.Margin = new Padding(0, 0, 0, 8);
            // An auto-sized label only wraps within a maximum width, so the width follows
            // the banner.
            bannerLayout.SizeChanged += (sender, args) =>
            {
                int width = Math.Max(1, bannerLayout.ClientSize.Width - bannerLayout.Padding.Horizontal);
                if (_pendingBannerLabel.MaximumSize.Width != width)
                {
                    _pendingBannerLabel.MaximumSize = new Size(width, 0);
                }
            };
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
            bannerLayout.SetColumnSpan(_pendingBannerLabel, 4);
            bannerLayout.Controls.Add(_keepChangeButton, 1, 1);
            bannerLayout.Controls.Add(restoreAllButton, 2, 1);
            bannerLayout.Controls.Add(bannerDismissButton, 3, 1);
            _pendingBanner.Controls.Add(bannerLayout);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = Padding.Empty, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            // A fixed row rather than one sized from the label: the status text changes with
            // every operation, and a row that followed it would move the pages above.
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            content.Controls.Add(_pendingBanner, 0, 0);
            content.Controls.Add(_headerPanel, 0, 1);
            _pivot.Dock = DockStyle.Fill;
            _pivot.Margin = new Padding(24, 8, 24, 0);
            content.Controls.Add(_pivot, 0, 2);
            content.Controls.Add(_pageHost, 0, 3);
            _operationStatusLabel.Dock = DockStyle.Fill;
            _operationStatusLabel.AutoEllipsis = true;
            _operationStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _operationStatusLabel.Margin = new Padding(24, 0, 24, 0);
            content.Controls.Add(_operationStatusLabel, 0, 4);
            _detailsPanel.Controls.Add(content);

            root.Controls.Add(_listPanel, 0, 0);
            root.Controls.Add(_detailsPanel, 1, 0);
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
            Accessibility.Describe(_dnsCombo, "DNS server selection", AccessibleRole.ComboBox,
                "Chooses which DNS servers are used by the selected adapter. Presets for common " +
                "providers, or a custom list.");
            Accessibility.Describe(_applyDnsButton, "Apply DNS servers", AccessibleRole.PushButton,
                "Applies the selected DNS servers to the selected adapter. The original servers " +
                "are restored on exit.");
            Accessibility.Describe(_currentMacVendorLabel, "MAC address vendor", AccessibleRole.Text,
                "The manufacturer registered to the first three bytes of the current MAC address, " +
                "from the bundled IEEE OUI list. Read-only; no network access is used.");
            Accessibility.Describe(_headerPublicIpLocationLabel, "Public IP location", AccessibleRole.Text,
                "The approximate city, region, and country of the public IP address, with the " +
                "network number. Empty until you enable \"Show public IP location\" in Safety " +
                "and settings, which sends the address to ipinfo.io.");
            Accessibility.Describe(_publicIpLocationLabel, "Public IP location", AccessibleRole.Text,
                "The approximate city, region, and country of the public IP address, with the " +
                "internet provider and its network number. Shown only after you enable " +
                "\"Show public IP location\" on the Safety and settings page, which sends the address to " +
                "ipinfo.io.");
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
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(24, 12, 24, 0),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            // The selected adapter's name and status, then the public IP and Refresh. The
            // app title moved to the top of the adapter list.
            // Shares, not a fixed 220px IP column: in a narrow window at 200% the fixed
            // column took the whole width and squeezed the adapter name to 1px under it.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
            // Not AutoSize: an adapter name longer than the column would widen an auto-sized
            // label over the public IP. Fixed width with an ellipsis, and the height is set
            // from the font in SizeTextLabelsToFit.
            _selectedTitleLabel.Dock = DockStyle.Top;
            _selectedTitleLabel.AutoSize = false;
            _selectedTitleLabel.AutoEllipsis = true;
            _selectedTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _selectedTitleLabel.Margin = Padding.Empty;
            _selectedStatusLabel.Dock = DockStyle.Top;
            _selectedStatusLabel.AutoSize = false;
            _selectedStatusLabel.AutoEllipsis = true;
            _selectedStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _selectedStatusLabel.Margin = Padding.Empty;
            titleLayout.Controls.Add(_selectedTitleLabel, 0, 0);
            titleLayout.Controls.Add(_selectedStatusLabel, 0, 1);
            layout.Controls.Add(titleLayout, 0, 0);
            _headerPublicIpLabel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _headerPublicIpLabel.Size = new Size(180, 28);
            _headerPublicIpLabel.TextAlign = ContentAlignment.MiddleRight;

            // Address above, location below, in one cell so the edge of the window can never
            // show the address without the location sitting under it. Shaped like titleLayout
            // above, which is the one nested table in this header that is known to size
            // itself: AutoSize has to be on, because a Dock=Top table with AutoSize off is
            // never given a height and falls back to its default 100px, which pushed the
            // location line half a header away from the address.
            var headerPublicIpLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            headerPublicIpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerPublicIpLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            headerPublicIpLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // No height is set on either label. Fixed pixel heights are wrong at
            // every display scaling except the one they were written at, and reading
            // PreferredHeight here is no better because this runs before the theme has
            // applied its fonts, so it would measure the wrong font. Both rows are
            // SizeType.AutoSize instead, which makes the table measure each label
            // against whatever font is actually in effect when the layout runs.
            _headerPublicIpLabel.Anchor = AnchorStyles.None;
            _headerPublicIpLabel.Dock = DockStyle.Top;
            _headerPublicIpLabel.AutoSize = false;
            _headerPublicIpLabel.AutoEllipsis = true;
            _headerPublicIpLabel.Margin = Padding.Empty;
            _headerPublicIpLabel.TextAlign = ContentAlignment.MiddleRight;
            _headerPublicIpLocationLabel.Anchor = AnchorStyles.None;
            _headerPublicIpLocationLabel.Dock = DockStyle.Top;
            _headerPublicIpLocationLabel.AutoSize = false;
            _headerPublicIpLocationLabel.AutoEllipsis = true;
            _headerPublicIpLocationLabel.Margin = Padding.Empty;
            _headerPublicIpLocationLabel.TextAlign = ContentAlignment.MiddleRight;
            headerPublicIpLayout.Controls.Add(_headerPublicIpLabel, 0, 0);
            headerPublicIpLayout.Controls.Add(_headerPublicIpLocationLabel, 0, 1);
            headerPublicIpLayout.Margin = new Padding(12, 4, 12, 0);
            layout.Controls.Add(headerPublicIpLayout, 1, 0);
            _refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _refreshButton.Margin = new Padding(0, 4, 0, 0);
            layout.Controls.Add(_refreshButton, 2, 0);
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
            // Reading order of the new layout: the adapter list column, the header's Refresh,
            // then each page's controls in page order. Dark mode now sits on the last page.
            sequence.AddRange(new Control[]
            {
                _adapterSearchBox,
                _connectedOnlyCheckBox,
                _adapterList,
                _refreshButton,
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
                _ipPreflightButton,
                _darkModeCheckBox,
                _startupRandomizeButton,
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
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16, 12, 16, 12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            // Sized to the filter row's contents. A fixed height clipped the bottom of the
            // Star selected button, which sizes itself to its text, at 150% scaling.
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // The app title and description head the adapter list, as a Fluent app's name
            // heads its navigation pane. Heights come from the font, in SizeTextLabelsToFit.
            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = new Padding(0, 0, 0, 10),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _titleLabel.Dock = DockStyle.Top;
            _titleLabel.AutoEllipsis = true;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _titleLabel.Margin = Padding.Empty;
            _subtitleLabel.Dock = DockStyle.Top;
            _subtitleLabel.AutoEllipsis = true;
            _subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _subtitleLabel.Margin = Padding.Empty;
            titleStack.Controls.Add(_titleLabel, 0, 0);
            titleStack.Controls.Add(_subtitleLabel, 0, 1);
            layout.Controls.Add(titleStack, 0, 0);

            Label title = MakeMutedLabel("Physical adapters");
            title.AutoSize = false;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
            layout.Controls.Add(title, 0, 3);

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

            var filterRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 4) };
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
                if (SelectedAdapter != null && ToggleFavoriteRequested != null)
                {
                    ToggleFavoriteRequested(this, EventArgs.Empty);
                }
            };
            filterRow.Controls.Add(_connectedOnlyCheckBox);
            filterRow.Controls.Add(_favoriteButton);
            layout.Controls.Add(filterRow, 0, 2);

            layout.Controls.Add(_adapterList, 0, 4);
            _listHintLabel.Dock = DockStyle.Fill;
            _listHintLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(_listHintLabel, 0, 5);
            _licenseLabel = MakeMutedLabel(LicenseInfo.Notice);
            _licenseLabel.Dock = DockStyle.Fill;
            _licenseLabel.TextAlign = ContentAlignment.MiddleLeft;
            _licenseLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            _toolTip.SetToolTip(_licenseLabel, "Open the tray menu and choose License to read the full " + LicenseInfo.Name + ".");
            layout.Controls.Add(_licenseLabel, 0, 6);
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
            // One page per card. The selected adapter's name and status moved to the header,
            // which every page shares.
            TableLayoutPanel macPage = CreatePageLayout();
            TableLayoutPanel networkPage = CreatePageLayout();
            TableLayoutPanel vpnPage = CreatePageLayout();
            TableLayoutPanel detailsPage = CreatePageLayout();
            TableLayoutPanel settingsPage = CreatePageLayout();

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
            AddPageRow(detailsPage, adapterCard);

            TableLayoutPanel macBody;
            TableLayoutPanel macCard = CreateCard("MAC address", 2, 6, out macBody);
            SetBodyRows(macBody, 22, 30, 22, 34, 38, -1);
            AddCellLabel(macBody, "Current MAC", 0, 0);
            AddCellLabel(macBody, "Permanent MAC", 1, 0);
            ConfigureValueCell(_currentMacLabel);
            ConfigureValueCell(_permanentMacLabel);
            ConfigureValueCell(_currentMacVendorLabel);
            macBody.Controls.Add(_currentMacLabel, 0, 1);
            macBody.Controls.Add(_permanentMacLabel, 1, 1);
            macBody.Controls.Add(_currentMacVendorLabel, 1, 2);
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
            AddPageRow(macPage, macCard);

            TableLayoutPanel networkBody;
            TableLayoutPanel networkCard = CreateCard("Local network", 2, 5, out networkBody);
            SetBodyRows(networkBody, 22, 30, 30, 28, -1);
            AddCellLabel(networkBody, "IPv4 address", 0, 0);
            AddCellLabel(networkBody, "Mode", 1, 0);
            ConfigureValueCell(_networkIpLabel);
            ConfigureValueCell(_networkModeLabel);
            networkBody.Controls.Add(_networkIpLabel, 0, 1);
            networkBody.Controls.Add(_networkModeLabel, 1, 1);
            AddCellLabel(networkBody, "Link", 0, 2);
            ConfigureValueCell(_networkLinkLabel);
            networkBody.Controls.Add(_networkLinkLabel, 1, 2);
            AddCellLabel(networkBody, "DNS", 0, 3);
            _dnsCombo.Dock = DockStyle.Fill;
            _dnsCombo.Margin = Padding.Empty;
            _networkDnsLabel.Dock = DockStyle.Fill;
            _networkDnsLabel.TextAlign = ContentAlignment.MiddleLeft;
            _networkDnsLabel.Margin = Padding.Empty;
            networkBody.Controls.Add(_dnsCombo, 1, 3);
            // IP preflight moved here from the old Safety and status card: it reports what
            // a local IP change would do, so it belongs beside the button that makes one.
            TableLayoutPanel networkActions = CreateActionGrid(2, _randomizeIpButton, _applyDnsButton, _ipPreflightButton);
            networkBody.Controls.Add(networkActions, 0, 4);
            networkBody.SetColumnSpan(networkActions, 2);
            AddPageRow(networkPage, networkCard);

            TableLayoutPanel vpnBody;
            TableLayoutPanel vpnCard = CreateCard("Public IP and VPN", 2, 4, out vpnBody);
            SetBodyRows(vpnBody, 22, -1, 40, -1);
            AddCellLabel(vpnBody, "Public IP", 0, 0);
            Label vpnProfileLabel = AddCellLabel(vpnBody, "Windows VPN profile", 1, 0);
            vpnProfileLabel.Dock = DockStyle.Fill;
            ConfigureValueCell(_publicIpLabel);
            ConfigureValueCell(_publicIpLocationLabel);

            // The address and its location share one cell, stacked. Given a row of their own
            // they could be split by the edge of the scrolling details pane at some window
            // sizes, which puts the location below the fold while the address stays visible.
            // Keeping them in one cell makes that impossible, and the two lines read together
            // anyway.
            _publicIpCell = new TableLayoutPanel
                {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            _publicIpCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // AutoSize, for the same reason the header stack uses it and for the same
            // reason it must not be percent: a percent row style inside a card that sizes
            // to its own content has no defined preferred height, and the card's AutoSize
            // then grows to fit it, which pushed the whole card below the fold of the
            // scrolling details pane and hid the public IP along with it.
            _publicIpCell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _publicIpCell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _publicIpCell.Controls.Add(_publicIpLabel, 0, 0);
            _publicIpCell.Controls.Add(_publicIpLocationLabel, 0, 1);

            _vpnCombo.Dock = DockStyle.Fill;
            _vpnCombo.Margin = Padding.Empty;
            _vpnHintLabel.Dock = DockStyle.Fill;
            _vpnHintLabel.TextAlign = ContentAlignment.TopLeft;
            _vpnHintLabel.Margin = new Padding(0, 8, 0, 0);
            vpnBody.Controls.Add(_publicIpCell, 0, 1);
            vpnBody.Controls.Add(_vpnCombo, 1, 1);
            vpnBody.Controls.Add(_vpnHintLabel, 1, 2);
            vpnBody.Controls.Add(CreateActionGrid(2, _connectVpnButton, _disconnectVpnButton), 0, 3);
            AddPageRow(vpnPage, vpnCard);

            TableLayoutPanel safetyBody;
            TableLayoutPanel safetyCard = CreateCard("Safety and settings", 1, 6, out safetyBody);
            ConfigureSafetyBody(safetyBody);
            AddPageRow(settingsPage, safetyCard);

            // Each page sits in its own clipping scroll host: AutoScroll on a TableLayoutPanel
            // does not clip its children, so a page taller than the window would draw over
            // the status line below it.
            _pageHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };
            _pivot = new PivotBar();
            AddDetailPage("MAC address", macPage);
            AddDetailPage("Local network", networkPage);
            AddDetailPage("Public IP and VPN", vpnPage);
            AddDetailPage("Adapter details", detailsPage);
            AddDetailPage("Safety and settings", settingsPage);
        }

        private void AddDetailPage(string label, TableLayoutPanel page)
        {
            page.Padding = new Padding(24, 12, 24, 12);
            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            scrollHost.Controls.Add(page);
            _pageHost.Controls.Add(scrollHost);
            _pivot.AddTab(label, scrollHost);
        }

        private void WireEvents()
        {
            _refreshButton.Click += (sender, args) => Raise(RefreshRequested);
            _randomizeBothButton.Click += (sender, args) => Raise(RandomizeBothRequested);
            _randomizeMacButton.Click += (sender, args) => Raise(RandomizeMacRequested);
            _applyMacButton.Click += (sender, args) => Raise(ManualMacRequested);
            _restorePermanentButton.Click += (sender, args) => Raise(RestorePermanentRequested);
            _randomizeIpButton.Click += (sender, args) => Raise(RandomizeIpRequested);
            _applyDnsButton.Click += (sender, args) =>
            {
                switch (_dnsCombo.SelectedIndex)
                {
                    case 0: _pendingCustomDnsServers = null; break; // DHCP
                    case 1: _pendingCustomDnsServers = new[] { "8.8.8.8", "8.8.4.4" }; break;
                    case 2: _pendingCustomDnsServers = new[] { "1.1.1.1", "1.0.0.1" }; break;
                    case 3: _pendingCustomDnsServers = new[] { "9.9.9.9", "149.112.112.112" }; break;
                    case 4: _pendingCustomDnsServers = new[] { "208.67.222.222", "208.67.220.220" }; break;
                    case 5:
                        string input = PromptForCustomDns();
                        if (input == null)
                        {
                            return; // cancelled
                        }
                        string[] parsed = ParseDnsList(input);
                        if (parsed == null)
                        {
                            MessageBox.Show(this, "No valid DNS server addresses were entered.", "Custom DNS",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        _pendingCustomDnsServers = parsed;
                        break;
                    default: _pendingCustomDnsServers = null; break;
                }
                Raise(DnsChangeRequested);
            };
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
                _currentMacLabel.Text = "-";
            _currentMacVendorLabel.Text = "Vendor: —";
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
            _currentMacVendorLabel.Text = "Vendor: " + OuiLookup.Lookup(adapter.MacAddress);
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
            _toolTip.SetToolTip(_currentMacVendorLabel, "OUI vendor for the current MAC address: " + OuiLookup.Lookup(adapter.MacAddress));
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
                return ParseThemePreference(File.Exists(_themePath) ? File.ReadAllText(_themePath) : null);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Dark unless the saved text says "light"; a missing or unreadable file means dark.</summary>
        internal static bool ParseThemePreference(string saved)
        {
            return !string.Equals((saved ?? "").Trim(), "light", StringComparison.OrdinalIgnoreCase);
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
                    _listMutedColor,
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
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
                if (selected)
                {
                    // Fluent's selection indicator: a short accent bar on the row's left edge.
                    float scale = FluentScale.For(_adapterList);
                    int barWidth = Math.Max(2, (int)Math.Round(3 * scale));
                    int inset = Math.Max(2, e.Bounds.Height / 4);
                    using (SolidBrush bar = new SolidBrush(_palette.Accent))
                    {
                        e.Graphics.FillRectangle(bar, e.Bounds.Left, e.Bounds.Top + inset, barWidth, Math.Max(1, e.Bounds.Height - 2 * inset));
                    }
                }
                int x = e.Bounds.Left + (int)Math.Round(6 * FluentScale.For(_adapterList));
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
            FluentPalette p = FluentPalette.Build(highContrast, dark);
            _palette = p;

            // Under high contrast every token is already a system colour (FluentPalette maps
            // them from Accessibility.BuildPalette), and muted text is full-contrast text.
            Color background = p.Background;
            Color layer = p.Layer;
            Color input = p.Input;
            Color text = p.Text;
            Color secondary = p.TextSecondary;
            Color accent = p.Accent;
            Color border = p.ControlBorder;
            _highContrastActive = highContrast;
            _listInputColor = background;
            _listHeaderColor = background;
            _listTextColor = text;
            _listMutedColor = secondary;
            _listSelectedColor = SelectedRowFill(highContrast, p);
            _listSelectedTextColor = SelectedRowText(highContrast, p);
            ApplyControlTheme(this, background, layer, input, text, secondary, accent, border, background);
            BackColor = background;
            _listPanel.BackColor = background;
            if (!highContrast)
            {
                // The list blends into its pane, as a Fluent list does.
                _adapterList.BackColor = background;
            }
            _detailsPanel.BackColor = layer;
            _headerPanel.BackColor = layer;
            _pageHost.BackColor = layer;
            if (!highContrast)
            {
                _pendingBanner.BackColor = p.InfoBackground;
                _pendingBannerLabel.ForeColor = text;
            }
            _pivot.ApplyPalette(p);
            _adapterList.Invalidate();
            FluentChrome.Apply(this, dark && !highContrast);
            _titleLabel.ForeColor = text;
            _selectedTitleLabel.ForeColor = text;
            _subtitleLabel.ForeColor = secondary;
            _licenseLabel.ForeColor = secondary;
            _headerPublicIpLabel.ForeColor = secondary;
            _headerPublicIpLocationLabel.ForeColor = secondary;
            _currentMacLabel.ForeColor = accent;
            _currentMacVendorLabel.ForeColor = secondary;
            _publicIpLabel.ForeColor = accent;
            _publicIpLocationLabel.ForeColor = secondary;
            // Fonts are final by this point, which is the only moment these labels can be
            // measured against the font actually in effect.
            SizeTextLabelsToFit();
            _operationStatusLabel.ForeColor = text;
            if (!themeSaved && IsHandleCreated)
            {
                _operationStatusLabel.Text = "Theme changed for this session, but the preference could not be saved.";
                _toolTip.SetToolTip(_operationStatusLabel, _operationStatusLabel.Text);
            }
        }

        private void ApplyControlTheme(Control control, Color background, Color surface, Color input, Color text, Color secondary, Color accent, Color border, Color listHeader)
        {
            // The tab strip styles itself from the palette (ApplyTheme calls ApplyPalette);
            // the generic button styling below would give its tabs borders and fills.
            if (control is PivotBar)
            {
                return;
            }
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
                if (control is ComboBox)
                {
                    ((ComboBox)control).FlatStyle = FlatStyle.Standard;
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
                // Card titles sit on the card's own fill; every other label is transparent
                // over a solid parent. Bold labels (values, titles) read as primary text.
                control.BackColor = control.Tag == FluentCard.TitleTag ? _palette.Card : Color.Transparent;
                control.ForeColor = control.Font.Bold ? text : secondary;
            }
            else if (control is Button)
            {
                StyleButton((Button)control);
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
                // Flat, because a visual-styles drop-down list ignores BackColor and stayed
                // white in dark mode.
                ComboBox combo = (ComboBox)control;
                combo.FlatStyle = FlatStyle.Flat;
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
                // A card shows the layer at its rounded corners and paints its own fill;
                // its body is solid card colour so the labels in it need no transparency
                // chain through the rounded painting.
                if (control.Tag == FluentCard.CardTag)
                {
                    control.BackColor = surface;
                    control.Invalidate();
                }
                else if (control.Tag == FluentCard.BodyTag)
                {
                    control.BackColor = _palette.Card;
                }
                else
                {
                    control.BackColor = Color.Transparent;
                }
            }
            else if (control is Panel)
            {
                control.BackColor = surface;
                control.ForeColor = text;
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

        /// <summary>
        /// Fluent button colours. A disabled primary button drops the accent fill: Windows
        /// draws disabled text grey, which was unreadable on the accent colour.
        /// </summary>
        // Under high contrast the Fluent selection fill can equal the window colour, so the
        // selected row uses the system Highlight pair, as it did before the redesign.
        internal static Color SelectedRowFill(bool highContrast, FluentPalette p)
        {
            return highContrast ? SystemColors.Highlight : p.Selected;
        }

        internal static Color SelectedRowText(bool highContrast, FluentPalette p)
        {
            return highContrast ? SystemColors.HighlightText : p.Text;
        }

        private void StyleButton(Button button)
        {
            if (!_highContrastActive)
            {
                FluentButton.Style(button, _palette);
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
            // the status line on a short window.
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

        private static void AddPageRow(TableLayoutPanel page, Control control)
        {
            int row = page.RowCount++;
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 0, 0, 12);
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            // The operation status moved to the status line under the pages, and IP preflight
            // to the Local network page. Dark mode moved here from the old header.
            SetBodyRows(body, 28, 28, 65, 28, 38, 38);
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
            _darkModeCheckBox.AutoSize = false;
            _darkModeCheckBox.Dock = DockStyle.Fill;
            _darkModeCheckBox.TextAlign = ContentAlignment.MiddleLeft;
            _darkModeCheckBox.Margin = Padding.Empty;
            _startupRandomizeButton.Dock = DockStyle.Fill;
            _startupRandomizeButton.Margin = new Padding(0, 4, 0, 0);
            _notificationCenterButton.Dock = DockStyle.Fill;
            _notificationCenterButton.Margin = new Padding(0, 4, 0, 0);
            body.Controls.Add(_allowDhcpIpCheckBox, 0, 0);
            body.Controls.Add(_showPublicIpLocationCheckBox, 0, 1);
            body.Controls.Add(_safetySummaryLabel, 0, 2);
            body.Controls.Add(_darkModeCheckBox, 0, 3);
            body.Controls.Add(_startupRandomizeButton, 0, 4);
            body.Controls.Add(_notificationCenterButton, 0, 5);
        }

        /// <summary>
        /// A Fluent card: the card table paints a rounded fill and border, and its title
        /// and body are solid card colour. The 4px padding keeps the square corners of the
        /// title and body inside the 8px rounded corner, so nothing has to be transparent.
        /// </summary>
        private TableLayoutPanel CreateCard(string title, int bodyColumns, int bodyRows, out TableLayoutPanel body)
        {
            body = CreateBodyLayout(bodyColumns, bodyRows);
            return FluentCard.Create(title, body, delegate { return _palette; });
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
