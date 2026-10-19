using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MacRando;

/// <summary>
/// Coverage for restoring when the Windows session ends, and for Retry after a failed change.
///
/// The automatic restore only ran from the tray menu's Exit command, so shutting down,
/// restarting or signing out left every change in place across the reboot. A session end
/// cannot be produced in a test, so the parts are tested separately: which profiles are
/// restored, that the wait is bounded and keeps the message loop running, that the
/// dashboard survives the end-of-session message while a restore needs it, and that the
/// handlers are subscribed and released.
/// </summary>
internal static class SessionEndTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            OnlyThisSessionsChangesAreRestored();
            TheWaitKeepsTheMessageLoopRunning();
            TheWaitIsBounded();
            TheDashboardSurvivesTheSessionEndWhileARestoreNeedsIt();
            TheSessionEventsAreSubscribedAndReleased();
            RetryDoesNotUndoAnEarlierChange();
            Console.WriteLine("session-end-tests=OK;checks=" + _checks);
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

    private static string Source(string name)
    {
        string root = Environment.GetEnvironmentVariable("MACRANDO_REPO_ROOT") ?? ".";
        return File.ReadAllText(Path.Combine(Path.Combine(root, "src"), name));
    }

    private static void OnlyThisSessionsChangesAreRestored()
    {
        var backups = new Dictionary<string, AdapterBackup>(StringComparer.OrdinalIgnoreCase)
        {
            { "{mac}", new AdapterBackup { AdapterKey = "{mac}", MacChanged = true } },
            { "{ip}", new AdapterBackup { AdapterKey = "{ip}", IpChanged = true } },
            { "{earlier}", new AdapterBackup { AdapterKey = "{earlier}", MacChanged = true } },
            { "{empty}", new AdapterBackup { AdapterKey = "{empty}" } }
        };
        Func<string, AdapterBackup> find = key =>
        {
            AdapterBackup found;
            return backups.TryGetValue(key, out found) ? found : null;
        };

        List<AdapterBackup> selected = SessionEndRestore.Select(
            new[] { "{mac}", "{IP}", "{mac}", "{kept}", "{empty}", null, "" }, find);
        Check(selected.Count == 2, "exactly the two changed adapters with profiles should be restored, got " + selected.Count);
        Check(selected[0].AdapterKey == "{mac}" && selected[1].AdapterKey == "{ip}",
            "the profiles for this session's changes should be selected, once each");
        // A profile from an earlier session is restored from the pending banner, not
        // silently at shutdown, and a kept change has left the session set already.
        Check(SessionEndRestore.Select(new string[0], find).Count == 0, "nothing changed means nothing restored");
        Check(SessionEndRestore.Select(null, find).Count == 0, "no session set means nothing restored");
    }

    private static void TheWaitKeepsTheMessageLoopRunning()
    {
        // The restore continues through the message loop, so a wait that does not pump
        // would deadlock it. Here the task can only finish if the pump runs.
        var completion = new TaskCompletionSource<bool>();
        int pumped = 0;
        bool finished = SessionEndRestore.WaitPumping(completion.Task, TimeSpan.FromSeconds(10), () =>
        {
            pumped++;
            if (pumped == 5)
            {
                completion.SetResult(true);
            }
        });
        Check(finished, "the wait should finish once the task does");
        Check(pumped >= 5, "the wait must keep pumping while it waits");
        Check(SessionEndRestore.WaitPumping(null, TimeSpan.FromSeconds(1), null), "no restore means nothing to wait for");
    }

    private static void TheWaitIsBounded()
    {
        // A restore that never finishes must not hold up shutdown indefinitely.
        var never = new TaskCompletionSource<bool>();
        DateTime started = DateTime.UtcNow;
        bool finished = SessionEndRestore.WaitPumping(never.Task, TimeSpan.FromMilliseconds(300), () => { });
        double waited = (DateTime.UtcNow - started).TotalMilliseconds;
        Check(!finished, "an unfinished restore must be reported as unfinished");
        Check(waited >= 250 && waited < 5000, "the wait must stop at its limit, waited " + waited + "ms");
        Check(SessionEndRestore.MaximumWait <= TimeSpan.FromMinutes(2), "the shutdown wait must stay bounded");
    }

    private static void TheDashboardSurvivesTheSessionEndWhileARestoreNeedsIt()
    {
        // WinForms disposes a form on WM_ENDSESSION, and the restore reports through the
        // dashboard, so disposing it mid-restore made the restore fail before it reached
        // the adapter.
        Exception captured = null;
        var thread = new Thread(delegate()
        {
            try
            {
                MethodInfo wndProc = typeof(DashboardForm).GetMethod(
                    "WndProc", BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(Message).MakeByRefType() }, null);
                using (var form = new DashboardForm())
                {
                    form.CreateControl();
                    IntPtr handle = form.Handle;
                    form.HoldOpenAtSessionEnd = true;
                    object[] args = { Message.Create(handle, 0x0016, (IntPtr)1, IntPtr.Zero) };
                    wndProc.Invoke(form, args);
                    Check(!form.IsDisposed, "the dashboard must stay open through the session end while a restore needs it");
                }
                using (var form = new DashboardForm())
                {
                    form.CreateControl();
                    IntPtr handle = form.Handle;
                    form.HoldOpenAtSessionEnd = false;
                    object[] args = { Message.Create(handle, 0x0016, (IntPtr)1, IntPtr.Zero) };
                    wndProc.Invoke(form, args);
                    Check(form.IsDisposed, "without a restore the session end must be handled normally");
                }
            }
            catch (Exception error)
            {
                captured = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60));
        if (captured != null)
        {
            throw new Exception("The session-end form check failed.", captured);
        }
    }

    private static void TheSessionEventsAreSubscribedAndReleased()
    {
        string tray = Source("TrayContext.cs");
        Check(tray.Contains("SystemEvents.SessionEnding += OnSessionEnding;"), "the session ending must be handled");
        Check(tray.Contains("SystemEvents.SessionEnded += OnSessionEnded;"), "the session end must wait for the restore");
        Check(tray.Contains("SystemEvents.SessionEnding -= OnSessionEnding;") &&
              tray.Contains("SystemEvents.SessionEnded -= OnSessionEnded;"),
            "SystemEvents holds handlers statically, so they must be released on dispose");
        int ending = tray.IndexOf("private void OnSessionEnding(", StringComparison.Ordinal);
        int endingBody = tray.IndexOf("private void OnSessionEnded(", StringComparison.Ordinal);
        Check(ending >= 0 && endingBody > ending, "the session-ending handler should be found");
        Check(tray.Substring(ending, endingBody - ending).IndexOf("e.Cancel", StringComparison.Ordinal) < 0,
            "MacRando must never cancel a shutdown to restore; it restores while Windows waits");
    }

    private static void RetryDoesNotUndoAnEarlierChange()
    {
        // A change refused before it started shares the adapter's profile with an earlier,
        // verified change. Retry restored that profile instead of retrying, which undid
        // the earlier change. Restore is offered, and Retry restores first, only when the
        // failed change itself started.
        AdapterBackup earlier = new AdapterBackup { AdapterKey = "{a}", MacChanged = true };
        Check(!TrayContext.ShouldRollBackFailedChange(false, earlier),
            "a change that never started must not offer to restore an earlier change");
        string tray = Source("TrayContext.cs");
        Check(tray.Contains("bool offerRestore = ShouldRollBackFailedChange(changeStarted, failedBackup);"),
            "whether a failed change offers Restore must follow whether it started");
        Check(tray.Contains("RetryNetworkOperationAsync(adapter, changeMac, changeIp, requestedMac, generateRandomMac, automated, offerRestore)"),
            "Retry must restore first only when the failed change started");
        Check(tray.Contains("AdapterBackup backup = restoreFirst ? FindBackup("),
            "Retry must not look for a profile to restore when the change never started");
    }
}
