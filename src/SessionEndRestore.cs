using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MacRando
{
    /// <summary>
    /// Restoring this session's changes when Windows shuts down, restarts or signs out.
    ///
    /// The automatic restore on exit only ran from the tray menu's Exit command, so the most
    /// common way the application ends, the session ending, left every change in place: a
    /// random static IP survived the reboot, which is the case the keep-change rules warn
    /// breaks connectivity. Windows ends a process soon after it returns from the end of
    /// the session, so the restore is started when the session begins to end and the final
    /// notification is held until it finishes, with a shutdown-block reason so Windows
    /// tells the user what it is waiting for. The wait is capped, so a stuck restore can
    /// never hold up shutdown indefinitely; anything not restored keeps its saved profile
    /// and is offered again at the next start.
    ///
    /// The pieces that make decisions are separated from the Windows calls so they can be
    /// tested without ending a session.
    /// </summary>
    internal static class SessionEndRestore
    {
        public const string BlockReason = "MacRando is restoring your network adapter settings.";

        /// <summary>Long enough for an adapter restart and verification, short enough to bound a hang.</summary>
        public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(90);

        /// <summary>
        /// The profiles to restore: those for adapters changed in this session that still
        /// have a saved profile. A kept MAC change has already left both sets, so it is not
        /// undone at shutdown any more than it is at Exit.
        /// </summary>
        public static List<AdapterBackup> Select(IEnumerable<string> changedKeys, Func<string, AdapterBackup> findBackup)
        {
            var selected = new List<AdapterBackup>();
            if (changedKeys == null || findBackup == null)
            {
                return selected;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in changedKeys)
            {
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                {
                    continue;
                }
                AdapterBackup backup = findBackup(key);
                if (backup != null && (backup.MacChanged || backup.IpChanged))
                {
                    selected.Add(backup);
                }
            }
            return selected;
        }

        /// <summary>
        /// Runs the message loop until the task finishes or the time runs out. The restore
        /// continues on the UI thread through async continuations, so blocking without
        /// pumping would deadlock it. Returns whether the task finished.
        /// </summary>
        public static bool WaitPumping(Task task, TimeSpan timeout, Action pump)
        {
            if (task == null)
            {
                return true;
            }
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!task.IsCompleted && elapsed.Elapsed < timeout)
            {
                if (pump != null)
                {
                    pump();
                }
                Thread.Sleep(25);
            }
            return task.IsCompleted;
        }

        public static void Block(IntPtr window)
        {
            try
            {
                if (window != IntPtr.Zero)
                {
                    ShutdownBlockReasonCreate(window, BlockReason);
                }
            }
            catch (Exception error)
            {
                // Without a reason Windows still waits, it just cannot say why.
                AppLogger.Warning("Could not register the shutdown block reason: " + error.Message);
            }
        }

        public static void Unblock(IntPtr window)
        {
            try
            {
                if (window != IntPtr.Zero)
                {
                    ShutdownBlockReasonDestroy(window);
                }
            }
            catch
            {
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string reason);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);
    }
}
