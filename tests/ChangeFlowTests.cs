using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MacRando;

/// <summary>
/// Drives the real change and restore flow in TrayContext against a fake PowerShell runner.
///
/// The bugs fixed across 1.16.8 to 1.16.12 were all in this flow, and until now only its
/// rule functions were tested. Each case sets up a saved state, runs one tray action end
/// to end, and checks what was sent to Windows and what was left in the restore data.
/// </summary>
internal static class ChangeFlowTests
{
    private static int _checks;
    private const string Key = "{121EC763-A5F6-4EF7-AD11-554AD8464295}";
    private const string Permanent = "A4-1B-C0-11-22-33";
    private const string KeptRandom = "02-11-22-33-44-55";

    /// <summary>Remembers the MAC the app applied, so verification sees the change.</summary>
    private sealed class StatefulRunner : IPowerShellRunner
    {
        public string CurrentMac = KeptRandom;
        public string Override = "021122334455";
        public int PreferredAddressCount = 1;
        public int Restores;
        public int Applies;

        public Task<PowerShellResult> RunAsync(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            string value;
            if (environment != null && environment.TryGetValue("MR_CHANGE_MAC", out value) && value == "1")
            {
                Applies++;
                Override = environment["MR_NEW_MAC"];
                CurrentMac = NetworkService.FormatMac(Override);
            }
            if (environment != null && environment.TryGetValue("MR_RESTORE_MAC", out value))
            {
                Restores++;
            }
            return Task.FromResult(new PowerShellResult { ExitCode = 0, StandardOutput = string.Empty, StandardError = string.Empty });
        }

        public Task<T> RunJsonAsync<T>(string script, IDictionary<string, string> environment, int timeoutMilliseconds)
        {
            object value;
            if (typeof(T) == typeof(NetworkState))
            {
                value = new NetworkState
                {
                    AdapterName = "Ethernet",
                    InterfaceGuid = Key,
                    InterfaceIndex = 3,
                    CurrentMacAddress = CurrentMac,
                    MacOverrideValue = Override,
                    MacOverridePresent = true,
                    MacPropertySupported = true,
                    IpAddress = "192.168.1.20",
                    PrefixLength = 24,
                    Gateway = "192.168.1.1",
                    DhcpEnabled = false,
                    ConfigurationKnown = true,
                    DnsPolicyKnown = true,
                    IpAddresses = PreferredAddressCount == 1 ? new[] { "192.168.1.20" } : new string[0],
                    PreferredAddressCount = PreferredAddressCount
                };
            }
            else if (typeof(T) == typeof(List<AdapterInfo>))
            {
                value = new List<AdapterInfo> { Adapter(CurrentMac) };
            }
            else if (typeof(T) == typeof(List<VpnProfile>))
            {
                value = new List<VpnProfile>();
            }
            else
            {
                // The snapshot and network identity are not needed here; the app falls back.
                throw new PowerShellCommandException("not provided by this fake");
            }
            return Task.FromResult((T)value);
        }
    }

