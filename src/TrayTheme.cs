using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacRando
{
    /// <summary>
    /// Colours the tray context menu, which is otherwise drawn by the system and stays
    /// light no matter what the dashboard is doing.
    ///
    /// A ToolStripMenuItem is a ToolStripItem, not a Control, so none of the dashboard's
    /// theming reaches it: the menu had no dark mode at all until this. The whole menu is
    /// drawn by a custom renderer rather than by setting BackColor on items, because
    /// ToolStripMenuItem.BackColor is ignored once a renderer draws the background, and
    /// partly works otherwise, which is worse than not working.
    ///
    /// High contrast wins over the dark preference, as everywhere else in MacRando. A user
    /// who has told the system they need a specific palette should not have it overridden
    /// by an app-level toggle they may not be able to see.
    /// </summary>
    internal sealed class TrayThemeRenderer : ToolStripProfessionalRenderer
    {
        private readonly TrayPalette _palette;

        public TrayThemeRenderer(TrayPalette palette)
            : base(new TrayColorTable(palette))
        {
            _palette = palette;
            RoundedEdges = false;
        }

        /// <summary>
        /// The item's own rectangle, in the graphics coordinates the renderer is handed.
        ///
        /// Not e.AffectedBounds, which does not exist on .NET Framework 4.8; the render
        /// graphics is already translated to the item, so the rectangle starts at the
        /// origin and spans the item's size.
        /// </summary>
        private static Rectangle ItemBounds(ToolStripItem item)
        {
            return new Rectangle(Point.Empty, item.Size);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }

            Rectangle bounds = ItemBounds(e.Item);
            using (SolidBrush brush = new SolidBrush(
                e.Item.Selected && e.Item.Enabled ? _palette.Selection : _palette.Surface))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            // A section header is disabled, so it never highlights, but it is drawn with a
            // rule under it so the groups read as groups.
            if (!e.Item.Enabled && e.Item.Font != null && e.Item.Font.Bold)
            {
                using (Pen pen = new Pen(_palette.Border))
                {
                    int y = bounds.Height - 1;
                    e.Graphics.DrawLine(pen, 8, y, bounds.Width - 8, y);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // A disabled item is how a section header is shown. Windows would draw it in a
            // washed-out grey that is unreadable on a dark surface, so disabled text is
            // drawn at a deliberate, readable dimness rather than the system default.
            e.TextColor = e.Item.Enabled
                ? _palette.Text
                : (string.IsNullOrWhiteSpace(e.Text) ? _palette.Text : _palette.TextMuted);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }
            using (Pen pen = new Pen(_palette.Border))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // The check mark is drawn by the base renderer in a colour that assumes a light
            // surface. Redrawn here in the text colour so it is visible on dark.
            if (e.Item == null)
            {
                return;
            }
            Rectangle box = new Rectangle(
                e.ImageRectangle.Left,
                e.ImageRectangle.Top,
                Math.Min(e.ImageRectangle.Width, 16),
                Math.Min(e.ImageRectangle.Height, 16));
            using (Pen pen = new Pen(_palette.Text))
            {
                e.Graphics.DrawRectangle(pen, box);
                e.Graphics.DrawLine(pen, box.Left + 3, box.Top + 8, box.Left + 6, box.Top + 11);
                e.Graphics.DrawLine(pen, box.Left + 6, box.Top + 11, box.Left + 12, box.Top + 3);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip == null)
            {
                return;
            }
            using (Pen pen = new Pen(_palette.Border))
            {
                Rectangle bounds = e.ToolStrip.ClientRectangle;
                e.Graphics.DrawRectangle(pen,
                    bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_palette.Surface))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }
            using (SolidBrush brush = new SolidBrush(
                e.Item.Selected && e.Item.Enabled ? _palette.Selection : _palette.Surface))
            {
                e.Graphics.FillRectangle(brush, ItemBounds(e.Item));
            }
        }
    }

    /// <summary>
    /// Feeds the colours the renderer and the colour table both need.
    ///
    /// Separate from the dashboard's own palette because this is a ToolStrip colour table,
    /// which wants a different set of keys from a set of control colours, and a
    /// ContextMenuStrip in a native WinForms app has no other source for them.
    /// </summary>
    internal sealed class TrayPalette
    {
        public Color Surface { get; set; }
        public Color Selection { get; set; }
        public Color Text { get; set; }
        public Color TextMuted { get; set; }
        public Color Border { get; set; }
        public Color ImageMargin { get; set; }

        /// <summary>The same colours the dashboard uses, so the two agree.</summary>
        public static TrayPalette Build(bool darkMode)
        {
            if (Accessibility.ShouldUseHighContrast())
            {
                return new TrayPalette
                {
                    Surface = SystemColors.Menu,
                    Selection = SystemColors.Highlight,
                    Text = SystemColors.MenuText,
                    TextMuted = SystemColors.MenuText,
                    Border = SystemColors.ControlDark,
                    ImageMargin = SystemColors.Menu
                };
            }

            if (!darkMode)
            {
                return new TrayPalette
                {
                    Surface = Color.White,
                    Selection = Color.FromArgb(219, 234, 254),
                    Text = Color.FromArgb(30, 41, 59),
                    TextMuted = Color.FromArgb(100, 116, 139),
                    Border = Color.FromArgb(203, 213, 225),
                    ImageMargin = Color.White
                };
            }

            return new TrayPalette
            {
                Surface = Color.FromArgb(30, 41, 59),
                Selection = Color.FromArgb(51, 65, 85),
                Text = Color.FromArgb(226, 232, 240),
                TextMuted = Color.FromArgb(148, 163, 184),
                Border = Color.FromArgb(71, 85, 105),
                ImageMargin = Color.FromArgb(30, 41, 59)
            };
        }
    }

    /// <summary>
    /// The colour table the professional renderer reads. Every key it can ask for is
    /// supplied, because an unset one falls back to a light default and shows through as a
    /// pale strip in an otherwise dark menu.
    /// </summary>
    internal sealed class TrayColorTable : ProfessionalColorTable
    {
        private readonly TrayPalette _palette;

        public TrayColorTable(TrayPalette palette)
        {
            _palette = palette;
            UseSystemColors = false;
        }

        public override Color MenuItemSelected { get { return _palette.Selection; } }
        public override Color MenuItemSelectedGradientBegin { get { return _palette.Selection; } }
        public override Color MenuItemSelectedGradientEnd { get { return _palette.Selection; } }
        public override Color MenuItemBorder { get { return _palette.Selection; } }
        public override Color MenuBorder { get { return _palette.Border; } }
        public override Color ToolStripDropDownBackground { get { return _palette.Surface; } }
        public override Color ImageMarginGradientBegin { get { return _palette.ImageMargin; } }
        public override Color ImageMarginGradientMiddle { get { return _palette.ImageMargin; } }
        public override Color ImageMarginGradientEnd { get { return _palette.ImageMargin; } }
        public override Color SeparatorDark { get { return _palette.Border; } }
        public override Color SeparatorLight { get { return _palette.Border; } }
        public override Color CheckBackground { get { return _palette.Surface; } }
        public override Color CheckSelectedBackground { get { return _palette.Selection; } }
        public override Color CheckPressedBackground { get { return _palette.Selection; } }
    }

    internal static class TrayTheme
    {
        /// <summary>
        /// Applies the theme to a menu, following the dashboard's dark-mode toggle.
        ///
        /// The renderer is swapped rather than the items recoloured, because the items are
        /// rebuilt on every refresh and recolouring each one would mean remembering to do
        /// it every time a new item is added. One renderer covers items added later.
        /// </summary>
        public static void Apply(ToolStrip menu, bool darkMode)
        {
            if (menu == null)
            {
                return;
            }

            TrayPalette palette = TrayPalette.Build(darkMode);
            menu.BackColor = palette.Surface;
            menu.ForeColor = palette.Text;
            // Only the renderer is assigned. Setting RenderMode as well is the trap here,
            // and it is silent: assigning a custom Renderer sets the mode to Custom, and
            // then assigning RenderMode.Professional replaces that renderer with a stock
            // ToolStripProfessionalRenderer, which draws the light palette from a fresh
            // ProfessionalColorTable. The menu then looks almost right, because the item
            // foregrounds set below still apply, while the background stays white. The
            // mode is left alone so the renderer survives.
            menu.Renderer = new TrayThemeRenderer(palette);

            // The items themselves still need their foreground set, because a ToolStripItem
            // carries its own colour and it overrides what the renderer paints. A disabled
            // item is a section header, which the renderer already dims; setting the
            // renderer colour for those would undo it.
            foreach (ToolStripItem item in menu.Items)
            {
                ApplyToItem(item, palette);
            }
        }

        private static void ApplyToItem(ToolStripItem item, TrayPalette palette)
        {
            if (item == null)
            {
                return;
            }

            if (item.Enabled)
            {
                item.ForeColor = palette.Text;
            }
            else
            {
                item.ForeColor = palette.TextMuted;
            }
            item.BackColor = palette.Surface;
            if (item is ToolStripDropDownItem)
            {
                ToolStripDropDownItem dropDown = (ToolStripDropDownItem)item;
                dropDown.DropDown.BackColor = palette.Surface;
                foreach (ToolStripItem child in dropDown.DropDownItems)
                {
                    ApplyToItem(child, palette);
                }
            }
        }

    }
}
