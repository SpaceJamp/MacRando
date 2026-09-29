using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MacRando;

/// <summary>
/// Contrast regression tests.
///
/// Every text control is checked against the background it actually renders on,
/// found by walking up the parent chain past transparent containers. The thresholds
/// are WCAG 2.1 AA: 4.5:1 for normal text and 3:1 for large text.
///
/// This exists because a real bug shipped white text on light system buttons in the
/// notification center, and a layout or overlap audit cannot see that.
/// </summary>
internal static class ContrastTests
{
    private const double NormalTextRatio = 4.5;
    private const double LargeTextRatio = 3.0;

    public static int Run()
    {
        int failures = 0;
        int checkedControls = 0;
        var problems = new List<string>();

        // WinForms requires a single-threaded apartment to create windows.
        Exception captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                checkedControls += AuditDashboard(problems);
                checkedControls += AuditNotificationCenter(problems);
                checkedControls += AuditPopup(problems);
            }
            catch (Exception error)
            {
                captured = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60));

        if (captured != null)
        {
            throw new Exception("Contrast tests failed to run.", captured);
        }
        failures = problems.Count;
        foreach (string problem in problems)
        {
            Console.Error.WriteLine(problem);
        }
        if (failures > 0)
        {
            throw new Exception("contrast-tests=FAILED issues=" + failures);
        }
        Console.WriteLine("contrast-tests=OK;controls=" + checkedControls);
        return 0;
    }

    private static int AuditDashboard(List<string> problems)
    {
        int count = 0;
        using (DashboardForm form = new DashboardForm())
        {
            form.Size = new Size(1120, 720);
            var connected = new AdapterInfo
            {
                Name = "Ethernet",
                Description = "Realtek 2.5GbE Family Controller",
                InterfaceIndex = 3,
                InterfaceGuid = "{121EC763-A5F6-4EF7-AD11-554AD8464295}",
                Status = "Up",
                IsUp = true,
                MacAddress = "02-00-00-00-00-01",
                PermanentMacAddress = "A4-1B-C0-11-22-33",
                MacPropertySupported = true,
                IpAddress = "192.168.1.42",
                PrefixLength = 24,
                DhcpEnabled = true,
                LinkSpeed = "1 Gbps"
            };
            var disconnected = new AdapterInfo
            {
                Name = "Wi-Fi",
                Description = "Wireless Adapter",
                InterfaceIndex = 7,
                InterfaceGuid = "{22222222-2222-2222-2222-222222222222}",
                Status = "Disconnected",
                IsUp = false,
                MacPropertySupported = false
            };
            form.SetAdapters(new[] { connected, disconnected });
            form.SetVpnProfiles(new VpnProfile[] { new VpnProfile { Name = "Home", ServerAddress = "vpn.example.com" } });
            form.SetPublicIp("203.0.113.9");
            form.SetBackupAvailable(true);
            // Exercise the surfaces added for recovery and adapter filtering.
            form.SetPendingRestoreCount(2);
            form.SetAdapterView(new List<string> { "{22222222-2222-2222-2222-222222222222}" }, false);

            CheckBox dark = (CheckBox)typeof(DashboardForm)
                .GetField("_darkModeCheckBox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(form);
            foreach (bool darkMode in new[] { false, true })
            {
                dark.Checked = darkMode;
                Application.DoEvents();
                count += Audit(form, darkMode ? "dashboard-dark" : "dashboard-light", problems);
            }
        }
        return count;
    }

    private static int AuditNotificationCenter(List<string> problems)
    {
        int count = 0;
        var settings = new AppSettings();
        var entries = new List<NotificationHistoryEntry>
        {
            new NotificationHistoryEntry
            {
                NotificationId = "contrast-1",
                TimestampUtc = DateTime.UtcNow,
                Title = "Restore needs attention",
                Message = "The saved configuration could not be restored.",
                Severity = NotificationKinds.Critical,
                AdapterKey = "{guid}",
                Action = "Retry",
                CanRetry = true
            }
        };
        foreach (bool darkMode in new[] { false, true })
        {
            using (var center = new NotificationCenterForm(settings, entries, darkMode))
            {
                center.CreateControl();
                center.Show();
                Application.DoEvents();
                count += Audit(center, darkMode ? "center-dark" : "center-light", problems);
            }
        }
        return count;
    }

    private static int AuditPopup(List<string> problems)
    {
        int count = 0;
        foreach (bool darkMode in new[] { false, true })
        {
            using (var popup = new NotificationPopup(
                null,
                "MacRando",
                "The selected adapter was updated successfully.",
                darkMode,
                NotificationKinds.Success,
                60000,
                false))
            {
                popup.Configure("contrast-popup", "{guid}", true, true, null, null, false, 60000);
                popup.CreateControl();
                popup.Show();
                Application.DoEvents();
                count += Audit(popup, darkMode ? "popup-dark" : "popup-light", problems);
            }
        }
        return count;
    }

    private static int Audit(Control root, string theme, List<string> problems)
    {
        int count = 0;
        Inspect(root, theme, problems, ref count);
        return count;
    }

    private static void Inspect(Control control, string theme, List<string> problems, ref int count)
    {
        TextBox asTextBox = control as TextBox;
        bool placeholderOnly = asTextBox != null && string.IsNullOrWhiteSpace(asTextBox.Text);
        bool carriesText = control is Label || control is CheckBox || control is Button ||
                           control is TextBox || control is ListView;

        if (carriesText && !placeholderOnly)
        {
            count++;
            string text = control.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                Color background = EffectiveBackground(control);
                double ratio = ContrastRatio(control.ForeColor, background);
                double required = IsLargeText(control) ? LargeTextRatio : NormalTextRatio;
                if (ratio < required)
                {
                    problems.Add(string.Format(
                        "{0} {1} '{2}' ratio={3:0.00} need={4:0.0} fg={5} bg={6}",
                        theme, control.GetType().Name, Shorten(text), ratio, required,
                        control.ForeColor.Name, background.Name));
                }
            }
        }

        foreach (Control child in control.Controls)
        {
            Inspect(child, theme, problems, ref count);
        }
    }

    private static string Shorten(string text)
    {
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length > 30 ? text.Substring(0, 30) + "..." : text;
    }

    private static bool IsLargeText(Control control)
    {
        try
        {
            if (control.Font == null)
            {
                return false;
            }
            if (control.Font.SizeInPoints >= 18F)
            {
                return true;
            }
            return control.Font.SizeInPoints >= 14F && control.Font.Bold;
        }
        catch
        {
            return false;
        }
    }

    private static Color EffectiveBackground(Control control)
    {
        Control current = control;
        while (current != null)
        {
            Color candidate = current.BackColor;
            if (candidate.A == 255)
            {
                return candidate;
            }
            current = current.Parent;
        }
        return Color.White;
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        double a = RelativeLuminance(foreground);
        double b = RelativeLuminance(background);
        if (a < b)
        {
            double swap = a;
            a = b;
            b = swap;
        }
        return (a + 0.05) / (b + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static double Channel(byte value)
    {
        double cs = value / 255.0;
        return cs <= 0.03928 ? cs / 12.92 : Math.Pow((cs + 0.055) / 1.055, 2.4);
    }
}
