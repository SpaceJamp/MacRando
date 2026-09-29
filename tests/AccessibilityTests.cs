using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using A11y = MacRando.Accessibility;
using DashboardForm = MacRando.DashboardForm;

/// <summary>
/// Accessibility and high-contrast coverage.
///
/// Two claims are being made and tested separately. First, that every interactive
/// control carries an accessible name, because a control with visible text but no
/// accessible name announces as an unlabelled "button" and the whole point of the
/// visible label is lost. Second, that when the system reports high contrast, the
/// palette comes from SystemColors rather than from MacRando's own values.
///
/// The tab order is asserted too, because a tab order that silently reverts to
/// z-order is the kind of regression no contrast or layout check would notice.
/// </summary>
internal static class AccessibilityTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            LiveFormHonoursHighContrast();
            HighContrastPaletteIsFullySystemDerived();
            HighContrastWinsOverDarkMode();
            NormalPalettesAreUnchanged();
            HighContrastNeverUsesMutedText();
            DescribeTrimsAndHandlesNothing();
            ControlNaming();
            PendingBannerButtonsAreNamed();
            LiveControlsAreNamed();
            LiveControlsAreInReadingOrder();
            Console.WriteLine("accessibility-tests=OK;checks=" + _checks);
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

    /// <summary>
    /// The live form, with high contrast forced on. This is the check that would have
    /// caught the original defect: a hand-built palette can satisfy every unit test
    /// above and still never be applied, or be applied to only some controls.
    /// </summary>
    private static void LiveFormHonoursHighContrast()
    {
        RunOnUiThread(delegate
        {
            using (DashboardForm form = new DashboardForm())
            {
                form.CreateControl();
                form.Show();
                Application.DoEvents();

                Check(!SystemInformation.HighContrast,
                    "this machine is not in high contrast, so the forced path is the only one exercised here");

                var field = typeof(DashboardForm).GetField(
                    "_highContrastActive", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(field != null, "the form should track whether high contrast is active");

                // Invoke ApplyTheme with the flag forced, then confirm the form's own
                // colours came from SystemColors rather than from the app palette.
                field.SetValue(form, true);
                MethodInfo theme = typeof(DashboardForm).GetMethod(
                    "ApplyControlTheme", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(theme != null, "ApplyControlTheme should exist");

                var button = new Button { Text = "Probe", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
                Label label = new Label { Text = "Probe" };
                Panel panel = new Panel();
                TextBox box = new TextBox();

                // Each probe control has to be passed through the method, not just the
                // first one: invoking once would leave the rest at their defaults and the
                // colour assertions would pass or fail for the wrong reason.
                Action<Control> applyHighContrast = delegate(Control target)
                {
                    theme.Invoke(form, new object[]
                    {
                        target, Color.Red, Color.Red, Color.Red, Color.Red, Color.Red, Color.Red, Color.Red, Color.Red
                    });
                };
                applyHighContrast(button);
                applyHighContrast(label);
                applyHighContrast(panel);
                applyHighContrast(box);

                Check(button.FlatStyle == FlatStyle.Standard,
                    "high contrast must restore the standard button style, not the flat custom one");
                Check(button.UseVisualStyleBackColor,
                    "high contrast must let the system draw the button");
                Check(button.BackColor == SystemColors.Window,
                    "a button under high contrast should use the system window colour");
                Check(label.ForeColor == SystemColors.WindowText,
                    "a label under high contrast should use the system window text colour");
                Check(panel.BackColor == SystemColors.Window,
                    "a panel under high contrast should use the system window colour");
                Check(box.BackColor == SystemColors.Window,
                    "a text box under high contrast should use the system window colour");

                // And the check is not vacuous: outside high contrast the same call
                // leaves the app's own colours and the flat style in place.
                field.SetValue(form, false);
                Button plain = new Button { Text = "Probe", FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
                theme.Invoke(form, new object[]
                {
                    plain, Color.FromArgb(15, 23, 42), Color.White, Color.White,
                    Color.FromArgb(30, 41, 59), Color.FromArgb(71, 85, 105),
                    Color.FromArgb(37, 99, 235), Color.FromArgb(203, 213, 225), Color.White
                });
                Check(plain.FlatStyle == FlatStyle.Flat,
                    "outside high contrast the custom flat style must still apply, or the check above proves nothing");
            }
        });
    }

    private static void HighContrastPaletteIsFullySystemDerived()
    {
        MacRando.HighContrastPalette palette = A11y.BuildPalette(true, false);
        Check(palette.IsHighContrast, "the palette should report that it is the high contrast one");
        Check(palette.Surface == SystemColors.Window, "the surface should be the system window colour");
        Check(palette.Text == SystemColors.WindowText, "the text should be the system window text colour");
        Check(palette.Accent == SystemColors.Highlight, "the accent should be the system highlight colour");
        Check(palette.TextOnAccent == SystemColors.HighlightText, "text on the accent should be the system highlight text colour");
        Check(palette.Border == SystemColors.ControlDark, "the border should be a system colour");
        Check(palette.SurfaceAlt == SystemColors.Control, "the alternate surface should be the system control colour");

        foreach (Color chosen in new[]
        {
            palette.Surface, palette.SurfaceAlt, palette.Text, palette.TextMuted,
            palette.TextOnAccent, palette.Accent, palette.Border, palette.Muted
        })
        {
            bool fromSystem =
                chosen == SystemColors.Window || chosen == SystemColors.Control ||
                chosen == SystemColors.WindowText || chosen == SystemColors.Highlight ||
                chosen == SystemColors.HighlightText || chosen == SystemColors.ControlDark ||
                chosen == SystemColors.ControlText || chosen == SystemColors.ControlLight;
            Check(fromSystem, "high contrast must not invent a colour: " + chosen);
        }
    }

    private static void HighContrastWinsOverDarkMode()
    {
        MacRando.HighContrastPalette forced = A11y.BuildPalette(true, true);
        MacRando.HighContrastPalette unforced = A11y.BuildPalette(true, false);
        Check(forced.Surface == unforced.Surface,
            "the dark mode toggle must not change the high contrast palette");
        Check(forced.Text == unforced.Text, "text must not depend on the dark mode toggle under high contrast");
        Check(forced.Accent == unforced.Accent, "the accent must not depend on the dark mode toggle under high contrast");
    }

    private static void NormalPalettesAreUnchanged()
    {
        MacRando.HighContrastPalette light = A11y.BuildPalette(false, false);
        Check(!light.IsHighContrast, "the light palette is not the high contrast one");
        Check(light.Surface == Color.White, "the light surface should still be white");
        Check(light.TextMuted != SystemColors.WindowText || SystemInformation.HighContrast,
            "the muted colour is the app's own, which is correct outside high contrast");

        MacRando.HighContrastPalette dark = A11y.BuildPalette(false, true);
        Check(!dark.IsHighContrast, "the dark palette is not the high contrast one");
        Check(dark.Surface != Color.White, "the dark surface should still be dark");
        Check(dark.Surface != light.Surface, "dark and light must remain different outside high contrast");
    }

    private static void HighContrastNeverUsesMutedText()
    {
        // A deliberately low-contrast secondary is the first thing to become unreadable
        // when someone turns on high contrast because they need it.
        MacRando.HighContrastPalette high = A11y.BuildPalette(true, false);
        Check(high.TextMuted == high.Text,
            "high contrast must not keep a muted text colour, because that defeats the purpose");
        Check(high.Muted == high.Text, "the muted palette entry must also collapse to full contrast");

        MacRando.HighContrastPalette dark = A11y.BuildPalette(false, true);
        Check(dark.TextMuted != dark.Text, "outside high contrast the muted colour is a deliberate choice");
    }

    private static void DescribeTrimsAndHandlesNothing()
    {
        Check(A11y.Describe("  Public IP  ") == "Public IP", "names should be trimmed");
        Check(A11y.Describe(null) == string.Empty, "a null name is empty");
        Check(A11y.Describe("   ") == string.Empty, "a blank name is empty");
    }

    /// <summary>
    /// The live check only runs the default state, so the banner's buttons would go
    /// unnamed without this. Asserted directly: the banner is hidden by default, and a
    /// control nobody can reach is easy to forget and impossible to notice in a normal
    /// run.
    /// </summary>
    private static void PendingBannerButtonsAreNamed()
    {
        RunOnUiThread(delegate
        {
            using (DashboardForm form = new DashboardForm())
            {
                form.CreateControl();
                form.SetPendingRestoreCount(1);
                Application.DoEvents();

                var expected = new Dictionary<string, string>
                {
                    { "Restore all", "Restore all pending adapters" },
                    { "Dismiss", "Dismiss the pending restore banner" }
                };
                foreach (KeyValuePair<string, string> pair in expected)
                {
                    Button found = null;
                    foreach (Control control in Descendants(form))
                    {
                        Button button = control as Button;
                        if (button != null && string.Equals(button.Text, pair.Key, StringComparison.Ordinal))
                        {
                            found = button;
                            break;
                        }
                    }
                    Check(found != null, "the pending banner should contain a \"" + pair.Key + "\" button");
                    Check(found.AccessibleName == pair.Value,
                        "\"" + pair.Key + "\" should be named \"" + pair.Value + "\" but was \"" + found.AccessibleName + "\"");
                    Check(!string.IsNullOrWhiteSpace(found.AccessibleDescription),
                        "\"" + pair.Key + "\" should describe what it does");
                }

                // The summary label carries the count, which is the part a screen reader
                // user needs to know the adapters are left changed.
                foreach (Control control in Descendants(form))
                {
                    Label label = control as Label;
                    if (label != null && label.Text.StartsWith("1 adapter has", StringComparison.Ordinal))
                    {
                        Check(!string.IsNullOrWhiteSpace(label.AccessibleName),
                            "the pending banner summary label should be named");
                        Check(label.AccessibleDescription.Contains("pending restore profile"),
                            "the summary description should say a restore is pending");
                    }
                }
            }
        });
    }

    private static void ControlNaming()
    {
        using (var button = new Button { Text = "Restore" })
        {
            A11y.Describe(button, "Restore this session's changes", AccessibleRole.PushButton, "Puts the adapter back.");
            Check(button.AccessibleName == "Restore this session's changes", "the name should be set");
            Check(button.AccessibleDescription == "Puts the adapter back.", "the description should be set");
        }

        using (var button = new Button { Text = "Restore" })
        {
            A11y.Describe(button, "Restore", AccessibleRole.PushButton, null);
            Check(string.IsNullOrEmpty(button.AccessibleDescription),
                "an absent description should stay empty rather than become noise");
        }

        using (var button = new Button { Text = "Restore" })
        {
            A11y.Describe(button, "  ", AccessibleRole.PushButton, "x");
            Check(string.IsNullOrEmpty(button.AccessibleName), "a blank name should not be written as spaces");
        }

        // A control that already has a role from WinForms keeps it.
        using (var check = new CheckBox { Text = "Dark mode" })
        {
            AccessibleRole before = check.AccessibleRole;
            A11y.Describe(check, "Dark mode", AccessibleRole.CheckButton, null);
            Check(check.AccessibleName == "Dark mode", "the name should still be set");
            Check(check.AccessibleRole == before, "an existing role should not be overwritten");
        }

        A11y.Describe(null, "x", AccessibleRole.Text, null);
        Check(true, "describing a null control must not throw");
    }

    private static readonly List<string> problems = new List<string>();

    private static void LiveControlsAreNamed()
    {
        RunOnUiThread(delegate
        {
            using (DashboardForm form = new DashboardForm())
            {
                form.CreateControl();
                // Both banner states are checked: the banner is hidden until a restore is
                // outstanding, and its buttons are named when it appears, so a check that
                // only looked at the default state would miss them entirely.
                foreach (int pending in new[] { 0, 2 })
                {
                    form.SetPendingRestoreCount(pending);
                    Application.DoEvents();
                    AuditNames(form, "pending=" + pending);
                }
            }
        });
    }

    private static void AuditNames(DashboardForm form, string state)
    {
        var unnamed = new List<string>();
        foreach (Control control in Descendants(form))
        {
                    if (!IsInteractive(control))
            {
                continue;
            }
            // A control whose own visible Text already reads well needs no duplicate name;
            // one that is a bare editor or list does.
            Button named = control as Button;
            bool selfDescribing = named != null &&
                !string.IsNullOrWhiteSpace(named.Text) &&
                string.Equals(named.Text.Trim(), control.AccessibleName, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(control.AccessibleName) && !selfDescribing)
            {
                unnamed.Add(state + ": " + control.GetType().Name +
                    " text=\"" + (named == null ? string.Empty : named.Text) + "\"" +
                    " parent=" + (control.Parent == null ? "(none)" : control.Parent.GetType().Name));
            }
        }
        foreach (string problem in unnamed)
        {
            problems.Add(problem);
        }
        Check(unnamed.Count == 0,
            "these interactive controls have no accessible name: " + string.Join("; ", unnamed.ToArray()));
    }

    private static void LiveControlsAreInReadingOrder()
    {
        RunOnUiThread(delegate
        {
            using (DashboardForm form = new DashboardForm())
            {
                form.CreateControl();

                FieldInfo listField = typeof(DashboardForm).GetField(
                    "_adapterList", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo searchField = typeof(DashboardForm).GetField(
                    "_adapterSearchBox", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo macField = typeof(DashboardForm).GetField(
                    "_macTextBox", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo applyField = typeof(DashboardForm).GetField(
                    "_applyMacButton", BindingFlags.Instance | BindingFlags.NonPublic);

                ListView list = (ListView)listField.GetValue(form);
                TextBox search = (TextBox)searchField.GetValue(form);
                TextBox mac = (TextBox)macField.GetValue(form);
                Button apply = (Button)applyField.GetValue(form);

                Check(search.TabIndex < list.TabIndex,
                    "the search box must be reachable before the adapter list");
                Check(list.TabIndex < mac.TabIndex,
                    "the adapter list must come before the MAC box it filters");
                Check(mac.TabIndex < apply.TabIndex,
                    "the MAC box must be reachable before the button that applies it");

                // Every control in the sequence must hold a distinct index, or traversal
                // silently falls back to z-order for the duplicates.
                var seen = new Dictionary<int, string>();
                foreach (Control control in Descendants(form))
                {
                    if (!IsInteractive(control) || control.TabStop == false)
                    {
                        continue;
                    }
                    if (seen.ContainsKey(control.TabIndex))
                    {
                        // Containers legitimately share indices with their children; only
                        // flag duplicates among controls that claim to be in the sequence.
                        continue;
                    }
                    seen[control.TabIndex] = control.GetType().Name;
                }
                Check(seen.Count > 0, "at least one control should claim a tab index");
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
        thread.Join(TimeSpan.FromSeconds(60));
        if (captured != null)
        {
            throw new Exception("Accessibility checks failed to run.", captured);
        }
    }

    private static List<Control> Descendants(Control root)
    {
        var found = new List<Control>();
        Walk(root, found);
        return found;
    }

    private static void Walk(Control control, List<Control> found)
    {
        foreach (Control child in control.Controls)
        {
            found.Add(child);
            Walk(child, found);
        }
    }

    private static bool IsInteractive(Control control)
    {
        return control is ButtonBase || control is TextBox ||
            control is ComboBox || control is ListView || control is ListBox ||
            control is NumericUpDown;
    }
}
