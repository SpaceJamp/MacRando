using System;
using MacRando;
using AppSettings = MacRando.AppSettings;

/// <summary>
/// Notification logic.
///
/// The quiet hours and severity rules used to live only inside TrayContext, reachable
/// only by running the whole application. Extracted into NotificationDisplayPolicy so
/// they can be checked here, the same way NetworkAutoApply and KeepChangePolicy already
/// are. The cases that matter are the ones that are easy to get wrong and impossible to
/// notice by eye: a quiet window that wraps midnight, a critical notification that must
/// still get through, and a disabled setting that must silence everything.
/// </summary>
internal static class NotificationTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            NotificationKindsNormalize();
            NotificationKindsIsCritical();
            RetryKindsNormalize();
            RetryKindsFromFlags();
            DisplayDisabledSilencesEverything();
            DisplayQuietHoursNonCritical();
            DisplayCriticalBypassesQuietHours();
            DisplayOutsideQuietHours();
            QuietHoursSameStartAndEnd();
            QuietHoursAscendingWindow();
            QuietHoursWrappingWindow();
            QuietHoursClampsHours();
            QuietHoursNullSettings();
            Console.WriteLine("notification-tests=OK;checks=" + _checks);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static AppSettings QuietSettings(int start, int end)
    {
        return new AppSettings
        {
            NotificationsEnabled = true,
            NotificationQuietHoursEnabled = true,
            NotificationQuietHoursStartHour = start,
            NotificationQuietHoursEndHour = end
        };
    }

    // ------------------------------------------------------------- NotificationKinds

    private static void NotificationKindsNormalize()
    {
        Check(NotificationKinds.Normalize(null) == NotificationKinds.Info,
            "null should normalize to Info.");
        Check(NotificationKinds.Normalize("") == NotificationKinds.Info,
            "empty should normalize to Info.");
        Check(NotificationKinds.Normalize("   ") == NotificationKinds.Info,
            "whitespace should normalize to Info.");
        Check(NotificationKinds.Normalize("warning") == NotificationKinds.Warning,
            "normalization should be case-insensitive.");
        Check(NotificationKinds.Normalize("CRITICAL") == NotificationKinds.Critical,
            "normalization should be case-insensitive.");
        Check(NotificationKinds.Normalize("nonsense") == NotificationKinds.Info,
            "an unknown value should fall back to Info, not throw.");
    }

    private static void NotificationKindsIsCritical()
    {
        Check(NotificationKinds.IsCritical(NotificationKinds.Critical),
            "Critical should be critical.");
        Check(NotificationKinds.IsCritical(NotificationKinds.Error),
            "Error should be critical, so errors get through quiet hours.");
        Check(!NotificationKinds.IsCritical(NotificationKinds.Info),
            "Info should not be critical.");
        Check(!NotificationKinds.IsCritical(NotificationKinds.Success),
            "Success should not be critical.");
        Check(!NotificationKinds.IsCritical(NotificationKinds.Warning),
            "Warning should not be critical.");
        // Normalization should happen inside IsCritical too.
        Check(NotificationKinds.IsCritical("error"),
            "IsCritical should normalize its input.");
    }

    // ------------------------------------------------------------- RetryKinds

    private static void RetryKindsNormalize()
    {
        Check(RetryKinds.Normalize(null) == RetryKinds.None,
            "null should normalize to None.");
        Check(RetryKinds.Normalize("") == RetryKinds.None,
            "empty should normalize to None.");
        Check(RetryKinds.Normalize("mac") == RetryKinds.Mac,
            "normalization should be case-insensitive.");
        Check(RetryKinds.Normalize("MACANDIP") == RetryKinds.MacAndIp,
            "normalization should be case-insensitive.");
        Check(RetryKinds.Normalize("garbage") == RetryKinds.None,
            "an unknown value should fall back to None.");
    }

    private static void RetryKindsFromFlags()
    {
        Check(RetryKinds.FromFlags(true, true) == RetryKinds.MacAndIp,
            "both flags should give MacAndIp.");
        Check(RetryKinds.FromFlags(true, false) == RetryKinds.Mac,
            "MAC only should give Mac.");
        Check(RetryKinds.FromFlags(false, true) == RetryKinds.Ip,
            "IP only should give Ip.");
        Check(RetryKinds.FromFlags(false, false) == RetryKinds.None,
            "neither flag should give None.");
    }

    // ------------------------------------------------------------- display

    private static void DisplayDisabledSilencesEverything()
    {
        var settings = new AppSettings { NotificationsEnabled = false };
        foreach (string severity in new[] { NotificationKinds.Info, NotificationKinds.Warning,
            NotificationKinds.Error, NotificationKinds.Critical })
        {
            Check(!NotificationDisplayPolicy.ShouldDisplay(settings, severity, 12),
                "with notifications disabled, " + severity + " must not display.");
        }
    }

    private static void DisplayQuietHoursNonCritical()
    {
        // 22:00 to 07:00, and it is 23:00. A non-critical notification is suppressed.
        var settings = QuietSettings(22, 7);
        Check(!NotificationDisplayPolicy.ShouldDisplay(settings, NotificationKinds.Info, 23),
            "a non-critical notification inside quiet hours must be suppressed.");
        Check(!NotificationDisplayPolicy.ShouldDisplay(settings, NotificationKinds.Warning, 23),
            "a warning inside quiet hours must be suppressed.");
    }

    private static void DisplayCriticalBypassesQuietHours()
    {
        // Critical and Error get through quiet hours. A user who turned quiet hours on
        // still needs to know about a failed restore.
        var settings = QuietSettings(22, 7);
        Check(NotificationDisplayPolicy.ShouldDisplay(settings, NotificationKinds.Critical, 23),
            "Critical must bypass quiet hours.");
        Check(NotificationDisplayPolicy.ShouldDisplay(settings, NotificationKinds.Error, 23),
            "Error must bypass quiet hours.");
    }

    private static void DisplayOutsideQuietHours()
    {
        var settings = QuietSettings(22, 7);
        Check(NotificationDisplayPolicy.ShouldDisplay(settings, NotificationKinds.Info, 12),
            "outside quiet hours, a normal notification must display.");
    }

    // ------------------------------------------------------------- IsQuietHours

    private static void QuietHoursSameStartAndEnd()
    {
        // Equal hours mean "no quiet hours", not "always quiet".
        var settings = QuietSettings(22, 22);
        for (int hour = 0; hour < 24; hour++)
        {
            Check(!NotificationDisplayPolicy.IsQuietHours(settings, hour),
                "equal start and end must disable quiet hours at hour " + hour + ".");
        }
    }

    private static void QuietHoursAscendingWindow()
    {
        // 09:00 to 17:00, a simple daytime window.
        var settings = QuietSettings(9, 17);
        Check(NotificationDisplayPolicy.IsQuietHours(settings, 9), "the start hour is inside.");
        Check(NotificationDisplayPolicy.IsQuietHours(settings, 12), "midday is inside.");
        Check(!NotificationDisplayPolicy.IsQuietHours(settings, 17), "the end hour is outside (half-open).");
        Check(!NotificationDisplayPolicy.IsQuietHours(settings, 8), "the hour before is outside.");
        Check(!NotificationDisplayPolicy.IsQuietHours(settings, 20), "the evening is outside.");
    }

    private static void QuietHoursWrappingWindow()
    {
        // 22:00 to 07:00, wrapping midnight. This is the default and the case most
        // likely to be implemented with an off-by-one.
        var settings = QuietSettings(22, 7);
        Check(NotificationDisplayPolicy.IsQuietHours(settings, 22), "22:00 is inside.");
        Check(NotificationDisplayPolicy.IsQuietHours(settings, 23), "23:00 is inside.");
        Check(NotificationDisplayPolicy.IsQuietHours(settings, 0), "00:00 is inside.");
        Check(NotificationDisplayPolicy.IsQuietHours(settings, 6), "06:00 is inside.");
        Check(!NotificationDisplayPolicy.IsQuietHours(settings, 7), "07:00 is outside (half-open).");
        Check(!NotificationDisplayPolicy.IsQuietHours(settings, 12), "midday is outside.");
        Check(!NotificationDisplayPolicy.IsQuietHours(settings, 21), "21:00 is outside.");
    }

    private static void QuietHoursClampsHours()
    {
        // Hours outside 0..23 are clamped rather than wrapping or throwing.
        var settings = QuietSettings(25, -3);
        // 25 clamps to 23, -3 clamps to 0: window 23..0, which wraps, so 23 is inside.
        Check(NotificationDisplayPolicy.IsQuietHours(settings, 23),
            "an out-of-range start should be clamped, not crash.");
    }

    private static void QuietHoursNullSettings()
    {
        Check(!NotificationDisplayPolicy.IsQuietHours(null, 12),
            "null settings must not throw and must report not-quiet.");
        Check(!NotificationDisplayPolicy.ShouldDisplay(null, NotificationKinds.Info, 12),
            "null settings must not display.");
    }
}