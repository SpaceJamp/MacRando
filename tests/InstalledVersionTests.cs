using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Coverage for the installed-version bookkeeping the update helper performs.
///
/// 'Add or remove programs' shows whatever DisplayVersion the uninstall registry key holds,
/// and only the installer writes that key. The updater replaced the executable and nothing
/// else, so after an in-app update the Settings list kept showing the version the installer
/// last wrote, permanently. It is a cosmetic string and the application was working, so
/// nothing reported it.
///
/// The AppId is the part most likely to break, because the two copies are edited in
/// different files and a mismatch is silent: the write targets a key that does not exist,
/// is skipped, and the version stays stale exactly as before.
/// </summary>
internal static class InstalledVersionTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            TheAppIdMatchesTheInstaller();
            TheUninstallKeyIsTheAppIdWithTheInnoSuffix();
            TheHelperPublishesTheInstalledVersion();
            TheHelperIsCarefulOnRollback();
            TheHelperDoesNotRefuseToFinishOverTheLabel();
            TheInstallerStillRequiresAdministrator();
            Console.WriteLine("installed-version-tests=OK;checks=" + _checks);
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

    private static string HelperScript()
    {
        System.Reflection.MethodInfo build = typeof(MacRando.UpdateInstaller).GetMethod(
            "BuildHelperScript",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check(build != null, "the update installer should build its helper script");
        return (string)build.Invoke(null, null);
    }

    private static void TheAppIdMatchesTheInstaller()
    {
        string installerPath = FindRepositoryFile("installer.iss");
        Check(installerPath != null,
            "installer.iss was not found. This check needs the repository root, so it is skipped " +
            "only when the suite is run from somewhere without the sources.");
        if (installerPath == null)
        {
            return;
        }

        string installer = File.ReadAllText(installerPath, Encoding.UTF8);

        // Inno Setup doubles the leading brace to escape it, so the file reads
        // AppId={{GUID} and the value is {GUID}.
        Match match = Regex.Match(installer, @"(?m)^AppId=\{\{(.+?)\}\s*$");
        Check(match.Success, "installer.iss has no AppId line, so the AppId cannot be compared.");
        string installerAppId = "{" + match.Groups[1].Value.Trim() + "}";

        Check(installerAppId == MacRando.UpdateInstaller.InstallerAppId,
            "The AppId in installer.iss is " + installerAppId + " but the updater writes to " +
            MacRando.UpdateInstaller.InstallerAppId + ". The installed version would never be " +
            "published, and nothing would report it, because writing to a key that does not exist " +
            "is skipped silently.");

        // A well-formed GUID, so a typo that still matches the installer is at least a valid one.
        Check(Regex.IsMatch(MacRando.UpdateInstaller.InstallerAppId,
                @"^\{[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}\}$"),
            "the AppId is not a well-formed GUID: " + MacRando.UpdateInstaller.InstallerAppId);
    }

    private static void TheUninstallKeyIsTheAppIdWithTheInnoSuffix()
    {
        // Inno Setup names the uninstall key after the AppId with _is1 appended. Getting
        // this wrong writes the version to a key nothing reads.
        Check(MacRando.UpdateInstaller.UninstallKeyName ==
                MacRando.UpdateInstaller.InstallerAppId + "_is1",
            "the uninstall key name should be the AppId with _is1 appended, but was " +
            MacRando.UpdateInstaller.UninstallKeyName);
    }

    private static void TheHelperPublishesTheInstalledVersion()
    {
        string script = HelperScript();
        Check(script.IndexOf("DisplayVersion", StringComparison.Ordinal) >= 0,
            "the update helper never touches DisplayVersion, so Add or remove programs will keep " +
            "showing the version the installer wrote");
        Check(script.IndexOf("MACRANDO_UNINSTALL_KEY", StringComparison.Ordinal) >= 0,
            "the helper does not read the uninstall key name from the environment");
        Check(script.IndexOf("New-ItemProperty", StringComparison.Ordinal) >= 0,
            "the helper does not appear to write the registry value");

        // Both registry views, because where the key lives depends on how the installer was
        // built, and this machine may be either.
        Check(script.IndexOf("WOW6432Node", StringComparison.Ordinal) >= 0,
            "the helper only tries the 64-bit uninstall hive. A 32-bit install would keep showing " +
            "a stale version forever.");

        // The version comes from the file, not from the environment, so a mismatch publishes
        // what is actually installed rather than what was intended.
        Check(script.IndexOf("Get-FileVersionString", StringComparison.Ordinal) >= 0,
            "the helper does not read the version back out of the installed file");
        Check(script.IndexOf("VersionInfo.FileVersion", StringComparison.Ordinal) >= 0,
            "the helper does not read the file version resource");
    }

    private static void TheHelperIsCarefulOnRollback()
    {
        string script = HelperScript();
        int rollbackRestores = Regex.Matches(
            script, @"Set-InstalledVersion \(Get-FileVersionString \$target\)").Count;
        Check(rollbackRestores >= 2,
            "both the startup-failure rollback and the general failure path must put the published " +
            "version back with the binary, otherwise Add or remove programs advertises a build that " +
            "is no longer installed. Found " + rollbackRestores + " of 2.");
    }

    private static void TheHelperDoesNotRefuseToFinishOverTheLabel()
    {
        string script = HelperScript();
        // The function must swallow its own errors: a cosmetic version string must never be the
        // reason an update does not finish.
        Match function = Regex.Match(
            script,
            @"(?s)function Set-InstalledVersion.*?\n\}",
            RegexOptions.None);
        Check(function.Success, "the helper has no Set-InstalledVersion function");
        string body = function.Value;
        Check(body.IndexOf("catch", StringComparison.Ordinal) >= 0,
            "Set-InstalledVersion does not catch anything, so a registry failure would abort the helper");
        Check(body.IndexOf("Test-Path", StringComparison.Ordinal) >= 0,
            "Set-InstalledVersion does not check that the uninstall key exists, so a portable copy " +
            "would grow an entry in Add or remove programs for a program that was never installed");
        Check(body.IndexOf("throw", StringComparison.Ordinal) < 0,
            "Set-InstalledVersion throws. A label in a settings list must never be able to fail an install.");

        // The catch above only runs for a terminating error, and a denied registry write is
        // non-terminating unless the call asks for it. Without -ErrorAction Stop the helper
        // logs that it published a version it did not write, which is worse than staying
        // quiet because it looks like the problem was handled.
        Check(body.IndexOf("-ErrorAction Stop", StringComparison.Ordinal) >= 0,
            "the registry write does not ask for a terminating error, so a denied write is " +
            "non-terminating, the catch never runs, and the helper reports success it did not have");
    }

    private static void TheInstallerStillRequiresAdministrator()
    {
        string installerPath = FindRepositoryFile("installer.iss");
        if (installerPath == null)
        {
            return;
        }
        string installer = File.ReadAllText(installerPath, Encoding.UTF8);

        // MacRando installs to Program Files and writes an HKLM uninstall key, neither of
        // which is possible without elevation. Dropping this directive would not fail the
        // build or any test; the install would just stop working.
        Check(Regex.IsMatch(installer, @"(?m)^PrivilegesRequired\s*=\s*admin\s*$"),
            "installer.iss no longer requires administrator. MacRando installs into Program Files " +
            "and writes an HKLM uninstall key, so an unelevated install cannot work.");

        // A command-line override would let a script or a shortcut silently drop the
        // requirement, which is the same failure by another route.
        Match overrides = Regex.Match(installer, @"(?m)^PrivilegesRequiredOverridesAllowed\s*=\s*(.*)$");
        Check(!overrides.Success || string.IsNullOrWhiteSpace(overrides.Groups[1].Value),
            "installer.iss allows PrivilegesRequired to be overridden from the command line, " +
            "which would let an install run unelevated and fail silently: " + overrides.Value);

        // The version is not hardcoded here; a copy that drifts is how this file sat at 1.4.4
        // while the application moved on through nine releases.
        Check(installer.IndexOf("#error AppVersion must be passed", StringComparison.Ordinal) >= 0,
            "installer.iss no longer refuses to compile without a version, so the version can " +
            "silently fall behind the application again");
    }

    /// <summary>
    /// Locates a repository file, using the root the suite hands over.
    ///
    /// The test binary is built into the temp directory, so walking up from its own location
    /// cannot reach the sources. Returns null when the root was not supplied, so the check
    /// degrades to being skipped rather than failing for the wrong reason.
    /// </summary>
    private static string FindRepositoryFile(string name)
    {
        string root = Environment.GetEnvironmentVariable("MACRANDO_REPO_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }
        string candidate = Path.Combine(root, name);
        return File.Exists(candidate) ? candidate : null;
    }
}
