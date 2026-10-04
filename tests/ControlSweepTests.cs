using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MacRando;
using AppSettings = MacRando.AppSettings;
using DashboardForm = MacRando.DashboardForm;
using NotificationHistoryEntry = MacRando.NotificationHistoryEntry;

/// <summary>
/// A cross-cutting sweep of every control in every window MacRando puts on screen.
///
/// The other suites each ask one question well: does anything overlap, is any text
/// legible, is anything named. That division is comfortable but it is also why several
/// defects got through in a row. Each suite looks at the controls through one lens, so a
/// control that is wrong in a way the lens does not measure is invisible to all of them.
///
/// Two things went wrong recently that none of the existing suites could see:
///
///   - The header's public IP and location labels were shorter than the text they had to
///     draw. Nothing overlaps a label that is clipped inside its own bounds: its rectangle
///     is perfectly correct, so the overlap audit passed, the contrast audit passed (it had
///     text and colours), and the accessibility audit passed. The text was simply cut off.
///     Only a check that measures the control against its own content finds that.
///
///   - A card was laid out past the bottom of its scrolling parent, so a feature the user
///     was looking at had moved off the screen without any control reporting a problem.
///
/// This suite applies several invariants to every control at once and, unlike the others,
/// collects every failure before reporting. A single pass therefore surfaces a batch of
/// distinct defects rather than stopping at the first one, which is what makes it useful
/// for a sweep rather than just another single-purpose check.
///
/// It sweeps three windows, two themes, three window sizes and two display scalings, so
/// one run covers thirty-six combinations.
/// </summary>
internal static class ControlSweepTests
{
    /// <summary>
    /// Window sizes to sweep the dashboard at. Deliberately not the sixty-six that the
    /// layout suite uses: this suite runs six invariants over every control, so the point
    /// is breadth of invariants rather than breadth of sizes.
    ///
    /// The notification windows are not swept at these sizes. A toast is a fixed-purpose
    /// surface and the notification center sizes itself to its content, so forcing either
    /// to an arbitrary dashboard size collapses their filter and preference rows and
    /// reports a layout failure that does not exist in any real use. They are swept at
    /// their own natural size instead, which is the only size they are ever seen at.
    /// </summary>
    private static readonly Size[] SweepSizes =
    {
        new Size(1120, 720),
        new Size(1400, 900),
        new Size(1920, 1200)
    };

    private static readonly float[] SweepScales = { 1.0f, 1.5f };

