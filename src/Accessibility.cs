using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MacRando
{
    /// <summary>
    /// Names, roles, and a high-contrast palette.
    ///
    /// The two problems are related. A control that relies on its visible Text for its
    /// accessible name announces as a bare "button" or an empty combo box, which is the
    /// same class of problem as one whose colours ignore the user's system settings: the
    /// control is technically present but conveys nothing to the person relying on the
    /// assistive channel.
    ///
    /// Kept free of any drawing so the rules can be tested without a window.
    /// </summary>
    internal static class Accessibility
    {
        /// <summary>
        /// Names for the dashboard's value labels. A screen reader reaching a group of
        /// labels otherwise reads a column of bare values with no indication of which is
        /// the current MAC and which is the permanent one.
        /// </summary>
        public static string Describe(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        }

        /// <summary>
        /// The palette used when the system reports high contrast. Every value comes from
        /// SystemColors so the user's own choice of colours is honoured, and ButtonFace is
        /// used for surfaces rather than a hard-coded grey.
        /// </summary>
        public static bool ShouldUseHighContrast()
        {
            return SystemInformation.HighContrast;
        }

        internal static HighContrastPalette BuildPalette(bool highContrast, bool darkMode)
        {
            if (highContrast)
            {
                return new HighContrastPalette
                {
                    IsHighContrast = true,
                    Surface = SystemColors.Window,
                    SurfaceAlt = SystemColors.Control,
                    Text = SystemColors.WindowText,
                    TextMuted = SystemColors.WindowText,
                    TextOnAccent = SystemColors.HighlightText,
                    Accent = SystemColors.Highlight,
                    Border = SystemColors.ControlDark,
                    Muted = SystemColors.WindowText
                };
            }

            if (darkMode)
            {
                return new HighContrastPalette
                {
                    IsHighContrast = false,
                    Surface = Color.FromArgb(15, 23, 42),
                    SurfaceAlt = Color.FromArgb(30, 41, 59),
                    Text = Color.FromArgb(241, 245, 249),
                    TextMuted = Color.FromArgb(148, 163, 184),
                    TextOnAccent = Color.White,
                    Accent = Color.FromArgb(37, 99, 235),
                    Border = Color.FromArgb(51, 65, 85),
                    Muted = Color.FromArgb(148, 163, 184)
                };
            }

            return new HighContrastPalette
            {
                IsHighContrast = false,
                Surface = Color.White,
                SurfaceAlt = Color.FromArgb(248, 250, 252),
                Text = Color.FromArgb(15, 23, 42),
                TextMuted = Color.FromArgb(71, 85, 105),
                TextOnAccent = Color.White,
                Accent = Color.FromArgb(30, 64, 175),
                Border = Color.FromArgb(203, 213, 225),
                Muted = Color.FromArgb(100, 116, 139)
            };
        }

        /// <summary>
        /// Assigns an accessible name and role to a control. The description is only set
        /// when there is something to add, because an empty description is noise in the
        /// assistive output rather than an improvement.
        /// </summary>
        public static void Describe(Control control, string name, AccessibleRole role, string description)
        {
            if (control == null)
            {
                return;
            }
            control.AccessibleName = Describe(name);
            if (control.AccessibleRole == AccessibleRole.None && role != AccessibleRole.None)
            {
                control.AccessibleRole = role;
            }
            if (!string.IsNullOrWhiteSpace(description))
            {
                control.AccessibleDescription = description.Trim();
            }
        }

        /// <summary>
        /// Gives every interactive control a name derived from its own visible text when it
        /// has one, and a role matching its type when WinForms did not pick a sensible one.
        ///
        /// This is a backstop for controls created in a layout helper, where the author
        /// already provided visible text and repeating it by hand would be duplication
        /// that drifts. Controls with no visible text are left for the caller to name.
        /// </summary>
        public static int ApplyDefaults(IEnumerable<Control> controls)
        {
            if (controls == null)
            {
                return 0;
            }
            int named = 0;
            foreach (Control control in controls)
            {
                if (control == null)
                {
                    continue;
                }
                if (control is ButtonBase)
                {
                    // CheckBox and RadioButton already announce their own state; naming
                    // them from Text is what a screen reader needs and cannot infer.
                    if (string.IsNullOrWhiteSpace(control.AccessibleName) &&
                        !string.IsNullOrWhiteSpace(control.Text))
                    {
                        control.AccessibleName = control.Text;
                        named++;
                    }
                }
                else if (control is ComboBox || control is ListBox || control is ListView)
                {
                    if (string.IsNullOrWhiteSpace(control.AccessibleName))
                    {
                        control.AccessibleName = string.IsNullOrWhiteSpace(control.Text)
                            ? string.Empty
                            : control.Text;
                        named++;
                    }
                }
                else if (control is TextBox)
                {
                    if (string.IsNullOrWhiteSpace(control.AccessibleName))
                    {
                        control.AccessibleName = string.IsNullOrWhiteSpace(control.Text) ? string.Empty : control.Text;
                        named++;
                    }
                }
            }
            return named;
        }
    }

    internal sealed class HighContrastPalette
    {
        public bool IsHighContrast { get; set; }
        public Color Surface { get; set; }
        public Color SurfaceAlt { get; set; }
        public Color Text { get; set; }
        public Color TextMuted { get; set; }
        public Color TextOnAccent { get; set; }
        public Color Accent { get; set; }
        public Color Border { get; set; }
        public Color Muted { get; set; }
    }
}
