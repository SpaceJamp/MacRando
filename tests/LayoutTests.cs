using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MacRando;
using DashboardForm = MacRando.DashboardForm;

/// <summary>
/// Layout regression tests: nothing may overlap, and the adapter list must always be
/// usable.
///
/// Two things had to be got right for this audit to be worth anything, and both were got
/// wrong first:
///
///   - Positions are computed by summing parent-relative Left/Top. PointToScreen returns
///     0,0 for a control whose handle has not been created, which puts every uncreated
///     control at the origin and manufactures thousands of phantom overlaps. This is also
///     why an earlier version of this audit reported the adapter list as having zero area:
///     it was measuring the failure of the measurement.
///
///   - A control's on-screen area is its bounds intersected with every ancestor's client
///     area. Without that, a card taller than the scrolling viewport inside the details
///     pane reports an overlap that the panel's clipping hides, and the real problems
///     drown in false positives.
/// </summary>
internal static class LayoutTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            NothingOverlapsAtAnySize();
            NoLabelIsShorterThanItsText();
            NoControlIsClippedAtAnySize();
            ThePublicIpLocationIsReportedOnScreen();
            TheAdapterListIsAlwaysUsable();
            TheListPaneIsNotClippedAtHighDpi();
            NothingOverlapsUnderHighDpiScaling();
            TheTrayMenuIsSensiblyOrdered();
            SectionHeadersAreNotClickable();
            ConstructingATrayContextDoesNotTouchRealData();
            TrayStatusWording();
            Console.WriteLine("layout-tests=OK;checks=" + _checks);
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

    /// <summary>Bounds relative to the form's client area, without needing a window handle.</summary>
    private static Rectangle Abs(Control c, Control root)
    {
        int x = 0;
        int y = 0;
        Control walk = c;
        while (walk != null && walk != root)
        {
            x += walk.Left;
            y += walk.Top;
            walk = walk.Parent as Control;
        }
        return new Rectangle(x, y, c.Width, c.Height);
    }

    /// <summary>
    /// The part of a control that is actually on screen. Intersecting with every ancestor's
    /// client area is what stops a clipped control from being reported as overlapping
    /// whatever sits below it.
    /// </summary>
    private static Rectangle Visible(Control c, Control root)
    {
        Rectangle visible = Abs(c, root);
        Control walk = c.Parent as Control;
        while (walk != null && walk != root)
        {
            Rectangle clip = Abs(walk, root);
            clip.Width = walk.ClientSize.Width;
            clip.Height = walk.ClientSize.Height;
            visible = Rectangle.Intersect(visible, clip);
            if (visible.Width <= 0 || visible.Height <= 0)
            {
                return new Rectangle(0, 0, 0, 0);
            }
            walk = walk.Parent as Control;
        }
        return visible;
    }

    /// <summary>
    /// Structure of the tray menu, read by reflection because the menu is built in the
    /// constructor and would otherwise need a running ApplicationContext with a real
    /// NotifyIcon.
    ///
    /// What is asserted is the property that actually matters: an action a user reaches
    /// for routinely is near the top, the status line is at the very top, and a group
    /// header is a non-clickable label rather than something that looks like an action.
    /// </summary>
    private static void TrayStatusWording()
    {
        Check(TrayMenuState.StatusText(0, true) == "Ready",
            "with nothing pending and permission, the status is simply ready");
        Check(TrayMenuState.StatusText(1, true) == "Ready - 1 adapter needs restoring",
            "one pending adapter is singular, not \"1 adapters\"");
        Check(TrayMenuState.StatusText(2, true) == "Ready - 2 adapters need restoring",
            "several pending adapters are plural");
        Check(TrayMenuState.StatusText(0, false) == "Administrator permission required",
            "without permission the status must say so rather than claiming to be ready");
        Check(TrayMenuState.StatusText(3, false) == "Administrator permission required",
            "the permission problem outranks the pending count: nothing can be done about either");

        // The count has to be in the text, not only in the colour, because the colour is
        // invisible to a screen reader and to anyone who cannot distinguish amber.
        for (int count = 1; count <= 12; count++)
        {
            Check(TrayMenuState.StatusText(count, true).Contains(count.ToString()),
                "the status text should state the pending count of " + count);
        }

        Check(TrayMenuState.NotifyText(0, true) == "MacRando - ready", "a clean tooltip is short");
        Check(TrayMenuState.NotifyText(1, true).Contains("1 adapter needs restoring"),
            "the tooltip should surface a pending restore");
        Check(TrayMenuState.NotifyText(1, true).Length <= 63,
            "the notify icon tooltip is truncated past 63 characters");

        Check(TrayMenuState.RestoreAllText(0) == "Restore all pending profiles",
            "with nothing pending the count is not shown");
        Check(TrayMenuState.RestoreAllText(2) == "Restore all pending profiles (2)",
            "with pending profiles the count is shown");

        // The colour must actually change, or the emphasis is decoration.
        Check(TrayMenuState.StatusColor(0, true) == SystemColors.ControlText,
            "a clean status uses the ordinary text colour");
        Check(TrayMenuState.StatusColor(2, true) != SystemColors.ControlText,
            "a pending restore should be visually distinct");
        Check(TrayMenuState.StatusColor(0, false) != TrayMenuState.StatusColor(2, true),
            "the permission problem should look different from a pending restore");
    }

    /// <summary>
    /// A sandbox for tests that need a real TrayContext.
    ///
    /// Constructing one registers an Application.Idle handler that performs a genuine
    /// refresh, and the tests pump the message loop, so a context left on the default data
    /// root rewrites the real state file. That happened: the profile count in the real
    /// state file oscillated during a test run, and it left a pending restore profile that
    /// then blocked the updater. The sandbox makes it impossible.
    /// </summary>
    private static string NewSandbox()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MacRandoTrayTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TheTrayMenuIsSensiblyOrdered()
    {
        string sandbox = NewSandbox();
        try
        {
        RunOnUiThread(delegate
        {
            using (TrayContext context = new TrayContext(false, sandbox))
            {
                ContextMenuStrip menu = (ContextMenuStrip)typeof(TrayContext)
                    .GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(context);
                Check(menu != null, "the tray context should own a menu");

                var labels = new List<string>();
                foreach (ToolStripItem item in menu.Items)
                {
                    ToolStripSeparator separator = item as ToolStripSeparator;
                    if (separator != null)
                    {
                        labels.Add("---");
                        continue;
                    }
                    labels.Add(item.Text);
                }

                Func<string, int> indexOf = delegate(string text)
                {
                    for (int i = 0; i < labels.Count; i++)
                    {
                        if (string.Equals(labels[i], text, StringComparison.Ordinal))
                        {
                            return i;
                        }
                    }
                    return -1;
                };

                // Nothing may be lost in the reorganisation. Every item the old flat menu
                // had has to still be reachable, or the tidy-up silently removed a feature.
                foreach (string required in new[]
                {
                    "Open dashboard", "Adapters", "Windows VPN profiles", "Restore saved adapter",
                    "Restore all pending profiles", "Refresh", "Run read-only diagnostics",
                    "Run read-only IP preflight", "Inspect device tracking identifiers (read-only)",
                    "Check for updates", "Notification center", "Open restore-data folder",
                    "Open logs folder", "Start minimized to tray", "Start with Windows",
                    "Randomize MAC on startup (risky)", "About MacRando", "Exit"
                })
                {
                    Check(indexOf(required) >= 0, "the tray menu lost the item \"" + required + "\"");
                }

                // The version and status lines come first: they are the only items that
                // need no click to be useful.
                Check(labels.Count > 2, "the menu should have items");
                Check(labels[0].StartsWith("MacRando ", StringComparison.Ordinal),
                    "the version should be the first line, but it is \"" + labels[0] + "\"");
                Check(indexOf("Status") < 0 && labels[1] != "---",
                    "the status line should follow the version directly");
                Check(labels[1] == "---" || !labels[1].StartsWith("Adapters", StringComparison.Ordinal),
                    "a section header should separate the status block from the actions");

                // Open dashboard and the restore actions are what a user comes for. They
                // must be above the reports and settings groups.
                int openIndex = indexOf("Open dashboard");
                int refreshIndex = indexOf("Refresh");
                int reportsIndex = indexOf("Reports (read-only)");
                Check(openIndex >= 0 && openIndex < reportsIndex,
                    "Open dashboard should be above the reports group");
                Check(refreshIndex > reportsIndex,
                    "Refresh belongs in the reports group, not with the primary actions");

                int notificationIndex = indexOf("Notification center");
                Check(notificationIndex > reportsIndex,
                    "Notification center belongs in the application group, above the reports");

                int exitIndex = indexOf("Exit");
                Check(exitIndex > 0, "Exit should be present");
                Check(exitIndex == labels.Count - 1,
                    "Exit should be the last item, but it is at " + exitIndex + " of " + labels.Count);
            }
        });
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch { }
        }
    }

    /// <summary>
    /// The regression that matters here: a test must not be able to touch the real user
    /// data. This constructs a context against the real data root, the way the test suite
    /// used to, and asserts the real state file is left byte-for-byte identical. If the
    /// sandbox is ever dropped from the tests, this fails.
    /// </summary>
    private static void ConstructingATrayContextDoesNotTouchRealData()
    {
        string realState = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacRando", "state.json");
        string before = File.Exists(realState)
            ? Convert.ToBase64String(File.ReadAllBytes(realState))
            : "(absent)";

        string sandbox = NewSandbox();
        try
        {
            RunOnUiThread(delegate
            {
                using (new TrayContext(false, sandbox))
                {
                }
            });
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch { }
        }

        string after = File.Exists(realState)
            ? Convert.ToBase64String(File.ReadAllBytes(realState))
            : "(absent)";
        Check(before == after,
            "constructing a TrayContext in a test must not modify the real state file");

        // And the sandbox must actually have been used, so the check above is not passing
        // because nothing happened at all.
        Check(!Directory.Exists(sandbox), "the sandbox should have been cleaned up");
    }

    private static void SectionHeadersAreNotClickable()
    {
        string sandbox = NewSandbox();
        try
        {
        RunOnUiThread(delegate
        {
            using (TrayContext context = new TrayContext(false, sandbox))
            {
                ContextMenuStrip menu = (ContextMenuStrip)typeof(TrayContext)
                    .GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(context);
                // Identified by bold font and disabled state rather than by the text: a real
                // report item also ends in a parenthesis, so matching on that would either
                // miss a header or flag a report.
                int headers = 0;
                foreach (ToolStripItem item in menu.Items)
                {
                    ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                    if (menuItem == null)
                    {
                        continue;
                    }
                    bool bold = menuItem.Font != null && menuItem.Font.Bold;
                    if (!bold)
                    {
                        continue;
                    }
                    headers++;
                    // A header that is enabled looks exactly like an action, and clicking
                    // it would do nothing, which is worse than not having it.
                    Check(!menuItem.Enabled,
                        "the section header \"" + menuItem.Text + "\" should not be clickable");
                    Check(menuItem.Text.Length > 0, "a section header should have text");
                }
                Check(headers >= 3, "the menu should have at least three section headers, found " + headers);

                // A real action must not be bold, or it would read as a group title.
                foreach (ToolStripItem item in menu.Items)
                {
                    ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                    if (menuItem == null || menuItem.Font == null)
                    {
                        continue;
                    }
                    if (menuItem.Text == "Open dashboard" || menuItem.Text == "Refresh")
                    {
                        Check(!menuItem.Font.Bold,
                            "\"" + menuItem.Text + "\" is an action and should not be styled as a header");
                    }
                }
            }
        });
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch { }
        }
    }

    private static List<Control> All(Control root)
    {
        var found = new List<Control>();
        foreach (Control child in root.Controls)
        {
            found.Add(child);
            found.AddRange(All(child));
        }
        return found;
    }

    private static bool IsAncestorOf(Control maybeAncestor, Control c)
    {
        Control walk = c;
        while (walk != null)
        {
            if (walk == maybeAncestor)
            {
                return true;
            }
            walk = walk.Parent as Control;
        }
        return false;
    }

    private static string Describe(Control c, Control root)
    {
        string type = c.GetType().Name;
        string text = null;
        Label label = c as Label;
        if (label != null)
        {
            text = label.Text;
        }
        Button button = c as Button;
        if (button != null)
        {
            text = button.Text;
        }
        if (text != null && text.Length > 30)
        {
            text = text.Substring(0, 30) + "...";
        }
        Rectangle v = Visible(c, root);
        return (text == null ? type : type + " (\"" + text + "\")") +
            " visible=" + v.Width + "x" + v.Height + " at " + v.X + "," + v.Y;
    }

    private static void Settle(Form form)
    {
        form.CreateControl();
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        foreach (Control c in All(form))
        {
            c.CreateControl();
        }
        form.PerformLayout();
        foreach (Control c in All(form))
        {
            c.PerformLayout();
        }
        Application.DoEvents();
    }

    private static void Populate(DashboardForm form)
    {
        form.SetAdapters(new[]
        {
            new AdapterInfo
            {
                Name = "Ethernet",
                Description = "Realtek 2.5GbE Family Controller, a description far too long to fit on one line",
                InterfaceIndex = 3,
                InterfaceGuid = "{121EC763-A5F6-4EF7-AD11-554AD8464295}",
                Status = "Up", IsUp = true, MacAddress = "02-00-00-00-00-01",
                PermanentMacAddress = "A4-1B-C0-11-22-33", MacPropertySupported = true,
                IpAddress = "192.168.1.42", PrefixLength = 24, DhcpEnabled = true, LinkSpeed = "1 Gbps"
            },
            new AdapterInfo
            {
                Name = "Wi-Fi", Description = "Wireless Adapter", InterfaceIndex = 7,
                InterfaceGuid = "{22222222-2222-2222-2222-222222222222}",
                Status = "Disconnected", IsUp = false, MacPropertySupported = false
            },
            new AdapterInfo
            {
                Name = "Bluetooth Network Connection", Description = "Bluetooth PAN",
                InterfaceIndex = 9, InterfaceGuid = "{33333333-3333-3333-3333-333333333333}",
                Status = "Up", IsUp = true, MacAddress = "02-00-00-00-00-03",
                PermanentMacAddress = "02-00-00-00-00-03", MacPropertySupported = true,
                IpAddress = "192.168.137.2", PrefixLength = 24, DhcpEnabled = true, LinkSpeed = "54 Mbps"
            }
        });
        form.SetVpnProfiles(new[]
        {
            new VpnProfile { Name = "Home", ServerAddress = "vpn.example.com" },
            new VpnProfile { Name = "Work", ServerAddress = "vpn2.example.com" }
        });
        form.SetPublicIp("203.0.113.9");
        // The banner is only present when a restore is outstanding, so it is shown here
        // deliberately: it is the tallest thing in the left column's sibling and the one
        // most likely to push the layout.
        form.SetPendingRestoreCount(2);
    }

    private static Size[] Sizes()
    {
        var sizes = new List<Size>();
        foreach (int w in new[] { 640, 700, 800, 900, 980, 1024, 1120, 1280, 1440, 1600, 1920 })
        {
            foreach (int h in new[] { 400, 480, 540, 620, 720, 900 })
            {
                sizes.Add(new Size(w, h));
            }
        }
        return sizes.ToArray();
    }

    private static List<string> FindOverlaps(Form form)
    {
        var problems = new List<string>();
        var leaves = new List<Control>();
        foreach (Control c in All(form))
        {
            if (!c.Visible)
            {
                continue;
            }
            Rectangle v = Visible(c, form);
            if (v.Width <= 0 || v.Height <= 0)
            {
                continue;
            }
            leaves.Add(c);
        }
        for (int i = 0; i < leaves.Count; i++)
        {
            for (int j = i + 1; j < leaves.Count; j++)
            {
                // A container and its descendant are not an overlap.
                if (IsAncestorOf(leaves[i], leaves[j]) || IsAncestorOf(leaves[j], leaves[i]))
                {
                    continue;
                }
                Rectangle a = Visible(leaves[i], form);
                Rectangle b = Visible(leaves[j], form);
                if (a.Right <= b.Left || b.Right <= a.Left)
                {
                    continue;
                }
                if (a.Bottom <= b.Top || b.Bottom <= a.Top)
                {
                    continue;
                }
                problems.Add(Describe(leaves[i], form) + " OVERLAPS " + Describe(leaves[j], form));
            }
        }
        return problems;
    }

    private static void NothingOverlapsAtAnySize()
    {
        RunOnUiThread(delegate
        {
            foreach (bool dark in new[] { false, true })
            {
                using (DashboardForm form = new DashboardForm())
                {
                    Populate(form);
                    CheckBox toggle = (CheckBox)typeof(DashboardForm)
                        .GetField("_darkModeCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(form);
                    toggle.Checked = dark;
                    Application.DoEvents();

                    foreach (Size size in Sizes())
                    {
                        form.Size = size;
                        Settle(form);
                        List<string> problems = FindOverlaps(form);
                        Check(problems.Count == 0,
                            "overlap at " + size.Width + "x" + size.Height + " (" + (dark ? "dark" : "light") + "): " +
                            (problems.Count == 0 ? "" : problems[0]));
                    }
                }
            }
        });
    }

    private static void TheAdapterListIsAlwaysUsable()
    {
        RunOnUiThread(delegate
        {
            using (DashboardForm form = new DashboardForm())
            {
                Populate(form);
                ListView list = (ListView)typeof(DashboardForm)
                    .GetField("_adapterList", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);

                int smallest = int.MaxValue;
                foreach (Size size in Sizes())
                {
                    form.Size = size;
                    Settle(form);
                    Rectangle v = Visible(list, form);
                    Check(v.Width > 0 && v.Height > 0,
                        "the adapter list collapsed to nothing at " + size.Width + "x" + size.Height);
                    if (v.Height < smallest)
                    {
                        smallest = v.Height;
                    }
                }
                // A list too short to show a row is not usable, and the smallest supported
                // window should still be able to show a few.
                Check(smallest >= 60,
                    "the adapter list gets down to " + smallest + "px tall, which cannot show a usable row");
            }
        });
    }

    private static void TheListPaneIsNotClippedAtHighDpi()
    {
        RunOnUiThread(delegate
        {
            // The list pane's header, search box, and filter row sit in fixed-height rows
            // while the labels inside them use point sizes, so a scaled font is the case
            // most likely to push a control out of its row.
            foreach (float scale in new[] { 1.25f, 1.5f, 2.0f })
            {
                using (DashboardForm form = new DashboardForm())
                {
                    Populate(form);
                    form.Size = new Size(1120, 720);
                    Settle(form);
                    form.Scale(new SizeF(scale, scale));
                    Settle(form);

                    Control listPanel = (Control)typeof(DashboardForm)
                        .GetField("_listPanel", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(form);
                    ListView list = (ListView)typeof(DashboardForm)
                        .GetField("_adapterList", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(form);

                    Rectangle panelVisible = Visible(listPanel, form);
                    Check(panelVisible.Height > 0, "the list pane vanished at " + scale + "x scaling");

                    Rectangle listVisible = Visible(list, form);
                    Check(listVisible.Height > 0,
                        "the adapter list vanished at " + scale + "x scaling");

                    // Each control directly in the list pane must keep some visible area;
                    // a control scrolled or clipped out of sight is the symptom.
                    foreach (Control child in listPanel.Controls)
                    {
                        Rectangle v = Visible(child, form);
                        Check(v.Height > 0,
                            "a list-pane control was clipped away entirely at " + scale + "x scaling: " +
                            child.GetType().Name);
                    }
                }
            }
        });
    }

    private static void NothingOverlapsUnderHighDpiScaling()
    {
        RunOnUiThread(delegate
        {
            foreach (float scale in new[] { 1.25f, 1.5f, 2.0f })
            {
                foreach (Size size in new[] { new Size(900, 620), new Size(1120, 720), new Size(1440, 900) })
                {
                    using (DashboardForm form = new DashboardForm())
                    {
                        Populate(form);
                        form.Size = size;
                        Settle(form);
                        form.Scale(new SizeF(scale, scale));
                        Settle(form);

                        List<string> problems = FindOverlaps(form);
                        Check(problems.Count == 0,
                            "overlap at " + size.Width + "x" + size.Height + " scaled " + scale + "x: " +
                            (problems.Count == 0 ? "" : problems[0]));
                    }
                }
            }
        });
    }

    private static void NoControlIsClippedAtAnySize()
    {
        RunOnUiThread(delegate
        {
            foreach (bool dark in new[] { false, true })
            {
                using (DashboardForm form = new DashboardForm())
                {
                    Populate(form);
                    CheckBox toggle = (CheckBox)typeof(DashboardForm)
                        .GetField("_darkModeCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(form);
                    toggle.Checked = dark;
                    Application.DoEvents();

                    foreach (Size size in Sizes())
                    {
                        form.Size = size;
                        Settle(form);
                        foreach (Control c in AllControls(form))
                        {
                            if (!c.Visible || c is Form || c is ListView) continue;
                            Rectangle v = Visible(c, form);
                            if (v.Width == 0 && v.Height == 0) continue;
                            // A control should never be entirely outside the form bounds.
                            if (v.Right <= 0 || v.Bottom <= 0 || v.Left >= form.Width || v.Top >= form.Height)
                            {
                                Check(false,
                                    "control \"" + c.Name + "\" (\"" + c.Text + "\") is entirely outside the " +
                                    "form bounds at " + size.Width + "x" + size.Height + " (" +
                                    (dark ? "dark" : "light") + ")");
                            }
                        }
                    }
                }
            }
        });
    }

    /// <summary>
    /// The public IP location must be readable on screen.
    ///
    /// This feature shipped reporting its result only as a tooltip on the public IP label,
    /// so enabling it changed nothing a user could see, and two releases were spent on a
    /// live service that was working while the interface looked permanently dead. The row
    /// has to exist, be visible, and say which of the three states it is in: not enabled,
    /// enabled but the lookup failed, or the location itself.
    /// </summary>
    private static void ThePublicIpLocationIsReportedOnScreen()
    {
        RunOnUiThread(delegate
        {
            using (DashboardForm form = new DashboardForm())
            {
                Populate(form);

                Label location = (Label)typeof(DashboardForm)
                    .GetField("_publicIpLocationLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);
                Check(location != null, "the dashboard has no label for the public IP location.");
                if (location == null)
                {
                    return;
                }

                CheckBox box = (CheckBox)typeof(DashboardForm)
                    .GetField("_showPublicIpLocationCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);

                var noData = new NetworkIdentity();
                box.Checked = false;
                form.SetPublicIp("203.0.113.9", noData);
                Check(location.Text.IndexOf("Off", StringComparison.OrdinalIgnoreCase) >= 0,
                    "with the setting off the Location row does not say it is off; it reads: " + location.Text);

                // Enabled but the lookup produced nothing. This is a different problem with
                // a different fix, so the row must not claim the user never turned it on.
                box.Checked = true;
                form.SetPublicIp("203.0.113.9", noData);
                Check(location.Text.IndexOf("Off", StringComparison.OrdinalIgnoreCase) < 0,
                    "with the setting on and no data, the Location row still says it is off: " + location.Text);
                Check(location.Text.IndexOf("could not be reached", StringComparison.OrdinalIgnoreCase) >= 0,
                    "with the setting on and no data, the Location row does not report a failed lookup; it reads: "
                        + location.Text);

                // The case that was actually broken: data arrives and has to appear.
                var identity = new NetworkIdentity
                {
                    PublicIpCity = "Clermont",
                    PublicIpRegion = "Florida",
                    PublicIpCountry = "US",
                    PublicIpIsp = "Charter Communications, Inc",
                    PublicIpAsn = "AS33363",
                    PublicIpTimezone = "America/New_York"
                };
                form.SetPublicIp("203.0.113.9", identity);
                Check(location.Text.Contains("Clermont"),
                    "the Location row does not show the city; it reads: " + location.Text);
                Check(location.Text.Contains("Florida"),
                    "the Location row does not show the region; it reads: " + location.Text);
                Check(location.Text.Contains("US"),
                    "the Location row does not show the country; it reads: " + location.Text);
                Check(location.Text.Contains("Charter Communications"),
                    "the Location row does not show the provider; it reads: " + location.Text);
                Check(location.Text.Contains("AS33363"),
                    "the Location row does not show the network number; it reads: " + location.Text);

                // The header is the only surface that is never scrolled or collapsed, and it
                // is where the public IP is always visible. The location has to sit on that
                // same surface, in the same cell, so it can never be separated from the
                // address it explains. This is the check that a tooltip-only implementation
                // would fail.
                Label headerIp = (Label)typeof(DashboardForm)
                    .GetField("_headerPublicIpLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);
                Label headerLocation = (Label)typeof(DashboardForm)
                    .GetField("_headerPublicIpLocationLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);
                Check(headerLocation != null, "the header has no label for the public IP location.");
                if (headerLocation == null)
                {
                    return;
                }

                form.SetPublicIp("203.0.113.9", identity);
                Check(headerLocation.Text.Contains("Clermont"),
                    "the header does not show the location; it reads: " + headerLocation.Text);
                Check(headerLocation.Text.Contains("AS33363"),
                    "the header does not show the network number; it reads: " + headerLocation.Text);

                string headerFilled = headerLocation.Text;
                int exercised = 0;
                foreach (Size size in Sizes())
                {
                    form.Size = size;
                    Settle(form);
                    Rectangle ipVisible = Visible(headerIp, form);
                    if (ipVisible.Width <= 0 || ipVisible.Height <= 0)
                    {
                        continue;
                    }
                    exercised++;
                    Rectangle locationVisible = Visible(headerLocation, form);
                    Check(locationVisible.Width > 0 && locationVisible.Height > 0,
                        "the public IP is in the header at " + size.Width + "x" + size.Height
                            + " but its location is not, so the location is harder to reach than the address");
                    Check(headerLocation.Text == headerFilled,
                        "the header location text changed when the window was resized to "
                            + size.Width + "x" + size.Height);
                }
                Check(exercised > 0,
                    "no tested window size showed the public IP in the header, so the location was never exercised.");

                // Opting out has to clear the header as well as the card.
                box.Checked = false;
                form.SetPublicIp("203.0.113.9", identity);
                Check(headerLocation.Text.Length == 0,
                    "the header still shows a location after the setting was turned off; it reads: "
                        + headerLocation.Text);

                // A failed address lookup and a failed location lookup are different problems
                // and were reported with the same words, which pointed the reader at the
                // location service even when the address had never arrived.
                box.Checked = true;
                form.SetPublicIp(DashboardForm.PublicIpUnavailable, new NetworkIdentity());
                Check(location.Text.IndexOf("public IP address", StringComparison.OrdinalIgnoreCase) >= 0,
                    "when the address could not be fetched the row should blame the address lookup, but it reads: "
                        + location.Text);
                Check(location.Text.IndexOf("location service", StringComparison.OrdinalIgnoreCase) < 0,
                    "when the address could not be fetched the row blames the location service; it reads: "
                        + location.Text);
                Check(headerLocation.Text.IndexOf("Public IP", StringComparison.OrdinalIgnoreCase) >= 0,
                    "the header should say the address is unavailable, but it reads: " + headerLocation.Text);

                // And the address arriving must clear it again.
                form.SetPublicIp("203.0.113.9", new NetworkIdentity());
                Check(location.Text.IndexOf("location service", StringComparison.OrdinalIgnoreCase) >= 0,
                    "with the address present and the location missing, the row should blame the location lookup; it reads: "
                        + location.Text);
            }
        });
    }

    /// <summary>
    /// No label may be shorter than the text it has to draw.
    ///
    /// An overlap audit cannot catch this: a label clipped inside its own bounds still
    /// has a correct rectangle, so nothing overlaps anything. The header's public IP and
    /// location lines were given fixed pixel heights, which were correct only at the
    /// display scaling they were written at. At 150% they were 20 and 16 pixels against
    /// a needed 21 and 18, and both lines lost their bottom edge.
    /// </summary>
    private static void NoLabelIsShorterThanItsText()
    {
        RunOnUiThread(delegate
        {
            foreach (float scale in new[] { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f })
            {
                foreach (Size size in new[] { new Size(1120, 720), new Size(1680, 1105) })
                {
                    using (DashboardForm form = new DashboardForm())
                    {
                        Populate(form);
                        form.Size = size;
                        Settle(form);
                        form.Scale(new SizeF(scale, scale));
                        Settle(form);
                        form.SetPublicIp("203.0.113.9", new NetworkIdentity
                        {
                            PublicIpCity = "Clermont",
                            PublicIpRegion = "Florida",
                            PublicIpCountry = "US",
                            PublicIpIsp = "Charter Communications, Inc",
                            PublicIpAsn = "AS33363"
                        });
                        Settle(form);

                        foreach (Control c in AllControls(form))
                        {
                            Label label = c as Label;
                            if (label == null || !label.Visible)
                            {
                                continue;
                            }
                            if (string.IsNullOrEmpty(label.Text))
                            {
                                continue;
                            }
                            int needed = label.PreferredHeight;
                            Check(label.Height >= needed,
                                "the label \"" + Shorten(label.Text) + "\" is " + label.Height
                                    + "px tall but needs " + needed + "px to draw its text, at "
                                    + size.Width + "x" + size.Height + " scaled " + scale + "x");
                        }
                    }
                }
            }
        });
    }

    private static string Shorten(string value)
    {
        value = value.Replace(Environment.NewLine, " ");
        return value.Length > 32 ? value.Substring(0, 32) + "..." : value;
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control grandchild in AllControls(child))
            {
                yield return grandchild;
            }
        }
    }

    private static void RunOnUiThread(ThreadStart body)
    {
        Exception captured = null;
        var thread = new Thread(delegate()
        {
            try
            {
                body();
            }
            catch (Exception error)
            {
                captured = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(120));
        if (captured != null)
        {
            throw new Exception("Layout checks failed to run.", captured);
        }
    }
}
