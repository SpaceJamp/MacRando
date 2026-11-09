using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MacRando
{
    /// <summary>
    /// The Windows 11 Fluent colour tokens shared by the dashboard, popups and tray menu.
    /// High contrast overrides dark and light and stays system-derived.
    /// </summary>
    internal sealed class FluentPalette
    {
        public Color Background { get; private set; }
        public Color Layer { get; private set; }
        public Color Card { get; private set; }
        public Color CardBorder { get; private set; }
        public Color ControlBorder { get; private set; }
        public Color Text { get; private set; }
        public Color TextSecondary { get; private set; }
        public Color Accent { get; private set; }
        public Color TextOnAccent { get; private set; }
        public Color Selected { get; private set; }
        public Color InfoBackground { get; private set; }
        public Color Success { get; private set; }
        public Color Warning { get; private set; }
        public Color Error { get; private set; }
        public bool IsHighContrast { get; private set; }

        public static FluentPalette Build(bool highContrast, bool dark)
        {
            if (highContrast)
            {
                HighContrastPalette h = Accessibility.BuildPalette(true, dark);
                return new FluentPalette
                {
                    IsHighContrast = true,
                    Background = h.Surface,
                    Layer = h.Surface,
                    Card = h.Surface,
                    CardBorder = h.Border,
                    ControlBorder = h.Border,
                    Text = h.Text,
                    TextSecondary = h.Text,
                    Accent = h.Accent,
                    TextOnAccent = h.TextOnAccent,
                    Selected = h.SurfaceAlt,
                    InfoBackground = h.Surface,
                    Success = h.Text,
                    Warning = h.Text,
                    Error = h.Text
                };
            }

            if (dark)
            {
                return new FluentPalette
                {
                    Background = Rgb(0x20, 0x20, 0x20),
                    Layer = Rgb(0x27, 0x27, 0x27),
                    Card = Rgb(0x2D, 0x2D, 0x2D),
                    CardBorder = Rgb(0x3A, 0x3A, 0x3A),
                    ControlBorder = Rgb(0x4D, 0x4D, 0x4D),
                    Text = Rgb(0xFF, 0xFF, 0xFF),
                    TextSecondary = Rgb(0xC8, 0xC8, 0xC8),
                    Accent = Rgb(0x60, 0xCD, 0xFF),
                    TextOnAccent = Rgb(0, 0, 0),
                    Selected = Rgb(0x38, 0x38, 0x38),
                    InfoBackground = Rgb(0x43, 0x35, 0x19),
                    Success = Rgb(0x6C, 0xCB, 0x5F),
                    Warning = Rgb(0xFC, 0xE1, 0x00),
                    Error = Rgb(0xFF, 0x99, 0xA4)
                };
            }

            return new FluentPalette
            {
                Background = Rgb(0xF3, 0xF3, 0xF3),
                Layer = Rgb(0xF9, 0xF9, 0xF9),
                Card = Color.White,
                CardBorder = Rgb(0xE5, 0xE5, 0xE5),
                ControlBorder = Rgb(0xD1, 0xD1, 0xD1),
                Text = Rgb(0x1B, 0x1B, 0x1B),
                TextSecondary = Rgb(0x5C, 0x5C, 0x5C),
                Accent = Rgb(0x00, 0x5F, 0xB8),
                TextOnAccent = Color.White,
                Selected = Rgb(0xE6, 0xE6, 0xE6),
                InfoBackground = Rgb(0xFF, 0xF4, 0xCE),
                Success = Rgb(0x0F, 0x7B, 0x0F),
                Warning = Rgb(0x9D, 0x5D, 0x00),
                Error = Rgb(0xC4, 0x2B, 0x1C)
            };
        }

        /// <summary>Windows 11's UI font. MacRando supports Windows 11 only, which always has it.</summary>
        public static string BodyFontFamily()
        {
            return "Segoe UI Variable Text";
        }

        private static Color Rgb(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }
    }

    /// <summary>Scale from the form's AutoScale dimensions (DeviceDpi reports 96 in this app).</summary>
    internal static class FluentScale
    {
        public static float For(Control control)
        {
            Form form = control == null ? null : control.FindForm();
            float h = form == null ? 0F : form.CurrentAutoScaleDimensions.Height;
            return h > 0F ? h / 96F : 1F;
        }
    }

    /// <summary>Tab strip: one button per page, only the selected page visible.</summary>
    internal sealed class PivotBar : FlowLayoutPanel
    {
        private readonly List<Button> _tabs = new List<Button>();
        private readonly List<Control> _pages = new List<Control>();
        private FluentPalette _palette = FluentPalette.Build(false, false);
        private int _selected = -1;

        public event EventHandler SelectedIndexChanged;

        public PivotBar()
        {
            AutoSize = true;
            // Wraps to a second row when the window is too narrow for every tab, rather
            // than cutting the last tabs off.
            WrapContents = true;
            AccessibleRole = AccessibleRole.PageTabList;
        }

        public int TabCount { get { return _tabs.Count; } }

        public Button TabButton(int index)
        {
            return _tabs[index];
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _tabs.Count || value == _selected)
                {
                    return;
                }
                _selected = value;
                for (int i = 0; i < _pages.Count; i++)
                {
                    _pages[i].Visible = i == value;
                    _tabs[i].AccessibleDescription = i == value ? "Selected" : "";
                    _tabs[i].TabStop = i == value;
                }
                // Selected page right after the bar: Tab goes button -> first control of its page.
                _pages[value].TabIndex = TabIndex + 1;
                ApplyPalette(_palette);
                EventHandler handler = SelectedIndexChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        public int AddTab(string label, Control page)
        {
            Button b = new Button();
            b.Text = label;
            b.AutoSize = true;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.AccessibleRole = AccessibleRole.PageTab;
            b.AccessibleName = label;
            b.AccessibleDescription = "";
            b.TabStop = false;
            b.Margin = new Padding(0, 0, 4, 0);
            b.Padding = new Padding(8, 4, 8, 8);
            b.Paint += PaintAccentBar;
            b.Click += delegate { SelectedIndex = _tabs.IndexOf(b); };
            _tabs.Add(b);
            _pages.Add(page);
            Controls.Add(b);
            page.Visible = false;
            int index = _tabs.Count - 1;
            if (index == 0)
            {
                SelectedIndex = 0;
            }
            ApplyPalette(_palette);
            return index;
        }

        public void ApplyPalette(FluentPalette p)
        {
            _palette = p;
            // The strip sits on the content layer in both windows that use it.
            BackColor = p.Layer;
            for (int i = 0; i < _tabs.Count; i++)
            {
                Button b = _tabs[i];
                bool on = i == _selected;
                b.BackColor = p.Layer;
                b.ForeColor = on ? p.Text : p.TextSecondary;
                b.FlatAppearance.MouseOverBackColor = p.Selected;
                b.FlatAppearance.MouseDownBackColor = p.Selected;
                if (b.Font.Bold != on || b.Font.Name != Font.Name)
                {
                    b.Font = new Font(Font.FontFamily, Font.Size, on ? FontStyle.Bold : FontStyle.Regular);
                }
                b.Invalidate();
            }
        }

        // Tabs are added before the strip has a parent, so its font is only final here.
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            ApplyPalette(_palette);
        }

        private void PaintAccentBar(object sender, PaintEventArgs e)
        {
            Button b = (Button)sender;
            if (_tabs.IndexOf(b) != _selected)
            {
                return;
            }
            int h = Math.Max(1, (int)Math.Round(3 * FluentScale.For(this)));
            int inset = b.Padding.Left;
            using (SolidBrush brush = new SolidBrush(_palette.Accent))
            {
                e.Graphics.FillRectangle(brush, inset, b.Height - h, Math.Max(0, b.Width - 2 * inset), h);
            }
        }

        // Left/Right move between tabs while a tab has focus; no wrap at either end.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData == Keys.Left || keyData == Keys.Right) && _selected >= 0 && _tabs[_selected].Focused)
            {
                int next = _selected + (keyData == Keys.Right ? 1 : -1);
                if (next >= 0 && next < _tabs.Count)
                {
                    SelectedIndex = next;
                    _tabs[next].Focus();
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal static class FluentCard
    {
        // Marks the parts of a card so a window's theme pass can colour them: the card
        // shows the layer at its rounded corners, its title and body are card colour.
        public static readonly object CardTag = new object();
        public static readonly object BodyTag = new object();
        public static readonly object TitleTag = new object();

        /// <summary>
        /// A card: a title row over the given body. The card paints a rounded fill and
        /// border; its 4px padding keeps the square corners of the title and body inside
        /// the 8px rounded corner, so nothing on it has to be transparent.
        /// </summary>
        public static TableLayoutPanel Create(string title, Control body, Func<FluentPalette> palette)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(4),
                Margin = new Padding(0, 0, 0, 12),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BorderStyle = BorderStyle.None,
                Tag = CardTag,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Attach(card, palette);
            body.Tag = BodyTag;
            body.Dock = DockStyle.Top;
            body.Margin = Padding.Empty;
            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 8, 16, 0),
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
                Tag = TitleTag
            };
            card.Controls.Add(titleLabel, 0, 0);
            card.Controls.Add(body, 0, 1);
            return card;
        }

        public static void Attach(TableLayoutPanel card, Func<FluentPalette> palette)
        {
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                Rectangle r = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                if (r.Width <= 0 || r.Height <= 0)
                {
                    return;
                }
                FluentPalette p = palette();
                int d = Math.Max(1, (int)Math.Round(8 * FluentScale.For(card))) * 2;
                d = Math.Min(d, Math.Min(r.Width, r.Height));
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(r.Left, r.Top, d, d, 180, 90);
                    path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush fill = new SolidBrush(p.Card))
                    using (Pen border = new Pen(p.CardBorder))
                    {
                        e.Graphics.FillPath(fill, path);
                        e.Graphics.DrawPath(border, path);
                    }
                }
            };
        }
    }

    internal static class FluentButton
    {
        /// <summary>
        /// Fluent button colours; a button whose Tag is true is primary. A disabled primary
        /// button drops the accent fill, because Windows draws disabled text grey and grey
        /// on the accent colour was unreadable. Callers skip this under high contrast.
        /// </summary>
        public static void Style(Button button, FluentPalette p)
        {
            bool primary = button.Tag is bool && (bool)button.Tag && button.Enabled;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? p.Accent : p.ControlBorder;
            button.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Dark(p.Accent, 0.05f) : p.Selected;
            button.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(p.Accent, 0.12f) : p.CardBorder;
            button.UseVisualStyleBackColor = false;
            button.BackColor = primary ? p.Accent : p.Card;
            button.ForeColor = primary ? p.TextOnAccent : p.Text;
        }
    }

    internal static class FluentChrome
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Form form, bool dark)
        {
            if (form.IsHandleCreated)
            {
                Apply(form.Handle, dark);
                return;
            }
            EventHandler once = null;
            once = delegate
            {
                form.HandleCreated -= once;
                Apply(form.Handle, dark);
            };
            form.HandleCreated += once;
        }

        public static void Apply(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            Set(hwnd, 20, dark ? 1 : 0);
            Set(hwnd, 33, 2);
        }

        private static void Set(IntPtr hwnd, int attribute, int value)
        {
            try
            {
                DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows lacks the attribute or dwmapi; chrome is cosmetic.
            }
        }
    }
}
