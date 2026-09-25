using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MacRando
{
    internal enum NotificationPopupAction
    {
        OpenDashboard,
        Restore,
        Retry,
        Diagnostics,
        Dismiss
    }

    internal sealed class NotificationPopupActionEventArgs : EventArgs
    {
        public NotificationPopupAction Action { get; private set; }

        public NotificationPopupActionEventArgs(NotificationPopupAction action)
        {
            Action = action;
        }
    }

    internal sealed class NotificationPopup : Form
    {
        private const int DefaultTimeoutMilliseconds = 5000;
        private const int PopupWidth = 430;
        private const int PopupHeight = 116;
        private const int PopupHeightWithActions = 158;
        private const int PopupMargin = 12;
        private const int PopupGap = 8;

        private Color _accentColor;
        private readonly Image _iconImage;
        private readonly Timer _closeTimer;
        private readonly Label _titleLabel;
        private readonly Label _messageLabel;
        private readonly TableLayoutPanel _layout;
        private readonly FlowLayoutPanel _actionsPanel;
        private readonly Button _openButton;
        private readonly Button _restoreButton;
        private readonly Button _retryButton;
        private readonly Button _diagnosticsButton;
        private readonly Button _dismissButton;
        private bool _persistent;
        private bool _dismissed;
        private bool _disposed;
        private int _timeoutMilliseconds;

        public string NotificationId { get; private set; }
        public string AdapterKey { get; private set; }
        public string TitleText { get; private set; }
        public string MessageText { get; private set; }
        public int GroupCount { get; private set; }
        public bool IsPersistent { get { return _persistent; } }
        public Func<Task> RestoreAction { get; set; }
        public Func<Task> RetryAction { get; set; }

        public event EventHandler PopupClicked;
        public event EventHandler<NotificationPopupActionEventArgs> ActionRequested;
        public event EventHandler PopupClosed;

        public NotificationPopup(Icon icon, string title, string message, bool darkMode, ToolTipIcon notificationType)
            : this(icon, title, message, darkMode, ToSeverity(notificationType), DefaultTimeoutMilliseconds, false)
        {
        }

        public NotificationPopup(Icon icon, string title, string message, bool darkMode, ToolTipIcon notificationType, int timeoutMilliseconds)
            : this(icon, title, message, darkMode, ToSeverity(notificationType), timeoutMilliseconds, false)
        {
        }

        public NotificationPopup(Icon icon, string title, string message, bool darkMode, string severity, int timeoutMilliseconds, bool persistent)
        {
            Text = "MacRando notification";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(PopupWidth, PopupHeight);
            MinimumSize = new Size(360, 100);
            Padding = new Padding(1);
            KeyPreview = true;
            BackColor = darkMode ? Color.FromArgb(31, 41, 55) : Color.White;
            ForeColor = darkMode ? Color.White : Color.FromArgb(15, 23, 42);
            _timeoutMilliseconds = Math.Max(1000, timeoutMilliseconds);
            _persistent = persistent;
            GroupCount = 1;
            TitleText = title ?? string.Empty;
            MessageText = message ?? string.Empty;
            _accentColor = GetAccentColor(severity, darkMode);
            _iconImage = CreateIconImage(icon);

            _titleLabel = new Label();
            _messageLabel = new Label();
            _actionsPanel = new FlowLayoutPanel();
            _openButton = MakeActionButton("Open dashboard");
            _restoreButton = MakeActionButton("Restore now");
            _retryButton = MakeActionButton("Retry");
            _diagnosticsButton = MakeActionButton("Diagnostics");
            _dismissButton = MakeActionButton("Dismiss");
            _layout = BuildContent(title, message, darkMode, severity);
            Controls.Add(_layout);

            _openButton.Click += (sender, args) => TriggerAction(NotificationPopupAction.OpenDashboard);
            _restoreButton.Click += (sender, args) => TriggerAction(NotificationPopupAction.Restore);
            _retryButton.Click += (sender, args) => TriggerAction(NotificationPopupAction.Retry);
            _diagnosticsButton.Click += (sender, args) => TriggerAction(NotificationPopupAction.Diagnostics);
            _dismissButton.Click += (sender, args) => TriggerAction(NotificationPopupAction.Dismiss);

            _closeTimer = new Timer();
            _closeTimer.Interval = _timeoutMilliseconds;
            _closeTimer.Tick += (sender, args) => Dismiss(false);
            SetActions(false, false, null, null, persistent, timeoutMilliseconds);
        }

        public void Configure(
            string notificationId,
            string adapterKey,
            bool canRestore,
            bool canRetry,
            Func<Task> restoreAction,
            Func<Task> retryAction,
            bool persistent,
            int timeoutMilliseconds)
        {
            NotificationId = notificationId ?? string.Empty;
            AdapterKey = adapterKey ?? string.Empty;
            RestoreAction = restoreAction;
            RetryAction = retryAction;
            SetActions(canRestore, canRetry, restoreAction, retryAction, persistent, timeoutMilliseconds);
        }

        public void UpdateContent(string title, string message, string severity, bool darkMode)
        {
            TitleText = title ?? string.Empty;
            MessageText = message ?? string.Empty;
            if (_titleLabel != null)
            {
                string displayTitle = GroupCount > 1
                    ? TitleText + " (" + GroupCount + ")"
                    : TitleText;
                _titleLabel.Text = Truncate(displayTitle, 80);
            }
            if (_messageLabel != null)
            {
                _messageLabel.Text = Truncate(message, 320);
            }
            _accentColor = GetAccentColor(severity, darkMode);
            Invalidate();
        }

        public void AppendGroupMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }
            GroupCount++;
            string combined = string.IsNullOrWhiteSpace(MessageText)
                ? Truncate(message, 320)
                : Truncate(MessageText + "  •  " + message, 320);
            MessageText = combined;
            if (_messageLabel != null)
            {
                _messageLabel.Text = combined;
            }
        }

        public void SetPersistent(bool persistent, int timeoutMilliseconds)
        {
            _persistent = persistent;
            _timeoutMilliseconds = Math.Max(1000, timeoutMilliseconds);
            _closeTimer.Stop();
            if (!persistent && IsHandleCreated && Visible)
            {
                _closeTimer.Interval = _timeoutMilliseconds;
                _closeTimer.Start();
            }
        }

        public void PositionAt(Rectangle workingArea, int stackIndex)
        {
            int x = workingArea.Right - Width - PopupMargin;
            int y = workingArea.Bottom - Height - PopupMargin - (Math.Max(0, stackIndex) * (Height + PopupGap));
            x = Math.Max(workingArea.Left + PopupMargin, x);
            y = Math.Max(workingArea.Top + PopupMargin, y);
            Location = new Point(x, y);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!_persistent)
            {
                _closeTimer.Interval = _timeoutMilliseconds;
                _closeTimer.Start();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _closeTimer.Stop();
            base.OnFormClosed(e);
            EventHandler handler = PopupClosed;
            PopupClosed = null;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                TriggerAction(NotificationPopupAction.Dismiss);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && !(ActiveControl is Button))
            {
                TriggerAction(NotificationPopupAction.OpenDashboard);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Dismiss(true);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Color borderColor = BackColor == Color.White
                ? Color.FromArgb(203, 213, 225)
                : Color.FromArgb(71, 85, 105);
            using (var pen = new Pen(borderColor))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            using (var brush = new SolidBrush(_accentColor))
            {
                e.Graphics.FillRectangle(brush, 1, 1, 4, Height - 2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _closeTimer.Stop();
                _closeTimer.Dispose();
                if (_iconImage != null)
                {
                    _iconImage.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private TableLayoutPanel BuildContent(string title, string message, bool darkMode, string severity)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(13, 10, 9, 6),
                BackColor = BackColor,
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = BackColor,
                Margin = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var iconBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _iconImage,
                BackColor = BackColor,
                Margin = new Padding(0, 1, 10, 0),
                TabStop = false
            };

            var textPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BackColor,
                Margin = Padding.Empty
            };
            textPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            textPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            textPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _titleLabel.Dock = DockStyle.Fill;
            _titleLabel.Text = Truncate(title, 80);
            _titleLabel.AutoEllipsis = true;
            _titleLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
            _titleLabel.ForeColor = darkMode ? Color.White : Color.FromArgb(15, 23, 42);
            _titleLabel.BackColor = BackColor;
            _titleLabel.Margin = Padding.Empty;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;

            _messageLabel.Dock = DockStyle.Fill;
            _messageLabel.Text = Truncate(message, 320);
            _messageLabel.AutoEllipsis = true;
            _messageLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _messageLabel.ForeColor = darkMode ? Color.FromArgb(203, 213, 225) : Color.FromArgb(71, 85, 105);
            _messageLabel.BackColor = BackColor;
            _messageLabel.Margin = new Padding(0, 2, 8, 0);
            textPanel.Controls.Add(_titleLabel, 0, 0);
            textPanel.Controls.Add(_messageLabel, 0, 1);

            var closeButton = new Button
            {
                Text = "×",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = darkMode ? Color.FromArgb(203, 213, 225) : Color.FromArgb(71, 85, 105),
                BackColor = BackColor,
                Margin = new Padding(3, 0, 0, 0),
                TabStop = true
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = darkMode ? Color.FromArgb(55, 65, 81) : Color.FromArgb(241, 245, 249);
            closeButton.Click += (sender, args) => TriggerAction(NotificationPopupAction.Dismiss);

            content.Controls.Add(iconBox, 0, 0);
            content.Controls.Add(textPanel, 1, 0);
            content.Controls.Add(closeButton, 2, 0);
            layout.Controls.Add(content, 0, 0);

            _actionsPanel.Dock = DockStyle.Fill;
            _actionsPanel.FlowDirection = FlowDirection.RightToLeft;
            _actionsPanel.WrapContents = true;
            _actionsPanel.BackColor = BackColor;
            _actionsPanel.Margin = Padding.Empty;
            _actionsPanel.Padding = new Padding(0, 4, 0, 0);
            _actionsPanel.Controls.Add(_dismissButton);
            _actionsPanel.Controls.Add(_diagnosticsButton);
            _actionsPanel.Controls.Add(_retryButton);
            _actionsPanel.Controls.Add(_restoreButton);
            _actionsPanel.Controls.Add(_openButton);
            layout.Controls.Add(_actionsPanel, 0, 1);

            iconBox.Click += BodyClick;
            _titleLabel.Click += BodyClick;
            _messageLabel.Click += BodyClick;
            textPanel.Click += BodyClick;
            content.Click += BodyClick;
            return layout;
        }

        private Button MakeActionButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(64, 28),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                BackColor = BackColor,
                ForeColor = ForeColor,
                Margin = new Padding(4, 0, 0, 0),
                TabStop = true
            };
        }

        private void SetActions(bool canRestore, bool canRetry, Func<Task> restoreAction, Func<Task> retryAction, bool persistent, int timeoutMilliseconds)
        {
            RestoreAction = restoreAction;
            RetryAction = retryAction;
            _persistent = persistent;
            _timeoutMilliseconds = Math.Max(1000, timeoutMilliseconds);
            bool showRestore = canRestore && restoreAction != null;
            bool showRetry = canRetry && retryAction != null;
            _restoreButton.Visible = showRestore;
            _retryButton.Visible = showRetry;
            _diagnosticsButton.Visible = showRestore || showRetry;
            _openButton.Visible = true;
            _dismissButton.Visible = true;
            _layout.RowStyles[1].SizeType = SizeType.Absolute;
            bool hasDiagnosticActions = showRestore || showRetry;
            _layout.RowStyles[1].Height = hasDiagnosticActions ? 68F : 38F;
            ClientSize = new Size(PopupWidth, hasDiagnosticActions ? PopupHeightWithActions + 30 : PopupHeightWithActions);
            PerformLayout();
            _closeTimer.Stop();
            if (!persistent && IsHandleCreated && Visible)
            {
                _closeTimer.Interval = _timeoutMilliseconds;
                _closeTimer.Start();
            }
        }

        private void BodyClick(object sender, EventArgs args)
        {
            Dismiss(true);
        }

        private void Dismiss(bool openDashboard)
        {
            if (_dismissed)
            {
                return;
            }
            _dismissed = true;
            if (openDashboard && PopupClicked != null)
            {
                PopupClicked(this, EventArgs.Empty);
            }
            Close();
        }

        private void TriggerAction(NotificationPopupAction action)
        {
            if (_dismissed)
            {
                return;
            }
            _dismissed = true;
            EventHandler<NotificationPopupActionEventArgs> handler = ActionRequested;
            Close();
            if (handler != null)
            {
                handler(this, new NotificationPopupActionEventArgs(action));
            }
        }

        private static Image CreateIconImage(Icon icon)
        {
            Icon source = icon == null
                ? (Icon)SystemIcons.Application.Clone()
                : (Icon)icon.Clone();
            using (source)
            {
                using (Bitmap bitmap = source.ToBitmap())
                {
                    return new Bitmap(bitmap);
                }
            }
        }

        private static Color GetAccentColor(string severity, bool darkMode)
        {
            string normalized = NotificationKinds.Normalize(severity);
            if (normalized == NotificationKinds.Critical || normalized == NotificationKinds.Error)
            {
                return darkMode ? Color.FromArgb(248, 113, 113) : Color.FromArgb(220, 38, 38);
            }
            if (normalized == NotificationKinds.Warning)
            {
                return darkMode ? Color.FromArgb(251, 191, 36) : Color.FromArgb(217, 119, 6);
            }
            if (normalized == NotificationKinds.Success)
            {
                return darkMode ? Color.FromArgb(74, 222, 128) : Color.FromArgb(22, 163, 74);
            }
            return darkMode ? Color.FromArgb(96, 165, 250) : Color.FromArgb(37, 99, 235);
        }

        private static string ToSeverity(ToolTipIcon notificationType)
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

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            value = value.Replace("\r", " ").Replace("\n", " ");
            return value.Length <= maxLength ? value : value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }
    }
}
