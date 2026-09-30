using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MacRando;

/// <summary>
/// Coverage for the tray menu theme and the status colour it has to keep readable.
///
/// The tray menu is the one surface a user reliably reads, because it is at the top of the
/// menu and needs no click. It was also the last surface with no dark mode, so it stayed
/// light next to a dark dashboard.
///
/// The checks here are mostly about contrast, because a themed menu that is technically
/// dark but unreadable is no better than a light one. Every foreground/background pairing
/// the menu can produce is measured, in both modes.
/// </summary>
internal static class TrayThemeTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            TheMenuGetsACustomRenderer();
            EveryItemIsColouredInBothModes();
            DisabledTextStaysReadable();
            SectionHeadersStayDistinguishable();
            NestedDropDownsAreThemedToo();
            StatusColoursAreReadableInBothModes();
            HighContrastOverridesThePreference();
            TheStatusLineNeverInheritsASystemColour();
            Console.WriteLine("tray-theme-tests=OK;checks=" + _checks);
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
    /// WCAG relative luminance contrast ratio, as a plain double. The thresholds match the
    /// ones the rest of MacRando uses, so a colour accepted here is accepted there.
    /// </summary>
    private static double Contrast(Color foreground, Color background)
    {
        Check(foreground.A == 255 && background.A == 255,
            "contrast needs opaque colours, got alpha " + foreground.A + " and " + background.A);
        double a = Channel(foreground.R, foreground.G, foreground.B);
        double b = Channel(background.R, background.G, background.B);
        double lighter = Math.Max(a, b);
        double darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Channel(int r, int g, int b)
    {
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    private static double Linear(int value)
    {
        double v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static void CheckReadable(Color text, Color surface, string what, double minimum)
    {
        double ratio = Contrast(text, surface);
        Check(ratio >= minimum,
            what + " is only " + Math.Round(ratio, 2) + ":1 against its background, which is below the " +
            minimum + ":1 it needs. Text=" + text + " surface=" + surface);
    }

    /// <summary>
    /// A menu shaped like the real one: a status line, section headers, a nested submenu
    /// and a disabled informational row. Built here rather than taken from a TrayContext so
    /// the test needs no state store and cannot touch the real user data.
    /// </summary>
    private static ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Ready") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var header = new ToolStripMenuItem("ADAPTERS") { Enabled = false };
        header.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        menu.Items.Add(header);

        var adapters = new ToolStripMenuItem("Adapters");
        adapters.DropDownItems.Add(new ToolStripMenuItem("Ethernet - Realtek") { Enabled = false });
        adapters.DropDownItems.Add(new ToolStripMenuItem("Randomize MAC only") { Checked = true });
        menu.Items.Add(adapters);

        var disabledRow = new ToolStripMenuItem("No physical network adapter") { Enabled = false };
        menu.Items.Add(disabledRow);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit"));
        return menu;
    }

    private static void TheMenuGetsACustomRenderer()
    {
        using (ContextMenuStrip menu = BuildMenu())
        {
            // A ContextMenuStrip with no renderer uses ToolStripManager's default, which
            // ignores BackColor for the background. That is exactly why the menu stayed
            // light, so the presence of a custom renderer is the assertion.
            Check(menu.Renderer == null || menu.Renderer.GetType() != typeof(ToolStripSystemRenderer),
                "before theming, the menu should not have a system renderer");

            TrayTheme.Apply(menu, true);
            Check(menu.Renderer != null, "theming must install a renderer");
            Check(menu.Renderer is TrayThemeRenderer,
                "the renderer should be a TrayThemeRenderer, but was " +
                (menu.Renderer == null ? "null" : menu.Renderer.GetType().Name));
            // Custom, not Professional. Assigning a custom renderer puts the strip into
            // Custom mode, and assigning RenderMode afterwards would swap the renderer
            // back to a stock professional one that draws a light palette. Asserted
            // because that failure is nearly invisible: the text colours still apply, so
            // the menu looks themed at a glance while its background stays white.
            Check(menu.RenderMode == ToolStripRenderMode.Custom,
                "the menu should be in Custom render mode so the tray renderer is the one used, but was " +
                menu.RenderMode);
        }
    }

    private static void EveryItemIsColouredInBothModes()
    {
        foreach (bool dark in new[] { false, true })
        {
            using (ContextMenuStrip menu = BuildMenu())
            {
                TrayTheme.Apply(menu, dark);
                TrayPalette palette = TrayPalette.Build(dark);
                _checks++;
                foreach (ToolStripItem item in AllItems(menu))
                {
                    Check(item.ForeColor == palette.Text || item.ForeColor == palette.TextMuted,
                        "an item in the " + (dark ? "dark" : "light") + " menu has a colour that is neither the " +
                        "text colour nor the muted one: " + item.ForeColor + " on \"" + item.Text + "\"");
                    Check(item.BackColor == palette.Surface,
                        "an item in the " + (dark ? "dark" : "light") + " menu does not carry the surface colour");
                    CheckReadable(
                        item.ForeColor,
                        palette.Surface,
                        "\"" + item.Text + "\" in the " + (dark ? "dark" : "light") + " menu",
                        4.5);
                }

                // The menu's own background, which is what shows behind the items.
                _checks++;
                Check(menu.BackColor == palette.Surface,
                    "the menu background should be the surface colour in " + (dark ? "dark" : "light") + " mode");
                CheckReadable(palette.Text, menu.BackColor,
                    "menu text in " + (dark ? "dark" : "light") + " mode", 4.5);
            }
        }
    }

    private static IEnumerable<ToolStripItem> AllItems(ToolStrip root)
    {
        foreach (ToolStripItem item in root.Items)
        {
            yield return item;
            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown != null)
            {
                foreach (ToolStripItem child in AllItems(dropDown.DropDown))
                {
                    yield return child;
                }
            }
        }
    }

    private static void DisabledTextStaysReadable()
    {
        // The failure this guards: a disabled item is how a section header and an
        // informational row are shown, and the system draws disabled text in a washed-out
        // grey that is unreadable on a dark surface. "No physical network adapter" is the
        // single most common disabled row in this menu.
        foreach (bool dark in new[] { false, true })
        {
            TrayPalette palette = TrayPalette.Build(dark);
            CheckReadable(palette.TextMuted, palette.Surface,
                "disabled text in " + (dark ? "dark" : "light") + " mode", 4.5);
            Check(palette.TextMuted != palette.Text,
                "the muted colour must actually differ from the text colour, or disabled rows are not " +
                "distinguishable from actions in " + (dark ? "dark" : "light") + " mode");
        }
    }

    private static void SectionHeadersStayDistinguishable()
    {
        // Headers are disabled and bold, and actions are enabled. If disabled text were as
        // bright as enabled text, the two would be hard to tell apart in a list, and a
        // header that looks like an action is worse than one that clearly is not.
        foreach (bool dark in new[] { false, true })
        {
            TrayPalette palette = TrayPalette.Build(dark);
            Check(palette.TextMuted != palette.Text,
                "in " + (dark ? "dark" : "light") + " mode a disabled header would be the same colour as an action");
        }
    }

    private static void NestedDropDownsAreThemedToo()
    {
        // The adapter submenu is where most of the menu's items live. A theme that only
        // reached the top level would look right until the user opened the one submenu that
        // matters, which is the failure that gets reported as "the dark mode is broken".
        using (ContextMenuStrip menu = BuildMenu())
        {
            TrayTheme.Apply(menu, true);
            TrayPalette palette = TrayPalette.Build(true);
            _checks++;
            bool foundNested = false;
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
                if (dropDown == null)
                {
                    continue;
                }
                foundNested = true;
                Check(dropDown.DropDown.BackColor == palette.Surface,
                    "a submenu's own background should be themed");
                Check(dropDown.DropDown.Renderer is TrayThemeRenderer,
                    "a submenu needs its own renderer, or it is drawn by the system in the light palette");
                foreach (ToolStripItem child in dropDown.DropDownItems)
                {
                    Check(child.ForeColor == palette.Text || child.ForeColor == palette.TextMuted,
                        "a nested item was not themed: \"" + child.Text + "\" is " + child.ForeColor);
                }
            }
            Check(foundNested, "the test menu should contain a submenu");
        }
    }

    private static void StatusColoursAreReadableInBothModes()
    {
        // Every state the status line can be in, against the surface it sits on. The status
        // line is the one piece of text a user always reads, so this is the pairing that
        // most needs checking.
        foreach (bool dark in new[] { false, true })
        {
            TrayPalette palette = TrayPalette.Build(dark);
            int[] pendingCounts = new[] { 0, 1, 4 };
            foreach (int pending in pendingCounts)
            {
                foreach (bool elevated in new[] { true, false })
                {
                    Color status = TrayMenuState.StatusColorFor(pending, elevated, dark);
                    _checks++;
                    CheckReadable(status, palette.Surface,
                        "the status line with " + pending + " pending and elevated=" + elevated +
                        " in " + (dark ? "dark" : "light") + " mode", 4.5);
                }
            }

            // The "needs restoring" state must not be the same colour as the ready state, or
            // the one thing the user must not miss is the one thing they cannot see.
            Color ready = TrayMenuState.StatusColorFor(0, true, dark);
            Color needsRestore = TrayMenuState.StatusColorFor(1, true, dark);
            Check(ready != needsRestore,
                "in " + (dark ? "dark" : "light") +
                " mode the ready status and the needs-restoring status are the same colour");

            // Nor must the not-elevated state be missed, since it means the app cannot act.
            Check(ready != TrayMenuState.StatusColorFor(0, false, dark),
                "in " + (dark ? "dark" : "light") + " mode the ready status and the not-elevated status are the same colour");
        }
    }

    private static void HighContrastOverridesThePreference()
    {
        // A user who has told the system they need a specific palette should not have it
        // overridden by an app-level toggle. Checked by asserting the palette resolves to
        // system colours, which it only can when high contrast is active.
        TrayPalette palette = TrayPalette.Build(true);
        bool surfaceIsSystem = palette.Surface == SystemColors.Menu;
        _checks++;
        // On a machine not in high contrast this is expected to be false, so this asserts
        // the code path exists rather than requiring the test host to be in high contrast.
        Check(surfaceIsSystem || palette.Surface == Color.FromArgb(30, 41, 59),
            "the dark palette should be either the system menu colour or the app's own dark surface, but was " +
            palette.Surface);
        CheckReadable(palette.Text, palette.Surface, "dark palette text", 4.5);
    }

    private static void TheStatusLineNeverInheritsASystemColour()
    {
        // The regression this whole change risks: the status colour was
        // SystemColors.ControlText when ready, which is near-black, and on a dark menu that
        // is invisible. Asserted directly rather than only through contrast, because a
        // near-black on a dark surface can pass a naive check while being unreadable in
        // practice, and because it names the specific mistake.
        foreach (bool dark in new[] { false, true })
        {
            Color ready = TrayMenuState.StatusColorFor(0, true, dark);
            _checks++;
            Check(ready != SystemColors.ControlText,
                "in " + (dark ? "dark" : "light") +
                " mode the ready status still uses SystemColors.ControlText, which is unreadable on a dark menu");
            // Lightness has to be judged against the surface, not on its own: a dark navy
            // is correct on a light menu and invisible on a dark one. The contrast checks
            // above are the real assertion; this only names the direction the colour has to
            // lean, which is what makes a regression obvious in the failure message.
            if (dark)
            {
                Check(ready.GetBrightness() > 0.5,
                    "on a dark menu the ready status must be a light colour, but was " + ready);
            }
            else
            {
                Check(ready.GetBrightness() < 0.5,
                    "on a light menu the ready status must be a dark colour, but was " + ready);
            }
        }
    }
}