    public static int Run()
    {
        try
        {
            RestoreOriginalAfterAKeptMacIsNotUndoneAtExit();
            AChangeRefusedBeforeItStartsLeavesAnEarlierChangeAlone();
            Console.WriteLine("change-flow-tests=OK;checks=" + _checks);
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

    private static AdapterInfo Adapter(string mac)
    {
        return new AdapterInfo
        {
            Name = "Ethernet",
            Description = "Realtek PCIe GbE Family Controller",
            InterfaceGuid = Key,
            InterfaceIndex = 3,
            Status = "Up",
            IsUp = true,
            MacAddress = mac,
            PermanentMacAddress = Permanent,
            MacPropertySupported = true,
            IpAddress = "192.168.1.20",
            PrefixLength = 24,
            DhcpEnabled = false
        };
    }

    /// <summary>
    /// Runs one private TrayContext action to completion on an STA thread with a message
    /// loop, the way the app runs it.
    /// </summary>
    private static void RunFlow(AppState saved, StatefulRunner runner, Action<TrayContext, string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "MacRandoFlow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception captured = null;
        var thread = new Thread(delegate()
        {
            try
            {
                new StateStore(root).Save(saved);
                using (var context = new TrayContext(false, root, new NetworkService(runner), () => true, text => DialogResult.Yes))
                {
                    // The startup refresh would run alongside the action under test.
                    MethodInfo start = typeof(TrayContext).GetMethod("StartInitialRefresh", BindingFlags.Instance | BindingFlags.NonPublic);
                    Application.Idle -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), context, start);
                    body(context, root);
                }
            }
            catch (Exception error)
            {
                captured = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromMinutes(3));
        try { Directory.Delete(root, true); } catch { }
        if (captured != null)
        {
            throw new Exception("The change flow did not run.", captured);
        }
    }

    private static void Await(object task)
    {
        Check(SessionEndRestore.WaitPumping((Task)task, TimeSpan.FromSeconds(90), Application.DoEvents),
            "the tray action did not finish");
        if (((Task)task).IsFaulted)
        {
            throw ((Task)task).Exception;
        }
    }

    private static object Call(TrayContext context, string method, params object[] args)
    {
        foreach (MethodInfo candidate in typeof(TrayContext).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (candidate.Name == method && candidate.GetParameters().Length == args.Length)
            {
                return candidate.Invoke(context, args);
            }
        }
        throw new MissingMethodException(method);
    }

    private static HashSet<string> SessionKeys(TrayContext context)
    {
        return (HashSet<string>)typeof(TrayContext)
            .GetField("_changedAdapterKeysThisSession", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(context);
    }

    /// <summary>
    /// After a kept MAC, "Restore original" saved the kept random MAC as the thing to go
    /// back to, so Exit put the random MAC back.
    /// </summary>
    private static void RestoreOriginalAfterAKeptMacIsNotUndoneAtExit()
    {
        var saved = new AppState();
        saved.KeptMacs.Add(new KeptMacRecord { AdapterKey = Key, OriginalMacAddress = Permanent, KeptMacAddress = KeptRandom });
        var runner = new StatefulRunner();
        RunFlow(saved, runner, (context, root) =>
        {
            Await(Call(context, "RestorePermanentMacAsync", Adapter(KeptRandom)));
            Check(runner.Applies == 1 && runner.CurrentMac == Permanent, "the permanent MAC should have been applied");
            AppState after = new StateStore(root).Load();
            Check(after.Backups.Count == 0,
                "no restore profile may remain, or Exit puts the kept random MAC back");
            Check(KeepChangePolicy.Find(after, Key) == null, "the MAC is no longer kept, so the record must go");
            Check(!SessionKeys(context).Contains(Key), "Exit must have nothing to restore for this adapter");
        });
    }

    /// <summary>
    /// A change refused before anything was applied used to roll back the adapter's
    /// existing profile, undoing an earlier, verified change.
    /// </summary>
    private static void AChangeRefusedBeforeItStartsLeavesAnEarlierChangeAlone()
    {
        var saved = new AppState();
        saved.Backups[Key] = new AdapterBackup
        {
            AdapterKey = Key,
            AdapterName = "Ethernet",
            InterfaceGuid = Key,
            MacChanged = true,
            OriginalMacAddress = Permanent,
            ModifiedMacAddress = KeptRandom
        };
        var runner = new StatefulRunner { PreferredAddressCount = 0 };
        RunFlow(saved, runner, (context, root) =>
        {
            Await(Call(context, "ChangeNetworkAsync", Adapter(KeptRandom), false, true, null, true, true));
            Check(runner.Restores == 0, "a refused change must not restore the earlier change");
            Check(runner.Applies == 0, "a refused change must not apply anything");
            AppState after = new StateStore(root).Load();
            Check(after.Backups.ContainsKey(Key) && after.Backups[Key].MacChanged,
                "the earlier change's profile must be left as it was");
        });
    }
}
