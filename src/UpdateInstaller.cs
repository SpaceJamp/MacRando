using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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

        /// <summary>
        /// The Inno Setup AppId, which is also the name of the uninstall registry key.
        ///
        /// The installer writes it and the updater has to write the same one, and nothing
        /// would report a mismatch: a wrong key simply does not exist, the write is skipped,
        /// and the installed program keeps showing the version the installer last wrote. A
        /// test compares this against installer.iss, because the two are edited separately
        /// and the failure is silent by nature.
        /// </summary>
        public const string InstallerAppId = "{B4D1E8B5-4D1C-4C77-9B27-2D22B6D6F1A0}";

        /// <summary>
        /// The uninstall key Inno Setup creates, which is the AppId with "_is1" appended.
        /// </summary>
        public const string UninstallKeyName = InstallerAppId + "_is1";

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

        /// <summary>
        /// Re-checks the downloaded file against the hash recorded when it was verified.
        ///
        /// Deliberately compares the hash only, not the signature. The Authenticode check
        /// is comparatively slow and needs the certificate chain, and the hash is the
        /// stronger of the two for this purpose: it is the thing that was verified against
        /// these exact bytes, and a swapped file will not match it. A file that has been
        /// replaced with something else signed by a different key would still be caught,
        /// because the manifest's hash describes the one file that was approved.
        /// </summary>
        public static bool StillMatchesVerifiedHash(UpdateCheckResult result)
        {
            if (result == null || result.Manifest == null ||
                string.IsNullOrWhiteSpace(result.Manifest.Sha256) ||
                string.IsNullOrWhiteSpace(result.LocalDownloadPath) ||
                !File.Exists(result.LocalDownloadPath))
            {
                return false;
            }
            try
            {
                string actual;
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = new FileStream(
                    result.LocalDownloadPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                }
                return string.Equals(actual, result.Manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error)
            {
                // A file that cannot be read is treated as not matching. Refusing to
                // install is the safe direction: the alternative is installing a file
                // nobody was able to check.
                AppLogger.Warning("Could not re-verify the downloaded update: " +
                    AppLogger.Sanitize(error.Message));
                return false;
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
            // The download sits in a per-user temp folder between verification and install,
            // which is a gap an unprivileged process running as this user can write to. The
            // hash is compared again here so the install decision is made against the bytes
            // on disk now, not against the bytes that were verified earlier. The helper
            // repeats this check immediately before the copy, because CanInstall returning
            // true is not the same moment as the file being installed.
            if (!StillMatchesVerifiedHash(result))
            {
                reason = "The downloaded file has changed since it was verified. " +
                    "This can happen if another program is modifying the temp folder. " +
                    "Run Check for updates again.";
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
            if (string.IsNullOrWhiteSpace(result.Manifest.Sha256))
            {
                reason = "The update manifest did not report a SHA-256 hash, so the download " +
                    "cannot be re-verified at install time.";
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
            // The hash the download was verified against, so the helper can re-check it at
            // the moment of the copy rather than trusting the main process's earlier check.
            startInfo.EnvironmentVariables["MACRANDO_SHA256"] = result.Manifest.Sha256;
            // The version to publish to Windows once the file is in place, and the uninstall
            // key to publish it under. Passed through the environment like everything else
            // this script reads, so no string is ever quoted into a command line.
            startInfo.EnvironmentVariables["MACRANDO_VERSION"] = version;
            startInfo.EnvironmentVariables["MACRANDO_UNINSTALL_KEY"] = UninstallKeyName;

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

# Publish the installed version where Windows shows it.
#
# 'Add or remove programs' reads DisplayVersion out of the uninstall registry key, and only
# the installer writes that key. The updater replaced the executable and nothing else, so
# after an in-app update the Settings list carried on showing whichever version the
# installer last wrote, for ever. It is a cosmetic string and the app was working, which is
# exactly why it went unnoticed.
#
# Deliberately forgiving. The key is only created when MacRando was installed with the
# installer, so a copy run from a ZIP has no entry and must not grow one, and a failure
# here is logged rather than raised, because refusing to finish an install over a label in
# a settings list would be a bad trade. Both the 64-bit and 32-bit views are tried because
# the key's location depends on how the installer was built.
function Set-InstalledVersion([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return }
    $keyName = $env:MACRANDO_UNINSTALL_KEY
    if ([string]::IsNullOrWhiteSpace($keyName)) {
        Write-Log 'no uninstall key name was supplied; leaving the installed version unchanged'
        return
    }
    $wrote = $false
    foreach ($hive in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
        $path = Join-Path $hive $keyName
        if (-not (Test-Path -LiteralPath $path)) { continue }
        try {
            # -ErrorAction Stop on the call itself rather than relying on the script-wide
            # ErrorActionPreference. Without it a denied write is a non-terminating error, the
            # catch below never runs, and the helper goes on to log that it published a version
            # it did not write. That is how this was found: an unelevated run reported success.
            New-ItemProperty -LiteralPath $path -Name 'DisplayVersion' -Value $value -PropertyType String -Force -ErrorAction Stop | Out-Null
            Write-Log ('published installed version ' + $value + ' to ' + $hive)
            $wrote = $true
        } catch {
            Write-Log ('could not publish the installed version to ' + $hive + ': ' + $_.Exception.Message)
        }
    }
    if (-not $wrote) {
        # Not an error. A portable copy has no uninstall entry, and creating one would put
        # an entry in Add or remove programs for a program that was never installed.
        Write-Log 'no uninstall entry found, so the installed version was left alone (portable copy)'
    }
}

# The version recorded in a built executable, as three parts rather than the four-part
# Windows file version, so it matches what the installer writes.
function Get-FileVersionString([string]$path) {
    try {
        $raw = (Get-Item -LiteralPath $path).VersionInfo.FileVersion
        if ([string]::IsNullOrWhiteSpace($raw)) { return '' }
        $parts = @($raw -split '[.]')
        if ($parts.Count -ge 3) { return ($parts[0] + '.' + $parts[1] + '.' + $parts[2]) }
        return $raw.Trim()
    } catch { return '' }
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

    # 3. Re-verify, then swap in the download.
    #
    # The download lives in a per-user temp folder and is checked in the main process, but
    # this script runs later, from an elevated helper. Anything running as the same user can
    # replace the file in between. The hash is recomputed here, as close to the copy as
    # possible, and the copy is refused if it does not match. Installing a file nobody was
    # able to check is the one outcome worth being strict about, so an unreadable file is
    # also treated as a failure rather than waved through.
    $expectedHash = $env:MACRANDO_SHA256
    if ([string]::IsNullOrWhiteSpace($expectedHash)) {
        Write-Log 'refusing to install: no expected hash was supplied'
        Remove-Self
        exit 2
    }
    if (-not (Test-Path -LiteralPath $source)) {
        Write-Log 'refusing to install: the download is no longer on disk'
        Remove-Self
        exit 2
    }
    $actualHash = $null
    try {
        $actualHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    } catch {
        Write-Log ('refusing to install: the download could not be read (' + $_.Exception.Message + ')')
        Remove-Self
        exit 2
    }
    if ($actualHash -ne $expectedHash.Trim().ToUpperInvariant()) {
        Write-Log 'refusing to install: the download no longer matches the verified hash'
        Remove-Self
        exit 2
    }
    Write-Log 're-verified the download immediately before installing'

    Copy-Item -LiteralPath $source -Destination $target -Force
    Write-Log 'installed the new build'

    # 3b. Tell Windows what version is now installed.
    #
    # After the copy, so the version published is the one actually on disk, and verified
    # against the file rather than taken on trust from the environment. If the two ever
    # disagreed, the honest thing to publish is what is in the file.
    $fileVersion = Get-FileVersionString $target
    $requested = $env:MACRANDO_VERSION
    if (-not [string]::IsNullOrWhiteSpace($fileVersion) -and $fileVersion -ne $requested) {
        Write-Log ('the installed file reports ' + $fileVersion + ' but ' + $requested + ' was expected; publishing the file version')
    }
    Set-InstalledVersion $fileVersion

    # 4. Launch it. The new build writes the marker once startup really succeeded.
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    $started = Start-Process -FilePath $target -PassThru
    Write-Log ('started the new build (pid ' + $started.Id + ')')

    $markerDeadline = (Get-Date).AddMilliseconds($watchdogMs)
    $markerSeen = $false
    while ((Get-Date) -lt $markerDeadline) {
        if (Test-Path -LiteralPath $marker) { $markerSeen = $true; break }
        # Stop waiting early only if the new build has already exited. A build that is still
        # running may simply be slow to start, and rolling that back would replace a
        # perfectly good build.
        if (-not (Get-Process -Id $started.Id -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
    }

    if ($markerSeen) {
        Write-Log 'the new build reported a successful start'
    }
    else {
        # No startup marker. Fall back to whether the process is actually alive: a slow start
        # is acceptable, a process that has already exited is not. A build that hangs without
        # ever starting is not distinguishable from a slow start here, so it is left in place
        # and the backup is kept for a manual recovery.
        $alive = $null -ne (Get-Process -Id $started.Id -ErrorAction SilentlyContinue)
        if ($alive) {
            Write-Log ('the new build is still running after ' + $watchdogMs + 'ms without writing its marker; treating a slow start as healthy')
        }
        else {
            Write-Log 'the new build exited without reporting a successful start; rolling back'
            try {
                $lingering = Get-Process -Name ([System.IO.Path]::GetFileNameWithoutExtension($target)) -ErrorAction SilentlyContinue
                if ($lingering) { $lingering | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 600 }
                Copy-Item -LiteralPath $backup -Destination $target -Force
                Write-Log 'rolled back to the previous build'
                # Put the published version back with the binary, or Add or remove programs
                # would advertise a build that is no longer installed.
                Set-InstalledVersion (Get-FileVersionString $target)
                Start-Process -FilePath $target | Out-Null
            }
            catch { Write-Log ('the rollback failed: ' + $_.Exception.Message) }
        }
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
            Set-InstalledVersion (Get-FileVersionString $target)
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