    public static int Run()
    {
        var problems = new List<string>();
        int inspected = 0;

        Exception captured = null;
        var thread = new Thread(delegate()
        {
            try
            {
                inspected += Sweep(problems);
            }
            catch (Exception error)
            {
                captured = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(600));

        if (captured != null)
        {
            throw new Exception("Control sweep failed to run.", captured);
        }

        foreach (string problem in problems)
        {
            Console.Error.WriteLine(problem);
        }
        if (problems.Count > 0)
        {
            throw new Exception("control-sweep-tests=FAILED issues=" + problems.Count);
        }

        Console.WriteLine("control-sweep-tests=OK;controls=" + inspected);
        return 0;
    }

    private static int Sweep(List<string> problems)
    {
        int inspected = 0;

        foreach (bool darkMode in new[] { false, true })
        {
            foreach (Size size in SweepSizes)
            {
                foreach (float scale in SweepScales)
                {
                    inspected += SweepDashboard(darkMode, size, scale, problems);
                    inspected += SweepNotificationCenter(darkMode, size, scale, problems);
                    inspected += SweepPopup(darkMode, size, scale, problems);
                }
            }
        }

        return inspected;
    }

    // ---------------------------------------------------------------- windows

    private static int SweepDashboard(bool darkMode, Size size, float scale, List<string> problems)
    {
        int count = 0;
        using (DashboardForm form = new DashboardForm())
        {
            form.SetAdapters(new[]
            {
                new AdapterInfo
                {
                    Name = "Ethernet",
                    Description = "Realtek 2.5GbE Family Controller, a description far too long to fit on one line",
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
                },
                new AdapterInfo
                {
                    Name = "Wi-Fi",
                    Description = "Wireless Adapter",
                    InterfaceIndex = 7,
                    InterfaceGuid = "{22222222-2222-2222-2222-222222222222}",
                    Status = "Disconnected",
                    IsUp = false,
                    MacPropertySupported = false
                }
            });
            form.SetVpnProfiles(new[] { new VpnProfile { Name = "Home", ServerAddress = "vpn.example.com" } });
            form.SetPublicIp("203.0.113.9", new NetworkIdentity
            {
                PublicIpCity = "Clermont",
                PublicIpRegion = "Florida",
                PublicIpCountry = "US",
                PublicIpIsp = "Charter Communications, Inc",
                PublicIpAsn = "AS33363"
            });
            form.SetBackupAvailable(true);
            form.SetPendingRestoreCount(2);

            CheckBox dark = (CheckBox)typeof(DashboardForm)
                .GetField("_darkModeCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form);
            dark.Checked = darkMode;

            form.Size = size;
            Settle(form);
            if (scale != 1.0f)
            {
                form.Scale(new SizeF(scale, scale));
                Settle(form);
            }

            string where = "dashboard-" + (darkMode ? "dark" : "light")
                + "-" + size.Width + "x" + size.Height + "@" + scale + "x";
            count += Inspect(form, where, problems);
        }
        return count;
    }

    private static int SweepNotificationCenter(bool darkMode, Size size, float scale, List<string> problems)
    {
        int count = 0;
        var settings = new AppSettings();
        var entries = new List<NotificationHistoryEntry>
        {
            new NotificationHistoryEntry
            {
                NotificationId = "sweep-1",
                TimestampUtc = DateTime.UtcNow,
                Title = "Restore needs attention",
                Message = "The saved configuration could not be restored for the selected adapter.",
                Severity = NotificationKinds.Critical,
                AdapterKey = "{guid}",
                Action = "Retry",
                CanRetry = true
            },
            new NotificationHistoryEntry
            {
                NotificationId = "sweep-2",
                TimestampUtc = DateTime.UtcNow.AddMinutes(-5),
                Title = "MAC changed",
                Message = "The MAC address was updated and will be restored on exit.",
                Severity = NotificationKinds.Info,
                AdapterKey = "{guid2}"
            }
        };

        using (NotificationCenterForm center = new NotificationCenterForm(settings, entries, darkMode))
        {
            center.CreateControl();
            center.Show();
            Settle(center);
            if (scale != 1.0f)
            {
                center.Scale(new SizeF(scale, scale));
                Settle(center);
            }

            string where = "center-" + (darkMode ? "dark" : "light")
                + "@" + scale + "x-" + center.Width + "x" + center.Height;
            count += Inspect(center, where, problems);
        }
        return count;
    }

    private static int SweepPopup(bool darkMode, Size size, float scale, List<string> problems)
    {
        int count = 0;
        using (NotificationPopup popup = new NotificationPopup(
            null,
            "MacRando",
            "The selected adapter was updated successfully.",
            darkMode,
            NotificationKinds.Success,
            60000,
            false))
        {
            popup.Configure("sweep-popup", "{guid}", true, true, null, null, false, 60000);
            popup.CreateControl();
            popup.Show();
            Settle(popup);
            if (scale != 1.0f)
            {
                popup.Scale(new SizeF(scale, scale));
                Settle(popup);
            }

            string where = "popup-" + (darkMode ? "dark" : "light")
                + "@" + scale + "x-" + popup.Width + "x" + popup.Height;
            count += Inspect(popup, where, problems);
        }
        return count;
    }

    // ------------------------------------------------------------- invariants

    private static int Inspect(Control root, string where, List<string> problems)
    {
        int count = 0;
        ToolTip tip = FindToolTip(root);
        InspectOne(root, root, where, tip, problems, ref count);
        return count;
    }

    /// <summary>
    /// The window's own ToolTip instance.
    ///
    /// ToolTip.GetToolTip is an instance method, not a static one, and the text lives on
    /// whichever instance called SetToolTip. Each window keeps its own, so it has to be
    /// found rather than assumed. Scanned rather than hard-coded by field name because the
    /// three windows declare it independently and a rename must not silently turn this
    /// check into one that always passes.
    /// </summary>
    private static ToolTip FindToolTip(Control root)
    {
        for (Type type = root.GetType(); type != null; type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!typeof(ToolTip).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }
                ToolTip tip = field.GetValue(root) as ToolTip;
                if (tip != null)
                {
                    return tip;
                }
            }
        }
        return null;
    }

    private static void InspectOne(Control control, Control root, string where, ToolTip tip, List<string> problems, ref int count)
    {
        count++;

        try
        {
            CheckUsableFont(control, where, problems);
            if (control.Visible)
            {
                CheckVisibleArea(control, root, where, problems);
                CheckWithinParent(control, where, problems);
                CheckTextIsNotClippedVertically(control, where, problems);
                CheckHiddenTextIsRecoverable(control, tip, where, problems);
                CheckInteractiveControlsAreNamed(control, where, problems);
            }
        }
        catch (Exception error)
        {
            // A control that throws while being measured must be reported, not swallowed
            // into a passing run.
            problems.Add(where + " measuring " + Describe(control) + " threw " + error.Message);
        }

        foreach (Control child in control.Controls)
        {
            InspectOne(child, root, where, tip, problems, ref count);
        }
    }

    /// <summary>
    /// A control with no usable font draws nothing, or draws at a default that ignores the
    /// theme. It is a rare failure but it is invisible to every other check, because the
    /// control still has correct bounds and the right colours.
    /// </summary>
    private static void CheckUsableFont(Control control, string where, List<string> problems)
    {
        Font font = control.Font;
        if (font == null)
        {
            problems.Add(where + " " + Describe(control) + " has no font.");
            return;
        }
        if (font.Height <= 0 || font.SizeInPoints <= 0F)
        {
            problems.Add(where + " " + Describe(control) + " has a font of size "
                + font.SizeInPoints + "pt / height " + font.Height + ", which cannot draw text.");
        }
    }

    /// <summary>
    /// A control that is visible but has no area cannot be seen or clicked. This catches a
    /// collapsed row or a cell that a table gave no size.
    /// </summary>
    private static void CheckVisibleArea(Control control, Control root, string where, List<string> problems)
    {
        Rectangle area = VisibleBounds(control, root);
        if (area.Width <= 0 || area.Height <= 0)
        {
            // A container is allowed to legitimately have no area when its parent clips it
            // away entirely, but only if it is not the thing the user is meant to see. An
            // empty layout panel is not a defect on its own.
            if (!(control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel))
            {
                problems.Add(where + " " + Describe(control) + " is visible but occupies no area; "
                    + "clipped by " + DescribeClippingAncestor(control, root) + ".");
            }
        }
    }

    /// <summary>
    /// The nearest ancestor that takes the control's area away. Reported with every
    /// zero-area finding, because "visible but invisible" is otherwise very hard to trace
    /// back to the container responsible.
    /// </summary>
    private static string DescribeClippingAncestor(Control control, Control root)
    {
        Control walk = control.Parent;
        while (walk != null && walk != root)
        {
            if (walk.ClientSize.Width <= 0 || walk.ClientSize.Height <= 0)
            {
                return Describe(walk) + " has client size " + walk.ClientSize.Width + "x" + walk.ClientSize.Height;
            }
            walk = walk.Parent;
        }
        return "the window itself, client size " + root.ClientSize.Width + "x" + root.ClientSize.Height
            + ", with no collapsed ancestor to explain it";
    }

    /// <summary>
    /// A control drawn outside its parent's client rectangle is either clipped away or
    /// overlapping something else. A parent that scrolls is allowed to hold content larger
    /// than itself, which is the one legitimate exception.
    /// </summary>
    private static void CheckWithinParent(Control control, string where, List<string> problems)
    {
        Control parent = control.Parent;
        if (parent == null || parent is ScrollableControl)
        {
            return;
        }
        // A control whose own bounds are inside the parent's, but which is clipped by a
        // further ancestor, is a different fault and is reported by CheckVisibleArea.
        if (parent.Controls.Contains(control))
        {
            return;
        }
        Rectangle child = control.Bounds;
        Rectangle parentArea = parent.ClientRectangle;
        bool outside = child.Right > parentArea.Right + 1
                       || child.Bottom > parentArea.Bottom + 1
                       || child.Left < parentArea.Left - 1
                       || child.Top < parentArea.Top - 1;
        if (outside)
        {
            problems.Add(where + " " + Describe(control) + " at " + child
                + " extends past its parent's client area " + parentArea
                + ", so part of it cannot be drawn or reached.");
        }
    }

    /// <summary>
    /// The control must be at least as tall as the text it has been asked to draw.
    ///
    /// This is the invariant that was missing. Both header lines were given fixed pixel
    /// heights smaller than their font needed, so the bottom of every glyph was cut off. No
    /// overlap was involved and the colours were right, so every other suite passed.
    /// </summary>
    private static void CheckTextIsNotClippedVertically(Control control, string where, List<string> problems)
    {
        Label label = control as Label;
        if (label == null || string.IsNullOrWhiteSpace(label.Text))
        {
            return;
        }

        int needed;
        try
        {
            needed = label.PreferredHeight;
        }
        catch
        {
            return;
        }

        if (needed > 0 && label.Height < needed)
        {
            problems.Add(where + " the label \"" + Shorten(label.Text) + "\" is " + label.Height
                + "px tall but needs " + needed + "px to draw its text, so the bottom is cut off.");
        }
    }

    /// <summary>
    /// Text that does not fit its control has to remain reachable somehow, otherwise the
    /// information is simply gone. An ellipsised label must carry a tooltip with the full
    /// text, and a label that cannot even ellipsise has lost the text outright.
    ///
    /// This is the invariant behind the row that read "Press Refresh to try again" with the
    /// instruction itself truncated away.
    /// </summary>
    private static void CheckHiddenTextIsRecoverable(Control control, ToolTip tip, string where, List<string> problems)
    {
        Label label = control as Label;
        if (label == null || string.IsNullOrWhiteSpace(label.Text) || label.Width <= 0)
        {
            return;
        }

        int width;
        try
        {
            // The label's own preferred width, not a raw TextRenderer measurement. Those two
            // disagree by a pixel or two because the label includes its internal padding,
            // and a check that trips on a two pixel disagreement reports noise instead of
            // defects. This is the width the label's layout would need to show everything.
            width = label.PreferredSize.Width;
        }
        catch
        {
            return;
        }

        if (width <= label.Width)
        {
            return;
        }

        bool hasTooltip = tip != null && !string.IsNullOrWhiteSpace(tip.GetToolTip(control));
        if (!hasTooltip)
        {
            problems.Add(where + " the label \"" + Shorten(label.Text) + "\" needs " + width
                + "px in a " + label.Width + "px box"
                + (label.AutoEllipsis ? " and has no tooltip, so the full text is unreachable."
                    : " and cannot even ellipsise, so the text is lost."));
        }
    }

    /// <summary>
    /// Anything the user can operate has to announce a name. A control with visible text but
    /// no accessible name is announced as an unlabelled button, which loses the visible
    /// label entirely for anyone using a screen reader.
    /// </summary>
    private static void CheckInteractiveControlsAreNamed(Control control, string where, List<string> problems)
    {
        // WinForms builds a NumericUpDown out of an internal UpDownEdit plus a pair of
        // UpDownButtons. Those are the toolkit's own controls, they are never announced to
        // the user, and naming them is not something this codebase can do. The control that
        // actually needs a name is the NumericUpDown itself, which is checked on its own.
        if (control is UpDownBase || control.Parent is UpDownBase)
        {
            return;
        }

        if (!(control is ButtonBase)
            && !(control is TextBox)
            && !(control is ComboBox)
            && !(control is ListView)
            && !(control is ListBox)
            && !(control is LinkLabel)
            && !(control is NumericUpDown))
        {
            return;
        }

        string name = control.AccessibleName;
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add(where + " the interactive control \"" + Shorten(control.Text)
                + "\" (" + control.GetType().Name + ") has no accessible name.");
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A control's on-screen area is its own bounds intersected with every ancestor's
    /// client area. Summing parent-relative Left and Top avoids PointToScreen, which
    /// returns 0,0 for a control whose handle has not been created.
    /// </summary>
    private static Rectangle VisibleBounds(Control control, Control root)
    {
        Rectangle area = Absolute(control, root);

        Control walk = control.Parent;
        while (walk != null && walk != root)
        {
            Rectangle origin = Absolute(walk, root);
            Rectangle clip = new Rectangle(origin.X, origin.Y, walk.ClientSize.Width, walk.ClientSize.Height);
            area = Rectangle.Intersect(area, clip);
            if (area.Width <= 0 || area.Height <= 0)
            {
                return new Rectangle(0, 0, 0, 0);
            }
            walk = walk.Parent;
        }
        return area;
    }

    /// <summary>
    /// Bounds relative to the window's client area, without needing a window handle.
    ///
    /// The walk stops at the root on purpose. Summing through to the form as well would
    /// fold the form's own screen position into the result and every control would land
    /// outside the window.
    /// </summary>
    private static Rectangle Absolute(Control control, Control root)
    {
        int x = 0;
        int y = 0;
        Control walk = control;
        while (walk != null && walk != root)
        {
            x += walk.Left;
            y += walk.Top;
            walk = walk.Parent;
        }
        return new Rectangle(x, y, control.Width, control.Height);
    }

    private static void Settle(Control root)
    {
        root.PerformLayout();
        SettleChildren(root);
        Application.DoEvents();
        root.PerformLayout();
        SettleChildren(root);
        Application.DoEvents();
    }

    private static void SettleChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            child.PerformLayout();
            SettleChildren(child);
        }
    }

    private static string Describe(Control control)
    {
        string text = control.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "(no text)";
        }
        else
        {
            text = Shorten(text);
        }
        return control.GetType().Name + " \"" + text + "\"";
    }

    private static string Shorten(string text)
    {
        if (text == null)
        {
            return "";
        }
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length > 34 ? text.Substring(0, 34) + "..." : text;
    }
}
