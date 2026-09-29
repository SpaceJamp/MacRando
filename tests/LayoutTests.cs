using System;
using System.Collections.Generic;
using System.Drawing;
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
            TheAdapterListIsAlwaysUsable();
            TheListPaneIsNotClippedAtHighDpi();
            NothingOverlapsUnderHighDpiScaling();
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
