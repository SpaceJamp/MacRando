using System;
using System.Drawing;

namespace MacRando
{
    /// <summary>
    /// The text and emphasis the tray menu shows for the current state.
    ///
    /// Separated from the menu itself so the wording can be tested. The status line is
    /// the one piece of MacRando text a user reliably reads, because it is at the top of
    /// the menu and it is the only thing that does not require a click, so a mistake in
    /// it is both easy to make and hard to notice.
    /// </summary>
    internal static class TrayMenuState
    {
        /// <summary>
        /// A pending restore is called out explicitly rather than left for the user to
        /// infer from a menu item being enabled, because it is the only state that means
        /// an adapter is not currently in the configuration the user left it in.
        /// </summary>
        public static string StatusText(int pendingRestoreCount, bool elevated)
        {
            if (!elevated)
            {
                return "Administrator permission required";
            }
            if (pendingRestoreCount > 0)
            {
                return "Ready - " + pendingRestoreCount +
                    (pendingRestoreCount == 1 ? " adapter needs restoring" : " adapters need restoring");
            }
            return "Ready";
        }

        /// <summary>
        /// Amber when something needs restoring, red when the app cannot act at all, and
        /// the system text colour otherwise. Not chosen to be alarming: a menu that is
        /// permanently coloured teaches a user to ignore the colour.
        /// </summary>
        public static Color StatusColor(int pendingRestoreCount, bool elevated)
        {
            if (!elevated)
            {
                return Color.FromArgb(185, 28, 28);
            }
            return pendingRestoreCount > 0
                ? Color.FromArgb(180, 83, 9)
                : SystemColors.ControlText;
        }

        /// <summary>
        /// The same three states, resolved for whichever menu surface is showing them.
        ///
        /// The light values above are kept as they are because the menu was light until
        /// recently and a user with a light dashboard should see no change. A dark menu
        /// needs its own values: SystemColors.ControlText is near-black and would be
        /// unreadable on the dark surface, and the light amber would sit on dark at a
        /// contrast that reads as a different, weaker signal than intended.
        ///
        /// Every pairing is checked for contrast by the test suite, because a status line
        /// that is unreadable is the one failure that a user would not report so much as
        /// quietly stop trusting.
        /// </summary>
        public static Color StatusColorFor(int pendingRestoreCount, bool elevated, bool darkMode)
        {
            if (darkMode)
            {
                if (!elevated)
                {
                    return Color.FromArgb(252, 165, 165);
                }
                return pendingRestoreCount > 0
                    ? Color.FromArgb(251, 191, 36)
                    : Color.FromArgb(226, 232, 240);
            }

            if (!elevated)
            {
                return Color.FromArgb(185, 28, 28);
            }
            return pendingRestoreCount > 0
                ? Color.FromArgb(180, 83, 9)
                : Color.FromArgb(30, 41, 59);
        }

        public static string NotifyText(int pendingRestoreCount, bool elevated)
        {
            if (!elevated)
            {
                return "MacRando - administrator permission required";
            }
            if (pendingRestoreCount > 0)
            {
                return "MacRando - " + pendingRestoreCount +
                    (pendingRestoreCount == 1 ? " adapter needs restoring" : " adapters need restoring");
            }
            return "MacRando - ready";
        }

        public static string RestoreAllText(int pendingRestoreCount)
        {
            return pendingRestoreCount > 0
                ? "Restore all pending profiles (" + pendingRestoreCount + ")"
                : "Restore all pending profiles";
        }
    }
}
