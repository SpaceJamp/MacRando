using System;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Coverage for the logon registration.
///
/// The previous registration wrote a Run key value, and it never worked: MacRando requires
/// administrator, Explorer cannot elevate a Run key entry, and Windows recorded 10 attempts
/// across three days each completing with PID 0, meaning no process was ever created. There
/// was no error dialog and nothing in MacRando's log, because the application never started.
///
/// So the assertions here are about the mechanism, not the success: that the scheduled task
/// runs with the highest privileges, that it triggers at logon, and that the path is passed
/// through the environment rather than built into the script. The last of those is the one
/// that would otherwise be a security bug rather than a functional one.
/// </summary>
internal static class StartupRegistrationTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            TheRegistrationRunsWithTheHighestPrivileges();
            TheTriggerIsAtLogOn();
            TheExecutableComesFromTheEnvironment();
            TheTaskNameIsFixed();
            TheRunKeyIsNoLongerUsed();
            TheUninstallerRemovesTheTask();
            Console.WriteLine("startup-registration-tests=OK;checks=" + _checks);
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

    private static string RepositoryFile(string relative)
    {
        string root = Environment.GetEnvironmentVariable("MACRANDO_REPO_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }
        string path = System.IO.Path.Combine(root, relative);
        return System.IO.File.Exists(path)
            ? System.IO.File.ReadAllText(path, Encoding.UTF8)
            : null;
    }

    private static string Source()
    {
        string text = RepositoryFile(System.IO.Path.Combine("src", "StartupRegistration.cs"));
        if (text == null)
        {
            throw new Exception("src\\StartupRegistration.cs was not found, so the logon registration " +
                "cannot be checked. This test needs the repository root.");
        }
        return text;
    }

    /// <summary>
    /// The whole reason this changed. Without RunLevel Highest, the task starts at the
    /// logged-on user's normal integrity, the manifest's requireAdministrator is then
    /// unsatisfiable from a logon trigger, and it fails exactly the way the Run key did.
    /// </summary>
    private static void TheRegistrationRunsWithTheHighestPrivileges()
    {
        string source = Source();
        Check(Regex.IsMatch(source, @"New-ScheduledTaskPrincipal[^;]*-RunLevel\s+Highest"),
            "The logon task is not registered with -RunLevel Highest, so it would start without " +
            "elevation and fail the same way the Run key did.");
        Check(Regex.IsMatch(source, @"-LogonType\s+Interactive"),
            "The logon task does not specify an interactive logon type, so it may be registered " +
            "for a session type that never fires on a normal sign-in.");
        Check(source.IndexOf("-ExecutionTimeLimit ([TimeSpan]::Zero)", StringComparison.Ordinal) >= 0,
            "The logon task has no execution time limit removed. The default limit stops a long " +
            "running task, and MacRando is a tray application meant to stay running.");
        Check(source.IndexOf("-MultipleInstances IgnoreNew", StringComparison.Ordinal) >= 0,
            "The logon task does not ignore a second instance. Two logons, or a logon while it " +
            "is already running, would start a second copy.");
    }

    private static void TheTriggerIsAtLogOn()
    {
        string source = Source();
        Check(Regex.IsMatch(source, @"New-ScheduledTaskTrigger\s+-AtLogOn"),
            "The registration does not trigger at logon, which is the only trigger that starts " +
            "MacRando when Windows starts.");
        Check(Regex.IsMatch(source, @"-Force\b"),
            "The registration does not pass -Force, so re-registering over an existing task " +
            "would fail rather than update it.");
    }

    /// <summary>
    /// The installation path is user-visible and can contain spaces, quotes or an ampersand.
    /// Building the action by concatenating it into a script would let a crafted path become
    /// a command, so the path travels through the environment and the script only ever reads
    /// a variable.
    /// </summary>
    private static void TheExecutableComesFromTheEnvironment()
    {
        string source = Source();
        Check(source.IndexOf("MR_EXECUTABLE", StringComparison.Ordinal) >= 0,
            "The executable path is not passed through the environment.");
        Check(source.IndexOf("$exe = $env:MR_EXECUTABLE", StringComparison.Ordinal) >= 0,
            "The registration script does not read the executable path from the environment.");
        Check(Regex.IsMatch(source, @"-Execute\s+\$exe"),
            "The task action does not use the value read from the environment.");

        // The argument must be a literal, not anything assembled from a path.
        Check(Regex.IsMatch(source, @"-Argument\s+'?--startup"),
            "The task action does not pass --startup, so MacRando would not know it was launched " +
            "at logon and would show the dashboard instead of starting in the tray.");
    }

    private static void TheTaskNameIsFixed()
    {
        string source = Source();
        // Fixed rather than taken from the environment, so nothing reaching the script can
        // create a task under a name of its choosing.
        Check(source.IndexOf("TaskName 'MacRando'", StringComparison.Ordinal) >= 0,
            "The task name is not fixed in the registration script.");
        Check(source.IndexOf("public const string TaskName = \"MacRando\"", StringComparison.Ordinal) >= 0,
            "StartupRegistration.TaskName is not the expected stable name, which the uninstaller " +
            "depends on to remove the task.");

        // The old Run value has to go, or two registrations exist and one of them always fails.
        Check(source.IndexOf("DeleteValue", StringComparison.Ordinal) >= 0,
            "The legacy Run key value is not removed, so a dead second registration is left behind.");
    }

    private static void TheRunKeyIsNoLongerUsed()
    {
        string tray = RepositoryFile(System.IO.Path.Combine("src", "TrayContext.cs"));
        if (tray == null)
        {
            return;
        }
        Check(tray.IndexOf("SetValue(\"MacRando\"", StringComparison.Ordinal) < 0,
            "TrayContext still writes the MacRando Run key value. That registration never worked, " +
            "because Explorer cannot elevate it.");
        Check(tray.IndexOf("StartupRegistration.Set", StringComparison.Ordinal) >= 0,
            "TrayContext does not delegate startup registration to StartupRegistration.");
    }

    /// <summary>
    /// Uninstall has to remove the task. It cannot leave a task behind that launches an
    /// executable which is no longer installed, at every logon, silently.
    /// </summary>
    private static void TheUninstallerRemovesTheTask()
    {
        string installer = RepositoryFile("installer.iss");
        if (installer == null)
        {
            return;
        }

        // schtasks rather than a PowerShell one-liner, because Inno reads braces in a
        // parameter value as a constant reference. A try/catch one-liner does not survive
        // that, and the installer failed to compile with "Unknown constant" until it was
        // replaced. Asserted on the mechanism so the braces cannot come back.
        Check(Regex.IsMatch(installer, @"(?i)Filename:\s*""schtasks\.exe"";\s*Parameters:\s*""/Delete /TN MacRando /F"""),
            "installer.iss does not remove the MacRando logon task with schtasks on uninstall, " +
            "which would leave a task launching an executable that is no longer installed, at " +
            "every logon, silently.");

        Match uninstallRun = Regex.Match(installer, @"(?ms)^\[UninstallRun\](.*?)(?=^\[)", RegexOptions.None);
        Check(uninstallRun.Success, "installer.iss has no [UninstallRun] section.");
        Check(!Regex.IsMatch(uninstallRun.Groups[1].Value, @"(?i)Filename:\s*""powershell"),
            "The uninstaller invokes PowerShell. An Inno parameter value containing braces is read " +
            "as a constant reference, so this does not compile.");

        // The installer's own "start with Windows" option must not go on writing the Run
        // key, which is the registration that never worked.
        Match registry = Regex.Match(installer, @"(?ms)^\[Registry\](.*?)(?=^\[)", RegexOptions.None);
        Check(registry.Success, "installer.iss has no [Registry] section.");
        Check(registry.Groups[1].Value.IndexOf("CurrentVersion\\Run", StringComparison.OrdinalIgnoreCase) < 0,
            "installer.iss still writes the Run key for its 'start with Windows' option, which " +
            "cannot work for an application that requires administrator.");

        Check(Regex.IsMatch(installer, @"(?i)Register-ScheduledTask"),
            "installer.iss does not create the scheduled task for its 'start with Windows' option, " +
            "so enabling it during install would leave the feature unconfigured.");

        // Same registration shape as the application, so the two cannot disagree about
        // whether it is running at the highest privilege level.
        Check(Regex.IsMatch(installer, @"-RunLevel\s+Highest"),
            "The installer's logon task is not registered with -RunLevel Highest, so it would " +
            "fail the same way the Run key did.");
    }
}