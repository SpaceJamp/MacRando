using System;
using System.Collections.Generic;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using MacRando;

/// <summary>Fluent palette tokens, readability, high-contrast derivation and the dark default.</summary>
internal static class FluentThemeTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            TokensMatchTheSpec();
            TextIsReadable();
            DarkInputsAreLighterThanCards();
            HighContrastIsSystemDerived();
            AccessibilityAndTrayFollowFluent();
            DarkIsTheDefaultTheme();
            HighContrastComesFromAccessibility();
            PivotShowsOnlyTheSelectedPage();
            PivotIsAccessible();
            PivotKeyboard();
            ChromeIsSafeWithoutHandle();
            CardPaintSurvivesZeroSize();
            DashboardActionsStillRaiseTheirEvents();
            CenterActionsStillRaiseTheirEvents();
            PivotTabsUseTheirWindowsFont();
            SelectedAdapterRowUsesHighlightInHighContrast();
            PopupFirstActionIsPrimary();
            PopupSeverityIsNamed();
            Console.WriteLine("fluent-theme-tests=OK;checks=" + _checks);
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

    private static bool Same(Color a, Color b)
    {
        return a.ToArgb() == b.ToArgb();
    }

    private static void TokensMatchTheSpec()
    {
        FluentPalette light = FluentPalette.Build(false, false);
        FluentPalette dark = FluentPalette.Build(false, true);
        Check(Same(light.Background, Color.FromArgb(0xF3, 0xF3, 0xF3)), "light background");
        Check(Same(light.Card, Color.White), "light card");
        Check(Same(light.Accent, Color.FromArgb(0x00, 0x5F, 0xB8)), "light accent");
        Check(Same(dark.Background, Color.FromArgb(0x20, 0x20, 0x20)), "dark background");
        Check(Same(dark.Card, Color.FromArgb(0x2D, 0x2D, 0x2D)), "dark card");
        Check(Same(dark.Accent, Color.FromArgb(0x60, 0xCD, 0xFF)), "dark accent");
        Check(Same(dark.TextOnAccent, Color.FromArgb(0, 0, 0)), "dark text on accent");
    }

    private static void TextIsReadable()
    {
        foreach (bool dark in new[] { false, true })
        {
            FluentPalette p = FluentPalette.Build(false, dark);
            foreach (Color back in new[] { p.Background, p.Layer, p.Card, p.Selected, p.InfoBackground, p.Input })
            {
                Check(Ratio(p.Text, back) >= 4.5, "text on " + back + " dark=" + dark);
                Check(Ratio(p.TextSecondary, back) >= 4.5, "secondary on " + back + " dark=" + dark);
            }
            Check(Ratio(p.TextOnAccent, p.Accent) >= 4.5, "text on accent dark=" + dark);
        }
    }

    /// <summary>
    /// Dark text boxes were filled with the window background, which read as black holes
    /// in the cards and were hard to read. A dark input is a step lighter than its card.
    /// </summary>
    private static void DarkInputsAreLighterThanCards()
    {
        FluentPalette dark = FluentPalette.Build(false, true);
        Check(Luminance(dark.Input) > Luminance(dark.Card),
            "a dark text box (" + dark.Input + ") should be lighter than its card (" + dark.Card + ")");
        FluentPalette light = FluentPalette.Build(false, false);
        Check(light.Input.ToArgb() == Color.White.ToArgb(), "a light text box stays white");
        using (DashboardForm form = new DashboardForm())
        {
            ((CheckBox)DashField(form, "_darkModeCheckBox")).Checked = true;
            if (!SystemInformation.HighContrast)
            {
                TextBox mac = (TextBox)DashField(form, "_macTextBox");
                Check(mac.BackColor.ToArgb() == dark.Input.ToArgb(),
                    "the dashboard's dark text boxes should use the input colour, is " + mac.BackColor);
            }
        }
        using (var center = new NotificationCenterForm(new AppSettings(), new List<NotificationHistoryEntry>(), true))
        {
            if (!SystemInformation.HighContrast)
            {
                TextBox search = (TextBox)typeof(NotificationCenterForm)
                    .GetField("_searchBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(center);
                Check(search.BackColor.ToArgb() == dark.Input.ToArgb(),
                    "the notification center's dark text boxes should use the input colour, is " + search.BackColor);
            }
        }
    }

    private static void HighContrastIsSystemDerived()
    {
        FluentPalette p = FluentPalette.Build(true, true);
        Check(p.IsHighContrast, "high contrast flag");
        Check(Same(p.Text, SystemColors.WindowText), "high contrast text is system");
    }

    private static void AccessibilityAndTrayFollowFluent()
    {
        if (SystemInformation.HighContrast)
        {
            return;
        }
        Color card = FluentPalette.Build(false, true).Card;
        Check(Same(MacRando.Accessibility.BuildPalette(false, true).Surface, card), "accessibility surface is fluent card");
        Check(Same(TrayPalette.Build(true).Surface, card), "tray surface is fluent card");
    }

    private static void DarkIsTheDefaultTheme()
    {
        Check(DashboardForm.ParseThemePreference(null), "null is dark");
        Check(DashboardForm.ParseThemePreference(""), "empty is dark");
        Check(DashboardForm.ParseThemePreference("dark"), "dark is dark");
        Check(!DashboardForm.ParseThemePreference(" LIGHT \r\n"), "padded LIGHT is light");
        Check(!DashboardForm.ParseThemePreference("light"), "light is light");
        Check(DashboardForm.ParseThemePreference("garbage"), "garbage is dark");
    }

    private static void HighContrastComesFromAccessibility()
    {
        FluentPalette p = FluentPalette.Build(true, false);
        HighContrastPalette a = MacRando.Accessibility.BuildPalette(true, false);
        Check(Same(p.Card, a.Surface), "hc card is accessibility surface");
        Check(Same(p.Text, a.Text), "hc text is accessibility text");
        Check(Same(p.Accent, a.Accent), "hc accent is accessibility accent");
    }

    private static PivotBar MakePivot(Form host, Panel[] pages)
    {
        PivotBar bar = new PivotBar();
        host.Controls.Add(bar);
        for (int i = 0; i < pages.Length; i++)
        {
            host.Controls.Add(pages[i]);
            bar.AddTab("Tab " + i, pages[i]);
        }
        return bar;
    }

    private static Panel[] ThreePages()
    {
        return new[] { new Panel(), new Panel(), new Panel() };
    }

    private static void PivotShowsOnlyTheSelectedPage()
    {
        using (Form host = new Form())
        {
            Panel[] pages = ThreePages();
            PivotBar bar = MakePivot(host, pages);
            TestHelpers.ShowOffScreen(host);
            int raised = 0;
            bar.SelectedIndexChanged += delegate { raised++; };
            bar.SelectedIndex = 2;
            Check(!pages[0].Visible && !pages[1].Visible && pages[2].Visible, "only page 2 visible");
            Check(raised == 1, "changed raised once, was " + raised);
            bar.SelectedIndex = 2;
            Check(raised == 1, "same index does not raise");
        }
    }

    private static void PivotIsAccessible()
    {
        using (Form host = new Form())
        {
            PivotBar bar = MakePivot(host, ThreePages());
            bar.SelectedIndex = 1;
            Check(bar.AccessibleRole == AccessibleRole.PageTabList, "bar role");
            for (int i = 0; i < bar.TabCount; i++)
            {
                Check(bar.TabButton(i).AccessibleRole == AccessibleRole.PageTab, "tab role " + i);
                string expected = i == 1 ? "Selected" : "";
                Check((bar.TabButton(i).AccessibleDescription ?? "") == expected, "tab description " + i);
            }
        }
    }

    // Keyboard: PivotBar.ProcessCmdKey (the protected path) handles Left/Right while a tab button
    // has focus; the test reaches it by reflection on the real instance.
    private static bool Press(PivotBar bar, Keys key)
    {
        Message msg = new Message();
        object[] args = { msg, key };
        return (bool)typeof(PivotBar).GetMethod("ProcessCmdKey",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(bar, args);
    }

    private static void PivotKeyboard()
    {
        using (Form host = new Form())
        {
            Panel[] pages = ThreePages();
            PivotBar bar = MakePivot(host, pages);
            TestHelpers.ShowOffScreen(host);
            bar.SelectedIndex = 2;
            bar.TabButton(2).Focus();
            Press(bar, Keys.Right);
            Check(bar.SelectedIndex == 2, "right on last stays, was " + bar.SelectedIndex);
            bar.SelectedIndex = 1;
            bar.TabButton(1).Focus();
            Press(bar, Keys.Left);
            Check(bar.SelectedIndex == 0, "left on tab 1 selects 0, was " + bar.SelectedIndex);
            Check(bar.TabButton(0).Focused, "focus follows selection");
            // The selected page directly follows the bar in tab order, so Tab goes button -> page content.
            Check(pages[0].TabIndex == bar.TabIndex + 1, "selected page follows the bar in TabIndex");
            bar.SelectedIndex = 2;
            Check(pages[2].TabIndex == bar.TabIndex + 1, "page 2 follows the bar after selecting it");
        }
    }

    private static void ChromeIsSafeWithoutHandle()
    {
        using (Form form = new Form())
        {
            FluentChrome.Apply(form, true);
            Check(!form.IsHandleCreated, "chrome must not create the handle");
            FluentChrome.Apply(IntPtr.Zero, true);
        }
    }

    private static void CardPaintSurvivesZeroSize()
    {
        using (TableLayoutPanel card = new TableLayoutPanel())
        using (Bitmap bmp = new Bitmap(10, 10))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            FluentCard.Attach(card, delegate { return FluentPalette.Build(false, false); });
            card.Size = new Size(0, 0);
            card.GetType().GetMethod("OnPaint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(card, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 0, 0)) });
            Check(true, "zero size paint");
        }
    }

    private static double Ratio(Color foreground, Color background)
    {
        double a = Luminance(foreground);
        double b = Luminance(background);
        if (a < b)
        {
            double swap = a;
            a = b;
            b = swap;
        }
        return (a + 0.05) / (b + 0.05);
    }

    private static double Luminance(Color c)
    {
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Channel(byte value)
    {
        double cs = value / 255.0;
        return cs <= 0.03928 ? cs / 12.92 : Math.Pow((cs + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// The redesign moved every network action onto pages, so each one is checked to still
    /// reach the code that performs it: every action button, checkbox, the search box and
    /// the adapter list must raise the dashboard event TrayContext handles. The network
    /// code behind those events is unchanged and covered by the mock network tests.
    /// </summary>
    private static void DashboardActionsStillRaiseTheirEvents()
    {
        using (DashboardForm form = new DashboardForm())
        {
            form.SetAdapters(new[]
            {
                new AdapterInfo
                {
                    Name = "Ethernet", Description = "Test adapter", InterfaceIndex = 3,
                    InterfaceGuid = "{121EC763-A5F6-4EF7-AD11-554AD8464295}", Status = "Up", IsUp = true,
                    MacAddress = "02-00-00-00-00-01", PermanentMacAddress = "A4-1B-C0-11-22-33",
                    MacPropertySupported = true, IpAddress = "192.168.1.42", PrefixLength = 24,
                    DhcpEnabled = true, LinkSpeed = "1 Gbps"
                },
                new AdapterInfo
                {
                    Name = "Wi-Fi", Description = "Wireless", InterfaceIndex = 7,
                    InterfaceGuid = "{22222222-2222-2222-2222-222222222222}", Status = "Up", IsUp = true,
                    MacAddress = "02-00-00-00-00-02", PermanentMacAddress = "02-00-00-00-00-02",
                    MacPropertySupported = true, IpAddress = "10.0.0.2", PrefixLength = 24
                }
            });
            form.SetVpnProfiles(new[] { new VpnProfile { Name = "Home", ServerAddress = "vpn.example.com" } });
            form.SetBackupAvailable(true);
            form.SetPendingRestoreCount(1);
            form.SetKeepChangeAvailable(true);
            TestHelpers.ShowOffScreen(form);
            Application.DoEvents();

            string[,] buttons =
            {
                { "_refreshButton", "RefreshRequested" },
                { "_randomizeBothButton", "RandomizeBothRequested" },
                { "_randomizeMacButton", "RandomizeMacRequested" },
                { "_applyMacButton", "ManualMacRequested" },
                { "_restorePermanentButton", "RestorePermanentRequested" },
                { "_randomizeIpButton", "RandomizeIpRequested" },
                { "_applyDnsButton", "DnsChangeRequested" },
                { "_restoreButton", "RestoreRequested" },
                { "_connectVpnButton", "ConnectVpnRequested" },
                { "_disconnectVpnButton", "DisconnectVpnRequested" },
                { "_savePresetButton", "SavePresetRequested" },
                { "_applyPresetButton", "ApplyPresetRequested" },
                { "_startupRandomizeButton", "StartupRandomizeToggleRequested" },
                { "_notificationCenterButton", "NotificationCenterRequested" },
                { "_ipPreflightButton", "IpPreflightRequested" },
                { "_keepChangeButton", "KeepChangeRequested" },
                { "_favoriteButton", "ToggleFavoriteRequested" }
            };
            for (int i = 0; i < buttons.GetLength(0); i++)
            {
                Button button = (Button)DashField(form, buttons[i, 0]);
                int raised = CountRaised(form, buttons[i, 1], delegate { Click(button); });
                Check(raised == 1, buttons[i, 0] + " raised " + buttons[i, 1] + " " + raised + " times, expected once");
            }

            Button restoreAll = null;
            foreach (Control c in DashboardForm.EnumerateControls(form))
            {
                Button b = c as Button;
                if (b != null && b.Text == "Restore all")
                {
                    restoreAll = b;
                }
            }
            Check(restoreAll != null, "the pending banner has no Restore all button");
            Check(CountRaised(form, "RestoreAllPendingRequested", delegate { Click(restoreAll); }) == 1,
                "Restore all did not raise RestoreAllPendingRequested");

            CheckBox dhcp = (CheckBox)DashField(form, "_allowDhcpIpCheckBox");
            Check(CountRaised(form, "DhcpIpConsentChanged", delegate { dhcp.Checked = !dhcp.Checked; }) == 1,
                "the DHCP consent box did not raise DhcpIpConsentChanged");
            CheckBox location = (CheckBox)DashField(form, "_showPublicIpLocationCheckBox");
            Check(CountRaised(form, "PublicIpLocationConsentChanged", delegate { location.Checked = !location.Checked; }) == 1,
                "the public IP location box did not raise PublicIpLocationConsentChanged");
            CheckBox darkBox = (CheckBox)DashField(form, "_darkModeCheckBox");
            Check(CountRaised(form, "DarkModeChanged", delegate { darkBox.Checked = !darkBox.Checked; }) == 1,
                "the dark mode box did not raise DarkModeChanged");
            darkBox.Checked = !darkBox.Checked;

            TextBox search = (TextBox)DashField(form, "_adapterSearchBox");
            Check(CountRaised(form, "AdapterViewChanged", delegate { search.Text = "Eth"; }) >= 1,
                "typing in the adapter search did not raise AdapterViewChanged");
            search.Text = string.Empty;

            ListView list = (ListView)DashField(form, "_adapterList");
            Check(list.Items.Count == 2, "both test adapters should be listed, found " + list.Items.Count);
            int target = list.SelectedIndices.Count == 1 && list.SelectedIndices[0] == 0 ? 1 : 0;
            Check(CountRaised(form, "AdapterChanged", delegate { list.Items[target].Selected = true; }) >= 1,
                "selecting another adapter did not raise AdapterChanged");
        }
    }

    private static object DashField(DashboardForm form, string name)
    {
        FieldInfo field = typeof(DashboardForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null, "the dashboard has no field " + name);
        return field.GetValue(form);
    }

    // Through OnClick rather than PerformClick, so a button disabled by the test's adapter
    // state is still checked for its wiring. Reachability is LayoutTests' job.
    private static void Click(Button button)
    {
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button, new object[] { EventArgs.Empty });
    }

    /// <summary>
    /// Both windows add their tabs before the strip has a parent. The selected tab was
    /// then given a bold copy of the default font (Microsoft Sans Serif) and kept it, so
    /// the first tab rendered in a different typeface until the user switched tabs.
    /// </summary>
    private static void PivotTabsUseTheirWindowsFont()
    {
        using (Form host = new Form())
        {
            host.Font = new Font("Segoe UI Variable Text", 9F);
            Panel[] pages = ThreePages();
            PivotBar bar = new PivotBar();
            for (int i = 0; i < pages.Length; i++)
            {
                bar.AddTab("Tab " + i, pages[i]);
            }
            host.Controls.Add(bar);
            Check(bar.TabButton(0).Font.Name == host.Font.Name && bar.TabButton(0).Font.Bold,
                "the selected tab should be the window's font in bold, is " + bar.TabButton(0).Font);
            Check(bar.TabButton(1).Font.Name == host.Font.Name && !bar.TabButton(1).Font.Bold,
                "an unselected tab should be the window's font, is " + bar.TabButton(1).Font);
            bar.SelectedIndex = 1;
            Check(bar.TabButton(1).Font.Bold && !bar.TabButton(0).Font.Bold,
                "selecting a tab should move the bold to it");
        }
    }

    /// <summary>
    /// Under high contrast the selected adapter row must be filled with the system
    /// Highlight colour: the Fluent selection fill maps to Control, which equals Window in
    /// several high contrast themes and left only a thin bar marking the selection.
    /// </summary>
    private static void SelectedAdapterRowUsesHighlightInHighContrast()
    {
        FluentPalette hc = FluentPalette.Build(true, false);
        Check(DashboardForm.SelectedRowFill(true, hc) == SystemColors.Highlight,
            "a selected adapter row should be Highlight under high contrast");
        Check(DashboardForm.SelectedRowText(true, hc) == SystemColors.HighlightText,
            "a selected adapter row's text should be HighlightText under high contrast");
        FluentPalette light = FluentPalette.Build(false, false);
        Check(DashboardForm.SelectedRowFill(false, light).ToArgb() == light.Selected.ToArgb(),
            "outside high contrast the selected row uses the Fluent selection fill");
    }

    private static NotificationPopup MakePopup(string severity, bool dark)
    {
        var popup = new NotificationPopup(null, "Title", "Message", dark, severity, 60000, false);
        popup.Configure("p", "{guid}", true, true,
            delegate { return System.Threading.Tasks.Task.FromResult(0); },
            delegate { return System.Threading.Tasks.Task.FromResult(0); }, false, 60000);
        return popup;
    }

    /// <summary>
    /// The leftmost action of a popup is its primary one, filled with the accent colour;
    /// the rest are plain card-coloured buttons.
    /// </summary>
    private static void PopupFirstActionIsPrimary()
    {
        if (SystemInformation.HighContrast)
        {
            return;
        }
        foreach (bool dark in new[] { false, true })
        {
            FluentPalette palette = FluentPalette.Build(false, dark);
            using (NotificationPopup popup = MakePopup(NotificationKinds.Error, dark))
            {
                TestHelpers.ShowOffScreen(popup);
                Application.DoEvents();
                Control panel = (Control)typeof(NotificationPopup)
                    .GetField("_actionsPanel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(popup);
                var visible = new List<Button>();
                foreach (Control c in panel.Controls)
                {
                    if (c is Button && c.Visible)
                    {
                        visible.Add((Button)c);
                    }
                }
                Check(visible.Count >= 2, "the popup should show at least two actions, shows " + visible.Count);
                // Reading order: top row first, then left to right.
                visible.Sort(delegate(Button a, Button b) { return a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left); });
                Check(visible[0].BackColor.ToArgb() == palette.Accent.ToArgb() &&
                      visible[0].ForeColor.ToArgb() == palette.TextOnAccent.ToArgb(),
                    "the leftmost popup action \"" + visible[0].Text + "\" should be accent filled (" + (dark ? "dark" : "light") + ")");
                for (int i = 1; i < visible.Count; i++)
                {
                    Check(visible[i].BackColor.ToArgb() == palette.Card.ToArgb(),
                        "the popup action \"" + visible[i].Text + "\" should be card coloured (" + (dark ? "dark" : "light") + ")");
                }
            }
        }
    }

    /// <summary>The severity glyph is painted, so its severity has to be in its name.</summary>
    private static void PopupSeverityIsNamed()
    {
        foreach (string severity in new[] { NotificationKinds.Error, NotificationKinds.Info, NotificationKinds.Warning })
        {
            using (NotificationPopup popup = MakePopup(severity, false))
            {
                FieldInfo field = typeof(NotificationPopup).GetField("_severityGlyph", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(field != null, "the popup has no _severityGlyph");
                Control glyph = (Control)field.GetValue(popup);
                Check(glyph.AccessibleName != null && glyph.AccessibleName.Contains(NotificationKinds.Normalize(severity)),
                    "the " + severity + " popup's glyph is named \"" + glyph.AccessibleName + "\"");
            }
        }
    }

    private sealed class Counter
    {
        public int Count;

        public void Hit(object sender, EventArgs e)
        {
            Count++;
        }

        public void HitHistory(object sender, NotificationHistoryEventArgs e)
        {
            Count++;
        }
    }

    // CreateDelegate needs an exact signature, so the history events bind to HitHistory.
    private static int CountRaised(object form, string eventName, Action action)
    {
        EventInfo info = form.GetType().GetEvent(eventName);
        Check(info != null, form.GetType().Name + " has no event " + eventName);
        var counter = new Counter();
        string method = info.EventHandlerType == typeof(EventHandler<NotificationHistoryEventArgs>) ? "HitHistory" : "Hit";
        Delegate handler = Delegate.CreateDelegate(info.EventHandlerType, counter, method);
        info.AddEventHandler(form, handler);
        try
        {
            action();
            Application.DoEvents();
        }
        finally
        {
            info.RemoveEventHandler(form, handler);
        }
        return counter.Count;
    }

    /// <summary>
    /// The notification center's actions, Restore and Retry among them, must still reach
    /// the handlers TrayContext attaches, whichever tab they ended up on.
    /// </summary>
    private static void CenterActionsStillRaiseTheirEvents()
    {
        var entries = new List<NotificationHistoryEntry>
        {
            new NotificationHistoryEntry
            {
                NotificationId = "wire-1", TimestampUtc = DateTime.UtcNow, Title = "Restore needs attention",
                Message = "Could not restore.", Severity = NotificationKinds.Critical, AdapterKey = "{guid}",
                Action = "Retry", CanRetry = true, CanRestore = true
            }
        };
        using (var center = new NotificationCenterForm(new AppSettings(), entries, false))
        {
            TestHelpers.ShowOffScreen(center);
            Application.DoEvents();
            ListView list = (ListView)typeof(NotificationCenterForm)
                .GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(center);
            Check(list.Items.Count == 1, "the test notification should be listed");
            list.Items[0].Selected = true;
            Application.DoEvents();

            string[,] buttons =
            {
                { "Open dashboard", "OpenDashboardRequested" },
                { "Restore now", "RestoreRequested" },
                { "Retry", "RetryRequested" },
                { "Diagnostics", "DiagnosticsRequested" },
                { "Refresh", "RefreshRequested" },
                { "Clear history", "ClearHistoryRequested" },
                { "Apply preferences", "SettingsChanged" },
                { "Check for updates", "CheckUpdatesRequested" },
                { "Send test notification", "SendTestNotificationRequested" }
            };
            for (int i = 0; i < buttons.GetLength(0); i++)
            {
                Button button = null;
                foreach (Control c in DashboardForm.EnumerateControls(center))
                {
                    Button b = c as Button;
                    if (b != null && b.Text == buttons[i, 0] && !(b.Parent is PivotBar))
                    {
                        button = b;
                    }
                }
                Check(button != null, "the notification center has no \"" + buttons[i, 0] + "\" button");
                Button target = button;
                int raised = CountRaised(center, buttons[i, 1], delegate { Click(target); });
                Check(raised == 1, "\"" + buttons[i, 0] + "\" raised " + buttons[i, 1] + " " + raised + " times, expected once");
            }
        }
    }
}
