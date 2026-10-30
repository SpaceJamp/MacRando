using System;

namespace MacRando
{
    /// <summary>
    /// Deciding whether a notification is shown right now.
    ///
    /// Kept as a pure function with no UI, settings store, or clock access so the quiet
    /// hours and severity rules can be tested exhaustively. TrayContext supplies the
    /// settings and the current hour; everything else is decided here.
    /// </summary>
    internal static class NotificationDisplayPolicy
    {
        public static bool ShouldDisplay(AppSettings settings, string severity, int nowHour)
        {
            if (settings == null)
            {
                return false;
            }
            if (!settings.NotificationsEnabled)
            {
                return false;
            }
            if (settings.NotificationQuietHoursEnabled &&
                !NotificationKinds.IsCritical(severity) &&
                IsQuietHours(settings, nowHour))
            {
                return false;
            }
            return true;
        }

        public static bool IsQuietHours(AppSettings settings, int nowHour)
        {
            if (settings == null)
            {
                return false;
            }
            int start = Math.Max(0, Math.Min(23, settings.NotificationQuietHoursStartHour));
            int end = Math.Max(0, Math.Min(23, settings.NotificationQuietHoursEndHour));
            int now = Math.Max(0, Math.Min(23, nowHour));
            if (start == end)
            {
                return false;
            }
            return start < end
                ? now >= start && now < end
                : now >= start || now < end;
        }
    }
}
