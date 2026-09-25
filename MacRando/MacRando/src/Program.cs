using System;
using System.Threading;
using System.Windows.Forms;

namespace MacRando
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] commandLineArgs)
        {
            bool globalOwned = false;
            bool localOwned = false;
            using (Mutex globalMutex = new Mutex(false, @"Global\MacRando.SingleInstance"))
            using (Mutex localMutex = new Mutex(false, @"Local\MacRando.SingleInstance"))
            {
                try
                {
                    globalOwned = TryAcquire(globalMutex);
                    localOwned = globalOwned && TryAcquire(localMutex);
                    if (!globalOwned || !localOwned)
                    {
                        MessageBox.Show(
                            "MacRando is already running. Check the notification area.",
                            "MacRando",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (sender, args) =>
                        AppLogger.Error("Unhandled UI exception.", args.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                        AppLogger.Error("Unhandled domain exception.", args.ExceptionObject as Exception);

                    using (TrayContext context = new TrayContext(IsStartupLaunch(commandLineArgs)))
                    {
                        Application.Run(context);
                    }
                }
                finally
                {
                    if (localOwned)
                    {
                        localMutex.ReleaseMutex();
                    }

                    if (globalOwned)
                    {
                        globalMutex.ReleaseMutex();
                    }
                }
            }
        }

        private static bool IsStartupLaunch(string[] args)
        {
            if (args == null)
            {
                return false;
            }
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryAcquire(Mutex mutex)
        {
            try
            {
                return mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
    }
}
