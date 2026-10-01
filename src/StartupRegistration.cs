using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MacRando
{
    /// <summary>
    /// Registers MacRando to start when Windows starts.
    ///
    /// This used to be a value in the Run key, and it never worked. MacRando's manifest
    /// requires administrator, and Explorer cannot elevate a Run key entry: it asks for the
    /// process, the elevation cannot be satisfied from that path, and the launch produces
    /// nothing. Windows records the attempt and the completion in the same second, with
    /// PID 0, meaning no process was ever created. On this machine that was 10 attempts
    /// across 3 days, all of them silent, with no error dialog and nothing in MacRando's
    /// own log because the application never started to write one.
    ///
    /// A scheduled task at logon, registered to run with the highest privileges, is the
    /// mechanism Windows provides for an application that legitimately needs elevation.
    /// It starts silently, with no prompt at every logon.
    ///
    /// The Run value is deleted when the task is registered. Leaving it would mean two
    /// registrations of which one always fails, and the dead one would keep appearing in
    /// startup troubleshooting tools as though it were doing something.
    ///
    /// Values are passed to PowerShell through the environment, never interpolated into a
    /// command line, so an installation path containing a quote or a space cannot alter
    /// what is registered. That is the same rule the update helper follows.
    /// </summary>
    internal static class StartupRegistration
    {
        /// <summary>
        /// The task name. Kept stable so the uninstaller can remove it.
        /// </summary>
        public const string TaskName = "MacRando";

        private const int TimeoutMilliseconds = 30000;

        /// <summary>
        /// Registers or removes the logon task.
        ///
        /// Called from the application, which requires administrator, so the task can be
        /// created with the highest run level without a further prompt.
        /// </summary>
        public static bool Set(bool enabled, string executablePath)
        {
            string script = enabled
                ? RegisterScript
                : UnregisterScript;

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.Arguments += Convert.ToBase64String(
                Encoding.Unicode.GetBytes(Environment.NewLine + script));
            startInfo.EnvironmentVariables["MR_EXECUTABLE"] = executablePath ?? string.Empty;

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return false;
                    }

                    // Both streams are drained concurrently, before waiting. Reading one to
                    // the end and then the other deadlocks as soon as the child fills the
                    // pipe that is not being read: the child blocks on its write and never
                    // exits. PowerShellRunner does the same for the same reason.
                    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardError = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        // Left running it would outlive the application and could
                        // register the task after the user had turned the setting off.
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    process.WaitForExit();

                    string error = null;
                    try
                    {
                        error = standardError.Result;
                    }
                    catch
                    {
                    }

                    if (process.ExitCode != 0)
                    {
                        AppLogger.Warning(
                            "Could not " + (enabled ? "register" : "remove") + " the logon task: " +
                            AppLogger.Sanitize(error));
                        Observe(standardOutput);
                        Observe(standardError);
                        return false;
                    }
                    Observe(standardOutput);
                    Observe(standardError);
                }
            }
            catch (Exception exception)
            {
                AppLogger.Warning("Could not reach Windows PowerShell to change the logon task: " + exception.Message);
                return false;
            }

            RemoveLegacyRunValue();
            return true;
        }

        /// <summary>
        /// Whether the logon task exists.
        ///
        /// Checked on every startup, so it has to be cheap. Task Scheduler keeps each task
        /// definition as a file under System32\Tasks, and looking for that file is a single
        /// stat. <c>schtasks /Query</c> was the obvious choice and is a process spawn that
        /// takes hundreds of milliseconds, on the constructor path of the whole
        /// application, which is a real cost for a question with a cheap answer.
        ///
        /// schtasks is still asked when the file is not there, because a file check that is
        /// wrong in the direction of "absent" would make the application believe it is not
        /// registered and disable the setting.
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                string tasks = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", TaskName);
                if (File.Exists(tasks))
                {
                    return true;
                }
            }
            catch
            {
            }
            return QueryWithSchtasks();
        }

        private static bool QueryWithSchtasks()
        {
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/Query /TN \"" + TaskName + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    if (process == null)
                    {
                        return false;
                    }
                    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardError = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    process.WaitForExit();
                    Observe(standardOutput);
                    Observe(standardError);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Drains a stream whose result is not wanted, so an unread pipe cannot wedge the
        /// process it belongs to.
        /// </summary>
        private static void Observe(Task<string> task)
        {
            if (task == null)
            {
                return;
            }
            task.ContinueWith(
                completed => { AggregateException ignored = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Removes the Run value the old registration wrote.
        ///
        /// Best effort by design. This is cleanup of something that never worked, so a
        /// failure to delete it must not be reported as a failure to register the task,
        /// which would make the setting appear to fail when it has in fact succeeded.
        /// </summary>
        private static void RemoveLegacyRunValue()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(TaskName, false);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Registers a logon trigger with the highest run level.
        ///
        /// The task name is fixed rather than taken from the environment, so nothing that
        /// reaches this script can create a task under a name of its choosing. The path and
        /// argument come from the environment.
        ///
        /// $env:MR_EXECUTABLE rather than an interpolated path: the installation directory
        /// is user-visible and can contain spaces, quotes or an ampersand, and building the
        /// action definition by string concatenation is how a path becomes a command.
        /// </summary>
        private const string RegisterScript = @"
$ErrorActionPreference = 'Stop'
$exe = $env:MR_EXECUTABLE
if ([string]::IsNullOrWhiteSpace($exe)) { throw 'No executable path was supplied.' }

$action = New-ScheduledTaskAction -Execute $exe -Argument '--startup'
$trigger = New-ScheduledTaskTrigger -AtLogOn -User ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'MacRando' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Output 'registered'
";

        /// <summary>
        /// Removes the task, ignoring the case where it is already gone.
        /// </summary>
        private const string UnregisterScript = @"
$ErrorActionPreference = 'Stop'
try {
  Unregister-ScheduledTask -TaskName 'MacRando' -Confirm:$false -ErrorAction Stop | Out-Null
  Write-Output 'removed'
}
catch {
  # Already absent is the desired end state, not a failure.
  Write-Output 'absent'
}
";
    }
}