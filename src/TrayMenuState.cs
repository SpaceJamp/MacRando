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
