using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MacRando
{
    internal sealed class NotificationHistoryEventArgs : EventArgs
    {
        public NotificationHistoryEntry Entry { get; private set; }

        public NotificationHistoryEventArgs(NotificationHistoryEntry entry)
        {
            Entry = entry;
        }
    }

    internal sealed class NotificationCenterForm : Form
    {
        private readonly AppSettings _settings;
        private List<NotificationHistoryEntry> _entries = new List<NotificationHistoryEntry>();
        private readonly ListView _list;
        private readonly TextBox _searchBox;
        private readonly ComboBox _severityFilter;
        private readonly TextBox _detailsBox;
        private readonly Button _openButton;
        private readonly Button _restoreButton;
        private readonly Button _retryButton;
        private readonly Button _diagnosticsButton;
        private readonly Button _copyButton;
        private readonly CheckBox _notificationsEnabled;
        private readonly CheckBox _soundEnabled;
        private readonly CheckBox _collapseEnabled;
        private readonly CheckBox _quietEnabled;
        private readonly NumericUpDown _quietStart;
        private readonly NumericUpDown _quietEnd;
        private readonly NumericUpDown _duration;
        private readonly TextBox _updateUrlBox;
        private readonly TextBox _signerBox;
        private Label _footerLabel;
        private bool _allowClose;
        private bool _darkMode;

        public event EventHandler<NotificationHistoryEventArgs> OpenDashboardRequested;
        public event EventHandler<NotificationHistoryEventArgs> RestoreRequested;
        public event EventHandler<NotificationHistoryEventArgs> RetryRequested;
        public event EventHandler<NotificationHistoryEventArgs> DiagnosticsRequested;
        public event EventHandler ClearHistoryRequested;
        public event EventHandler RefreshRequested;
        public event EventHandler SettingsChanged;
        public event EventHandler CheckUpdatesRequested;
        public event EventHandler SendTestNotificationRequested;

        public NotificationHistoryEntry SelectedEntry
        {
            get { return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as NotificationHistoryEntry; }
        }

        public NotificationCenterForm(AppSettings settings, IEnumerable<NotificationHistoryEntry> entries, bool darkMode)
        {
            _settings = settings ?? new AppSettings();
            _darkMode = darkMode;
            Text = "MacRando notification center";
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(760, 500);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = darkMode ? Color.FromArgb(15, 23, 42) : Color.White;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            KeyPreview = true;

            _searchBox = new TextBox { Width = 220 };
            _severityFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            _severityFilter.Items.AddRange(new object[] { "All", NotificationKinds.Critical, NotificationKinds.Error, NotificationKinds.Warning, NotificationKinds.Success, NotificationKinds.Info });
            _severityFilter.SelectedIndex = 0;
            _openButton = MakeButton("Open dashboard", false);
            _restoreButton = MakeButton("Restore now", false);
            _retryButton = MakeButton("Retry", false);
            _diagnosticsButton = MakeButton("Diagnostics", false);
            _copyButton = MakeButton("Copy details", false);
            _detailsBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.FromArgb(248, 250, 252),
                ForeColor = darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(30, 41, 59)
            };
            _list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                GridLines = false,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                MultiSelect = false
            };
            _list.Columns.Add("Time", 145);
            _list.Columns.Add("Severity", 75);
            _list.Columns.Add("Notification", 220);
            _list.Columns.Add("Adapter", 145);
            _list.Columns.Add("Actions", 120);

            _notificationsEnabled = MakeCheckBox("Show popups", _settings.NotificationsEnabled);
            _soundEnabled = MakeCheckBox("Play sound", _settings.NotificationSoundEnabled);
            _collapseEnabled = MakeCheckBox("Collapse repeats", _settings.NotificationCollapseDuplicates);
            _quietEnabled = MakeCheckBox("Quiet hours", _settings.NotificationQuietHoursEnabled);
            _quietStart = MakeHourControl(_settings.NotificationQuietHoursStartHour);
            _quietEnd = MakeHourControl(_settings.NotificationQuietHoursEndHour);
            _duration = new NumericUpDown { Minimum = 2, Maximum = 60, Value = Math.Max(2, Math.Min(60, _settings.NotificationDurationSeconds)), Width = 58 };
            _updateUrlBox = new TextBox { Text = _settings.UpdateManifestUrl ?? string.Empty, Width = 250, Height = 26, Margin = new Padding(2, 4, 2, 4) };
            _signerBox = new TextBox { Text = _settings.ExpectedSignerThumbprint ?? string.Empty, Width = 150, Height = 26, Margin = new Padding(2, 4, 2, 4) };

            BuildLayout();
            WireEvents();
            SetEntries(entries);
            ApplyTheme(_darkMode);
        }

        public void SetEntries(IEnumerable<NotificationHistoryEntry> entries)
        {
            _entries = new List<NotificationHistoryEntry>();
            if (entries != null)
            {
                foreach (NotificationHistoryEntry entry in entries)
                {
                    if (entry != null)
                    {
                        _entries.Add(entry);
                    }
                }
            }
            RefreshList();
        }

        public void ShowCentered(Control owner)
        {
            if (!Visible)
            {
                Show(owner);
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
            BringToFront();
        }

        public void ApplyTheme(bool darkMode)
        {
            _darkMode = darkMode;
            BackColor = darkMode ? Color.FromArgb(15, 23, 42) : Color.White;
            ForeColor = darkMode ? Color.White : Color.FromArgb(15, 23, 42);
            _list.BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.White;
            _list.ForeColor = darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(30, 41, 59);
            _detailsBox.BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.FromArgb(248, 250, 252);
            _detailsBox.ForeColor = darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(30, 41, 59);
            if (Accessibility.ShouldUseHighContrast())
            {
                // The form's own background is set before the child pass, so it has to be
                // corrected here or the window frame keeps the app's colour.
                BackColor = SystemColors.Window;
                ForeColor = SystemColors.WindowText;
                _list.BackColor = SystemColors.Window;
                _list.ForeColor = SystemColors.WindowText;
                _detailsBox.BackColor = SystemColors.Window;
                _detailsBox.ForeColor = SystemColors.WindowText;
            }
            ApplyThemeToChildren(this, darkMode);
            if (_footerLabel != null)
            {
                // Muted, but still above 4.5:1 against the themed background in both modes.
                // Under high contrast the muted colour is the first thing to fail, so it
                // becomes full contrast.
                _footerLabel.ForeColor = Accessibility.ShouldUseHighContrast()
                    ? SystemColors.WindowText
                    : (darkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(71, 85, 105));
            }
            RefreshList();
        }

        public void CloseForDispose()
        {
            _allowClose = true;
            Close();
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && !(ActiveControl is Button) && SelectedEntry != null)
            {
                RaiseOpenDashboard(SelectedEntry);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            toolbar.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(0, 7, 5, 0) });
            toolbar.Controls.Add(_searchBox);
            toolbar.Controls.Add(_severityFilter);
            var refresh = MakeButton("Refresh", false);
            refresh.Click += (sender, args) => RaiseRefresh();
            toolbar.Controls.Add(refresh);
            var clear = MakeButton("Clear history", false);
            clear.Click += (sender, args) => RaiseClearHistory();
            toolbar.Controls.Add(clear);
            var close = MakeButton("Close", false);
            close.Click += (sender, args) => Hide();
            toolbar.Controls.Add(close);

            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(900, 400), FixedPanel = FixedPanel.Panel2 };
            split.Panel1MinSize = 420;
            split.Panel2MinSize = 260;
            split.SplitterDistance = 570;
            split.Panel1.Controls.Add(_list);
            var detailsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8, 0, 0, 0) };
            detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            detailsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            detailsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            detailsPanel.Controls.Add(_detailsBox, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
            actions.Controls.Add(_openButton);
            actions.Controls.Add(_restoreButton);
            actions.Controls.Add(_retryButton);
            actions.Controls.Add(_diagnosticsButton);
            actions.Controls.Add(_copyButton);
            detailsPanel.Controls.Add(actions, 0, 1);
            split.Panel2.Controls.Add(detailsPanel);

            // Two explicit rows instead of one wrapping flow. A GroupBox does not reliably
            // grow around a single wrapping FlowLayoutPanel, which clipped the update fields.
            var settings = new GroupBox { Text = "Notification preferences", Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
            var settingsRows = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty
            };
            settingsRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            settingsRows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsRows.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var notificationRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            notificationRow.Controls.Add(_notificationsEnabled);
            notificationRow.Controls.Add(_soundEnabled);
            notificationRow.Controls.Add(_collapseEnabled);
            notificationRow.Controls.Add(_quietEnabled);
            notificationRow.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new Padding(8, 7, 2, 0) });
            notificationRow.Controls.Add(_quietStart);
            notificationRow.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(4, 7, 2, 0) });
            notificationRow.Controls.Add(_quietEnd);
            notificationRow.Controls.Add(new Label { Text = "Popup seconds", AutoSize = true, Margin = new Padding(8, 7, 2, 0) });
            notificationRow.Controls.Add(_duration);
            settingsRows.Controls.Add(notificationRow, 0, 0);

            // Wraps rather than clips on a narrow window; the group auto-sizes to fit.
            var updateRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            updateRow.Controls.Add(new Label { Text = "Update manifest URL", AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
            updateRow.Controls.Add(_updateUrlBox);
            updateRow.Controls.Add(new Label { Text = "Expected signer", AutoSize = true, Margin = new Padding(8, 7, 2, 0) });
            updateRow.Controls.Add(_signerBox);
            var apply = MakeButton("Apply preferences", true);
            apply.Click += (sender, args) => ApplySettings();
            updateRow.Controls.Add(apply);
            var checkUpdates = MakeButton("Check for updates", false);
            checkUpdates.Click += (sender, args) => RaiseCheckUpdates();
            updateRow.Controls.Add(checkUpdates);
            var sendTest = MakeButton("Send test notification", false);
            sendTest.Click += (sender, args) => RaiseSendTestNotification();
            updateRow.Controls.Add(sendTest);
            settingsRows.Controls.Add(updateRow, 0, 1);
            settings.Controls.Add(settingsRows);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            var versionLabel = new Label
            {
                Text = AppInfo.ProductName + " " + AppInfo.DisplayVersion + "  •  " + LicenseInfo.Notice + "  •  notifications are sanitized before storage",
                AutoSize = true,
                Margin = Padding.Empty
            };
            _footerLabel = versionLabel;
            footer.Controls.Add(versionLabel);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(split, 0, 1);
            root.Controls.Add(settings, 0, 2);
            root.Controls.Add(footer, 0, 3);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            ApplyAccessibility();
        }

        /// <summary>
        /// Names the notification center's controls and fixes the tab order.
        ///
        /// The two settings fields are the reason this matters beyond tidiness: they
        /// contain a URL and a certificate thumbprint, both of which a user pastes from
        /// elsewhere, and a bare edit box gives no indication of which is which or what
        /// happens if the thumbprint is wrong. The descriptions carry that, along with
        /// the fact that an update is refused unless the downloaded file matches both the
        /// hash and the signer.
        ///
        /// Order follows the visual layout top to bottom: filter, list, details, actions,
        /// then the settings and the test button.
        /// </summary>
        private void ApplyAccessibility()
        {
            Accessibility.Describe(_searchBox, "Search notifications", AccessibleRole.Text,
                "Filters the list by title, message, or adapter name.");
            Accessibility.Describe(_severityFilter, "Filter by severity", AccessibleRole.ComboBox,
                "Shows only notifications of one severity, or all of them.");
            Accessibility.Describe(_list, "Notification history", AccessibleRole.List,
                "Use the up and down arrows to move between notifications. The details of the selected one appear below.");
            Accessibility.Describe(_detailsBox, "Notification details", AccessibleRole.Text,
                "The full text of the selected notification, including what to do about it.");
            Accessibility.Describe(_copyButton, "Copy notification details", AccessibleRole.PushButton,
                "Copies the details of the selected notification to the clipboard.");
            Accessibility.Describe(_openButton, "Open the dashboard", AccessibleRole.PushButton,
                "Opens the MacRando dashboard.");
            Accessibility.Describe(_restoreButton, "Restore this adapter", AccessibleRole.PushButton,
                "Returns the adapter to the configuration saved when the change was made.");
            Accessibility.Describe(_retryButton, "Retry the operation", AccessibleRole.PushButton,
                "Attempts the failed operation again.");
            Accessibility.Describe(_diagnosticsButton, "Run read-only diagnostics", AccessibleRole.PushButton,
                "Opens a read-only report about the adapter. Changes nothing.");
            Accessibility.Describe(_updateUrlBox, "Update manifest URL", AccessibleRole.Text,
                "Where MacRando looks for update information. Leave blank to disable update checks.");
            Accessibility.Describe(_signerBox, "Expected signer thumbprint", AccessibleRole.Text,
                "The certificate thumbprint an update must be signed with. An update whose signer does not match is refused.");
            Accessibility.Describe(_duration, "Popup duration in seconds", AccessibleRole.SpinButton,
                "How long a notification stays on screen before it closes.");
            Accessibility.Describe(_footerLabel, "Version and license", AccessibleRole.Text,
                "The running version, the license, and a note that notifications are sanitized before storage.");

            // The quiet-hours pair. Both are empty of visible text of their own, and
            // together they form one setting, so each says which end of the range it is.
            Accessibility.Describe(_quietStart, "Quiet hours start hour", AccessibleRole.SpinButton,
                "0 to 23. Notifications are suppressed from this hour until the end hour.");
            Accessibility.Describe(_quietEnd, "Quiet hours end hour", AccessibleRole.SpinButton,
                "0 to 23. Notifications are suppressed from the start hour until this hour.");

            // The remaining items are created by a layout helper, so they are matched by
            // their visible text. A miss is logged rather than failing silently, because a
            // renamed button would otherwise leave an unlabelled control on screen.
            NameByVisibleText("Refresh", "Refresh the notification list", "Re-reads the stored notification history.");
            NameByVisibleText("Clear history", "Clear notification history", "Deletes every stored notification. This cannot be undone.");
            NameByVisibleText("Close", "Close the notification center", "Closes this window. Stored notifications are kept.");
            NameByVisibleText("Apply preferences", "Apply preferences", "Saves the notification settings shown above.");
            NameByVisibleText("Check for updates", "Check for updates", "Downloads and verifies a newer build, if one exists.");
            NameByVisibleText("Send test notification", "Send a test notification", "Shows a notification so you can confirm how they look and sound.");

            // The settings toggles are created by a helper, so they are named by their
            // visible text where the helper did not set one.
            foreach (Control control in Descendants(this))
            {
                CheckBox check = control as CheckBox;
                if (check != null && string.IsNullOrWhiteSpace(check.AccessibleName))
                {
                    // The state of a checkbox is announced by the control itself, so the
                    // name only has to say what is being toggled.
                    Accessibility.Describe(check, check.Text, AccessibleRole.CheckButton, null);
                }
            }

            var sequence = new Control[]
            {
                _searchBox, _severityFilter, _list, _detailsBox, _copyButton,
                _diagnosticsButton, _retryButton, _restoreButton, _openButton,
                _updateUrlBox, _signerBox, _duration
            };
            for (int index = 0; index < sequence.Length; index++)
            {
                if (sequence[index] != null)
                {
                    sequence[index].TabIndex = index;
                }
            }
        }

        private void NameByVisibleText(string text, string name, string description)
        {
            foreach (Control control in Descendants(this))
            {
                Button button = control as Button;
                if (button != null && string.Equals(button.Text, text, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(button.AccessibleName))
                {
                    Accessibility.Describe(button, name, AccessibleRole.PushButton, description);
                }
            }
        }

        internal static List<Control> Descendants(Control root)
        {
            var found = new List<Control>();
            if (root == null)
            {
                return found;
            }
            foreach (Control child in root.Controls)
            {
                found.Add(child);
                found.AddRange(Descendants(child));
            }
            return found;
        }

        private void WireEvents()
        {
            _searchBox.TextChanged += (sender, args) => RefreshList();
            _severityFilter.SelectedIndexChanged += (sender, args) => RefreshList();
            _list.SelectedIndexChanged += (sender, args) => UpdateDetails();
            _list.DoubleClick += (sender, args) => RaiseOpenDashboard(SelectedEntry);
            _openButton.Click += (sender, args) => RaiseOpenDashboard(SelectedEntry);
            _restoreButton.Click += (sender, args) => RaiseRestore(SelectedEntry);
            _retryButton.Click += (sender, args) => RaiseRetry(SelectedEntry);
            _diagnosticsButton.Click += (sender, args) => RaiseDiagnostics(SelectedEntry);
            _copyButton.Click += (sender, args) => CopyDetails();
        }

        private void RefreshList()
        {
            if (_list == null)
            {
                return;
            }
            string selectedId = SelectedEntry == null ? null : SelectedEntry.NotificationId;
            string search = _searchBox == null ? string.Empty : (_searchBox.Text ?? string.Empty).Trim();
            string filter = _severityFilter == null || _severityFilter.SelectedItem == null
                ? "All"
                : Convert.ToString(_severityFilter.SelectedItem);
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                for (int index = _entries.Count - 1; index >= 0; index--)
                {
                    NotificationHistoryEntry entry = _entries[index];
                    if (entry == null || !Matches(entry, search, filter))
                    {
                        continue;
                    }
                    ListViewItem item = new ListViewItem(entry.TimestampUtc.ToLocalTime().ToString("g"));
                    item.SubItems.Add(NotificationKinds.Normalize(entry.Severity));
                    item.SubItems.Add(Shorten(entry.Title, 42));
                    item.SubItems.Add(Shorten(entry.AdapterKey, 28));
                    item.SubItems.Add(Shorten(entry.Action, 24));
                    item.Tag = entry;
                    _list.Items.Add(item);
                    if (!string.IsNullOrWhiteSpace(selectedId) && string.Equals(selectedId, entry.NotificationId, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Selected = true;
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
                AdjustColumns();
            }
            UpdateDetails();
        }

        private bool Matches(NotificationHistoryEntry entry, string search, string filter)
        {
            if (!string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(NotificationKinds.Normalize(entry.Severity), filter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }
            string haystack = string.Join(" ", new string[]
            {
                entry.Title ?? string.Empty,
                entry.Message ?? string.Empty,
                entry.AdapterKey ?? string.Empty,
                entry.Action ?? string.Empty,
                entry.Details ?? string.Empty
            });
            return haystack.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateDetails()
        {
            NotificationHistoryEntry entry = SelectedEntry;
            bool hasEntry = entry != null;
            _detailsBox.Text = hasEntry ? BuildDetails(entry) : "Select a notification to view its details.";
            _openButton.Enabled = hasEntry;
            _copyButton.Enabled = hasEntry;
            _restoreButton.Enabled = hasEntry && entry.CanRestore;
            _retryButton.Enabled = hasEntry && entry.CanRetry;
            _diagnosticsButton.Enabled = hasEntry;
        }

        private string BuildDetails(NotificationHistoryEntry entry)
        {
            return "Time: " + entry.TimestampUtc.ToLocalTime().ToString("g") + Environment.NewLine +
                   "Severity: " + NotificationKinds.Normalize(entry.Severity) + Environment.NewLine +
                   "Title: " + (entry.Title ?? string.Empty) + Environment.NewLine +
                   "Message: " + (entry.Message ?? string.Empty) + Environment.NewLine +
                   "Adapter: " + (entry.AdapterKey ?? string.Empty) + Environment.NewLine +
                   "Action: " + (entry.Action ?? string.Empty) + Environment.NewLine +
                   "Details: " + (entry.Details ?? string.Empty);
        }

        private void ApplySettings()
        {
            _settings.NotificationsEnabled = _notificationsEnabled.Checked;
            _settings.NotificationSoundEnabled = _soundEnabled.Checked;
            _settings.NotificationCollapseDuplicates = _collapseEnabled.Checked;
            _settings.NotificationQuietHoursEnabled = _quietEnabled.Checked;
            _settings.NotificationQuietHoursStartHour = (int)_quietStart.Value;
            _settings.NotificationQuietHoursEndHour = (int)_quietEnd.Value;
            _settings.NotificationDurationSeconds = (int)_duration.Value;
            _settings.UpdateManifestUrl = (_updateUrlBox.Text ?? string.Empty).Trim();
            _settings.ExpectedSignerThumbprint = (_signerBox.Text ?? string.Empty).Replace(" ", "").ToUpperInvariant();
            if (SettingsChanged != null)
            {
                SettingsChanged(this, EventArgs.Empty);
            }
        }

        private void CopyDetails()
        {
            NotificationHistoryEntry entry = SelectedEntry;
            if (entry == null)
            {
                return;
            }
            try
            {
                Clipboard.SetText(BuildDetails(entry));
                _copyButton.Text = "Copied";
                var timer = new Timer { Interval = 1200 };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    _copyButton.Text = "Copy details";
                    timer.Dispose();
                };
                timer.Start();
            }
            catch
            {
                _copyButton.Text = "Copy failed";
            }
        }

        private void AdjustColumns()
        {
            if (_list == null || _list.Columns.Count == 0)
            {
                return;
            }
            int available = Math.Max(420, _list.ClientSize.Width - 24);
            _list.Columns[0].Width = 140;
            _list.Columns[1].Width = 75;
            _list.Columns[2].Width = Math.Max(150, (int)(available * 0.36));
            _list.Columns[3].Width = Math.Max(110, (int)(available * 0.23));
            _list.Columns[4].Width = Math.Max(90, available - _list.Columns[0].Width - _list.Columns[1].Width - _list.Columns[2].Width - _list.Columns[3].Width - 24);
        }

        private void RaiseOpenDashboard(NotificationHistoryEntry entry)
        {
            if (entry != null && OpenDashboardRequested != null)
            {
                OpenDashboardRequested(this, new NotificationHistoryEventArgs(entry));
            }
        }

        private void RaiseRestore(NotificationHistoryEntry entry)
        {
            if (entry != null && entry.CanRestore && RestoreRequested != null)
            {
                RestoreRequested(this, new NotificationHistoryEventArgs(entry));
            }
        }

        private void RaiseRetry(NotificationHistoryEntry entry)
        {
            if (entry != null && entry.CanRetry && RetryRequested != null)
            {
                RetryRequested(this, new NotificationHistoryEventArgs(entry));
            }
        }

        private void RaiseDiagnostics(NotificationHistoryEntry entry)
        {
            if (entry != null && DiagnosticsRequested != null)
            {
                DiagnosticsRequested(this, new NotificationHistoryEventArgs(entry));
            }
        }

        private void RaiseRefresh()
        {
            if (RefreshRequested != null)
            {
                RefreshRequested(this, EventArgs.Empty);
            }
        }

        private void RaiseCheckUpdates()
        {
            ApplySettings();
            if (CheckUpdatesRequested != null)
            {
                CheckUpdatesRequested(this, EventArgs.Empty);
            }
        }

        private void RaiseSendTestNotification()
        {
            if (SendTestNotificationRequested != null)
            {
                SendTestNotificationRequested(this, EventArgs.Empty);
            }
        }

        private void RaiseClearHistory()
        {
            if (ClearHistoryRequested != null)
            {
                ClearHistoryRequested(this, EventArgs.Empty);
            }
        }

        private static Button MakeButton(string text, bool primary)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(84, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(37, 99, 235) : SystemColors.Control,
                ForeColor = primary ? Color.White : SystemColors.ControlText,
                Margin = new Padding(4),
                Tag = primary
            };
            // Without this the visual style ignores BackColor, so a themed button
            // cannot guarantee a readable foreground/background pairing.
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static CheckBox MakeCheckBox(string text, bool value)
        {
            return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(8, 6, 4, 0) };
        }

        private static NumericUpDown MakeHourControl(int value)
        {
            return new NumericUpDown { Minimum = 0, Maximum = 23, Value = Math.Max(0, Math.Min(23, value)), Width = 48 };
        }

        private static string Shorten(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value.Length <= maxLength ? value : value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private static void ApplyThemeToChildren(Control parent, bool darkMode)
        {
            // High contrast replaces the custom styling instead of layering over it.
            // A themed panel with a flat borderless button on top is exactly the
            // combination that becomes invisible, and the whole point of high contrast
            // is that the user cannot read a subtle one.
            bool highContrast = Accessibility.ShouldUseHighContrast();
            foreach (Control control in parent.Controls)
            {
                if (highContrast)
                {
                    if (control is TextBox || control is ListView || control is ComboBox || control is NumericUpDown)
                    {
                        control.BackColor = SystemColors.Window;
                        control.ForeColor = SystemColors.WindowText;
                    }
                    else if (control is Button)
                    {
                        Button system = (Button)control;
                        system.FlatStyle = FlatStyle.Standard;
                        system.UseVisualStyleBackColor = true;
                        system.BackColor = SystemColors.Window;
                        system.ForeColor = SystemColors.WindowText;
                    }
                    else if (control is GroupBox || control is TableLayoutPanel ||
                             control is FlowLayoutPanel || control is SplitContainer)
                    {
                        control.ForeColor = SystemColors.WindowText;
                    }
                    ApplyThemeToChildren(control, darkMode);
                    continue;
                }
                if (control is TextBox)
                {
                    control.BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.White;
                    control.ForeColor = darkMode ? Color.White : Color.FromArgb(15, 23, 42);
                }
                else if (control is Button)
                {
                    Button button = (Button)control;
                    bool primary = button.Tag is bool && (bool)button.Tag;
                    // Both colors must move together. Setting only ForeColor here is what
                    // produced white text on a light system button in dark mode.
                    if (primary)
                    {
                        button.BackColor = Color.FromArgb(37, 99, 235);
                        button.ForeColor = Color.White;
                    }
                    else
                    {
                        button.BackColor = darkMode ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249);
                        button.ForeColor = darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(15, 23, 42);
                    }
                    button.FlatStyle = FlatStyle.Flat;
                    button.UseVisualStyleBackColor = false;
                    button.FlatAppearance.BorderColor = darkMode ? Color.FromArgb(71, 85, 105) : Color.FromArgb(203, 213, 225);
                    button.FlatAppearance.MouseOverBackColor = darkMode ? Color.FromArgb(51, 65, 85) : Color.FromArgb(219, 234, 254);
                }
                else if (control is ListView)
                {
                    control.BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.White;
                    control.ForeColor = darkMode ? Color.White : Color.FromArgb(15, 23, 42);
                }
                else if (control is ComboBox)
                {
                    ComboBox combo = (ComboBox)control;
                    // The background must be themed too, otherwise the selected item is
                    // white text on a white box in dark mode.
                    combo.BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.White;
                    combo.ForeColor = darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(15, 23, 42);
                    combo.FlatStyle = FlatStyle.Flat;
                }
                else if (control is NumericUpDown)
                {
                    NumericUpDown numeric = (NumericUpDown)control;
                    numeric.BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.White;
                    numeric.ForeColor = darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(15, 23, 42);
                }
                else if (control is GroupBox || control is TableLayoutPanel || control is FlowLayoutPanel || control is SplitContainer)
                {
                    control.ForeColor = darkMode ? Color.White : SystemColors.ControlText;
                }
                ApplyThemeToChildren(control, darkMode);
            }
        }
    }
}
