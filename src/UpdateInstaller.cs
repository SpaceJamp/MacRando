using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MacRando
{
    /// <summary>
    /// Replaces the running executable with a verified download and restarts it.
    ///
    /// A running process cannot overwrite its own image, so the work is handed to a short
    /// lived PowerShell helper that waits for this process to exit, swaps the file, launches
    /// the new build, and rolls back if the new build never reports a successful start.
    /// The build is only ever installed after UpdateService has verified both the SHA-256
    /// and the Authenticode signer of the download.
    /// </summary>
    internal static class UpdateInstaller
    {
        public const string StartupMarkerPrefix = "update-startup-";
        private const int DefaultWatchdogSeconds = 40;
        private const int DefaultExitWaitSeconds = 300;

        public static string GetStartupMarkerPath(string version)
        {
            string dataDirectory = GetDataDirectory();
            return Path.Combine(dataDirectory, StartupMarkerPrefix + version + ".marker");
        }

        public static string GetDataDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MacRando");
        }

        /// <summary>
        /// Called once startup has genuinely succeeded, so the helper can distinguish
        /// "the new build started" from "the new build crashed on launch".
        /// </summary>
        public static void MarkStartupSuccess(string version)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(version))
                {
                    return;
                }
                string directory = GetDataDirectory();
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    GetStartupMarkerPath(version.Trim()),
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
                ClearStaleMarkers(version.Trim());
            }
            catch (Exception error)
            {
                // Never let bookkeeping stop the application from starting.
                AppLogger.Warning("Could not write the update startup marker: " + error.Message);
            }
        }

        private static void ClearStaleMarkers(string keepVersion)
        {
            try
            {
                string directory = GetDataDirectory();
                foreach (string path in Directory.GetFiles(directory, StartupMarkerPrefix + "*.marker"))
                {
                    if (!string.Equals(Path.GetFileName(path), Path.GetFileName(GetStartupMarkerPath(keepVersion)), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(path);
                    }
                }
            }
            catch
            {
            }
        }

        public static bool CanInstall(
            UpdateCheckResult result,
            int pendingRestoreCount,
            bool busy,
            string currentVersion,
            out string reason)
        {
            if (result == null)
            {
                reason = "Run Check for updates first.";
                return false;
            }
            if (!result.IsVerified || string.IsNullOrWhiteSpace(result.LocalDownloadPath))
            {
                reason = "The update has not been downloaded and verified yet.";
                return false;
            }
            if (!File.Exists(result.LocalDownloadPath))
            {
                reason = "The verified download is no longer on disk. Run Check for updates again.";
                return false;
            }
            if (pendingRestoreCount > 0)
            {
                reason = "Restore the pending adapter profiles before installing an update. "
                    + "Installing closes MacRando, and the pending profile must be resolved first.";
                return false;
            }
            if (busy)
            {
                reason = "Wait for the current adapter, restore, or VPN operation to finish.";
                return false;
            }
            if (result.Manifest == null || string.IsNullOrWhiteSpace(result.Manifest.Version))
            {
                reason = "The update manifest did not report a version.";
                return false;
            }
            Version manifestVersion;
            Version installedVersion;
            if (!Version.TryParse(result.Manifest.Version, out manifestVersion) ||
                !Version.TryParse(currentVersion ?? "0", out installedVersion))
            {
                reason = "The update version could not be compared with the installed version.";
                return false;
            }
            if (manifestVersion <= installedVersion)
            {
                reason = "MacRando " + currentVersion + " is already at or above " + result.Manifest.Version + ".";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Starts the helper that performs the swap. Does not exit the process; the caller
        /// decides how to shut down so that a pending restore still runs first.
        /// </summary>
        public static void LaunchInstallAndRestart(string version, int watchdogSeconds, int exitWaitSeconds)
        {
            string target = Application.ExecutablePath;
            UpdateCheckResult result = PendingInstall;
            if (result == null || string.IsNullOrWhiteSpace(result.LocalDownloadPath))
            {
                throw new InvalidOperationException("There is no verified update waiting to be installed.");
            }

            string scriptPath = Path.Combine(
                Path.GetDirectoryName(result.LocalDownloadPath) ?? Path.GetTempPath(),
                "MacRando-install-" + Guid.NewGuid().ToString("N") + ".ps1");
            string markerPath = GetStartupMarkerPath(version);
            string backupPath = Path.Combine(GetDataDirectory(), "MacRando.previous.exe");
            string logPath = Path.Combine(GetDataDirectory(), "update-install.log");

            string script = BuildHelperScript();

            using (FileStream stream = new FileStream(scriptPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(script);
            }

            // The helper reads these from the environment, so no path is ever quoted into a
            // command line and the swap target cannot be tampered with in transit.
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.EnvironmentVariables["MACRANDO_TARGET"] = target;
            startInfo.EnvironmentVariables["MACRANDO_SOURCE"] = result.LocalDownloadPath;
            startInfo.EnvironmentVariables["MACRANDO_BACKUP"] = backupPath;
            startInfo.EnvironmentVariables["MACRANDO_MARKER"] = markerPath;
            startInfo.EnvironmentVariables["MACRANDO_PID"] = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            startInfo.EnvironmentVariables["MACRANDO_WATCHDOG_MS"] = (watchdogSeconds * 1000).ToString(CultureInfo.InvariantCulture);
            startInfo.EnvironmentVariables["MACRANDO_EXIT_WAIT_MS"] = (exitWaitSeconds * 1000).ToString(CultureInfo.InvariantCulture);
            startInfo.EnvironmentVariables["MACRANDO_LOG"] = logPath;

            // The helper inherits elevation from this process, so it can replace the
            // executable even when MacRando is installed under Program Files.
            Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("The update helper could not be started.");
            }

            AppLogger.Info("Update helper started (pid " + process.Id + ") for version " + version + ".");
            AppLogger.Info("  target : " + AppLogger.Sanitize(target));
            AppLogger.Info("  source : " + AppLogger.Sanitize(result.LocalDownloadPath));
            AppLogger.Info("  backup : " + AppLogger.Sanitize(backupPath));
            AppLogger.Info("  marker : " + AppLogger.Sanitize(markerPath));
            AppLogger.Info("  log    : " + AppLogger.Sanitize(logPath));
        }

        /// <summary>
        /// The verified download that LaunchInstallAndRestart will install. Kept in memory
        /// so the path does not have to round-trip through settings.
        /// </summary>
        public static UpdateCheckResult PendingInstall { get; set; }

        private static string BuildHelperScript()
        {
            // This is a C# verbatim string, so "$" needs no escaping. A PowerShell backtick
            // escape would be emitted literally here and produce an invalid script, so there
            // must not be a single backtick in the text below.
            return @"
$ErrorActionPreference = 'Stop'
$target     = $env:MACRANDO_TARGET
$source     = $env:MACRANDO_SOURCE
$backup     = $env:MACRANDO_BACKUP
$marker     = $env:MACRANDO_MARKER
$pidToWait  = [int]$env:MACRANDO_PID
$watchdogMs = [int]$env:MACRANDO_WATCHDOG_MS
$exitWaitMs = [int]$env:MACRANDO_EXIT_WAIT_MS
$log        = $env:MACRANDO_LOG
$self       = $MyInvocation.MyCommand.Path

function Write-Log([string]$message) {
    try { Add-Content -LiteralPath $log -Value ((Get-Date).ToString('u') + ' ' + $message) } catch { }
}

function Remove-Self {
    try { Remove-Item -LiteralPath $self -Force -ErrorAction SilentlyContinue } catch { }
}

try {
    Write-Log ('helper started for pid ' + $pidToWait)

    # 1. Wait for MacRando to exit. Bounded so an app that refuses to close does not
    #    leave the helper running forever.
    $deadline = (Get-Date).AddMilliseconds($exitWaitMs)
    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) {
        if ((Get-Date) -gt $deadline) {
            Write-Log 'timed out waiting for MacRando to exit; nothing was changed'
            Remove-Self
            exit 0
        }
        Start-Sleep -Milliseconds 200
    }
    Start-Sleep -Milliseconds 400

    # 2. Keep a copy so a build that fails to start can be rolled back.
    $backupDir = Split-Path -Parent $backup
    if (-not (Test-Path -LiteralPath $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }
    Copy-Item -LiteralPath $target -Destination $backup -Force
    Write-Log ('backed up the current build to ' + $backup)

    # 3. Swap in the verified download.
    Copy-Item -LiteralPath $source -Destination $target -Force
    Write-Log 'installed the new build'

    # 4. Launch it. The new build writes the marker once startup really succeeded.
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    $started = Start-Process -FilePath $target -PassThru
    Write-Log ('started the new build (pid ' + $started.Id + ')')

    $markerDeadline = (Get-Date).AddMilliseconds($watchdogMs)
    $ok = $false
    while ((Get-Date) -lt $markerDeadline) {
        if (Test-Path -LiteralPath $marker) { $ok = $true; break }
        Start-Sleep -Milliseconds 250
    }

    if (-not $ok) {
        Write-Log 'the new build did not report a successful start; rolling back'
        # A hung build still holds the executable open, and Windows will not let the file be
        # overwritten. Stop it first, otherwise the rollback silently fails and the user is
        # left with a broken installation.
        try {
            $lingering = Get-Process -Id $started.Id -ErrorAction SilentlyContinue
            if ($lingering) {
                Write-Log ('stopping the unresponsive new build (pid ' + $started.Id + ')')
                Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 600
            }
        } catch { Write-Log 'could not stop the unresponsive build' }

        Copy-Item -LiteralPath $backup -Destination $target -Force
        Write-Log 'rolled back to the previous build'
        Start-Process -FilePath $target | Out-Null
    }
    else {
        Write-Log 'the new build started successfully'
    }
}
catch {
    Write-Log ('helper failed: ' + $_.Exception.Message)
    # A failure part way through could leave a partial file, so restore if we made a backup.
    if (Test-Path -LiteralPath $backup) {
        try {
            $lingering = Get-Process -Name ([System.IO.Path]::GetFileNameWithoutExtension($target)) -ErrorAction SilentlyContinue
            if ($lingering) { $lingering | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400 }
            Copy-Item -LiteralPath $backup -Destination $target -Force
            Write-Log 'restored the previous build after a failure'
            Start-Process -FilePath $target | Out-Null
        } catch { Write-Log 'could not restore the previous build' }
    }
}
finally {
    Remove-Self
}
";
        }
    }
}
