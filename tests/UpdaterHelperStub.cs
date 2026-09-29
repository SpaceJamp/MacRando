using System;
using System.IO;
using System.Threading;

/// <summary>
/// Stand-in for the MacRando executable when testing the update install helper.
///
/// Controlled entirely through the environment, so one binary can play every role the
/// helper has to handle:
///   MACRANDO_STUB_HEALTHY=0    crashes immediately, the way a build that will not start does
///   MACRANDO_STUB_NO_MARKER=1  stays alive but never writes the startup marker, the way a
///                              slow start under antivirus looks
///   otherwise                  sleeps MACRANDO_STUB_DELAY_MS then writes the marker
/// </summary>
internal static class UpdaterHelperStub
{
    private static int Main()
    {
        string marker = Environment.GetEnvironmentVariable("MACRANDO_MARKER");
        int delayMs = 1500;
        int.TryParse(Environment.GetEnvironmentVariable("MACRANDO_STUB_DELAY_MS"), out delayMs);

        if (Environment.GetEnvironmentVariable("MACRANDO_STUB_HEALTHY") == "0")
        {
            return 3;
        }

        if (Environment.GetEnvironmentVariable("MACRANDO_STUB_NO_MARKER") == "1")
        {
            Thread.Sleep(60000);
            return 0;
        }

        Thread.Sleep(delayMs);

        try
        {
            if (!string.IsNullOrEmpty(marker))
            {
                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            }
        }
        catch
        {
        }
        return 0;
    }
}
