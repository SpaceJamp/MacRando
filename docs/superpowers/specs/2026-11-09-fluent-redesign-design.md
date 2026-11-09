# MacRando Fluent redesign: design

Approved mockup: https://claude.ai/artifact/7ruYbNNXhZRT1mshjByKYs (dashboard, notification center,
popups and tray menu, light and dark).

## Goal

Give all four MacRando windows a Windows 11 Fluent look, restructuring layout where it helps, without
changing any behaviour, wording of controls, or accessibility. High contrast keeps overriding both themes.

## What the user decided

- Windows: dashboard, notification center, notification popups, tray menu.
- Style: Windows 11 Fluent.
- Layout: full restructure allowed.
- Nothing else is fixed beyond behaviour, accessibility, high contrast and the existing tests.

## Constraints from the codebase

- .NET Framework 4.x, C# 5, `csc.exe` via `build.ps1` (compiles `src/*.cs`, so a new file needs no build change).
- `AutoScaleMode.Dpi`; every form calls `SuspendLayout()` before setting it and `ResumeLayout` after building.
  Fixed pixel sizes are scaled once by autoscale; nothing may be scaled twice.
- Tests reach controls through private field names (`_adapterList`, `_adapterSearchBox`, `_macTextBox`,
  `_applyMacButton`, `_darkModeCheckBox`, `_headerPublicIpLabel`, `_headerPublicIpLocationLabel`,
  `_publicIpLocationLabel`, `_showPublicIpLocationCheckBox`, `_listPanel`, `_searchBox`, `_updateUrlBox`,
  `_signerBox`, `_actionsPanel`, `_dismissButton`, `_menu`, ...). Those fields keep their names and types.
- Tab order: search box, then adapter list, then MAC box, then Apply MAC (`LiveControlsAreInReadingOrder`).
- The header must show the public IP and its location together at every tested size.
- `Accessibility.BuildPalette(false, false).Surface` must stay `Color.White`; dark and light must differ.

## Palette

One token set in a new `src/FluentTheme.cs` (`FluentPalette.Build(bool dark)`), used by the dashboard,
notification center, popups and `TrayPalette`. `Accessibility.BuildPalette` returns these values outside
high contrast; in high contrast every colour stays system-derived as today.

| Token | Light | Dark |
|---|---|---|
| Background (window, left pane) | #F3F3F3 | #202020 |
| Layer (content area) | #F9F9F9 | #272727 |
| Card / Surface | #FFFFFF | #2D2D2D |
| Card border | #E5E5E5 | #3A3A3A |
| Control border | #D1D1D1 | #4D4D4D |
| Text | #1B1B1B | #FFFFFF |
| Secondary text | #5C5C5C | #C8C8C8 |
| Accent (primary button fill, selection bar) | #005FB8 | #60CDFF |
| Text on accent | #FFFFFF | #000000 |
| Selected row | #E6E6E6 | #383838 |
| Info bar background | #FFF4CE | #433519 |
| Success / warning / error glyph | #0F7B0F / #9D5D00 / #C42B1C | #6CCB5F / #FCE100 / #FF99A4 |

Every text/background pair is checked by the existing contrast tests (4.5:1 body text).

Type: "Segoe UI Variable Text" when installed, else "Segoe UI". Body text stays 9pt so window sizes and
the 980x620 minimum still fit. Page title 20pt semibold; card titles 10pt semibold; captions 8pt.

## Shared pieces (src/FluentTheme.cs)

- `FluentPalette`: the tokens above, plus `ForHighContrast()` built from `Accessibility`'s palette.
- `FluentCard`: paints a rounded (8px, scaled by DPI) card background and 1px border on a
  `TableLayoutPanel`'s `Paint`. Children keep their own layout; the card only paints.
- `PivotBar`: a row of `Button`s styled as tabs (no border, semibold plus a 3px accent bar under the
  selected one). `AccessibleRole.PageTabList` on the row, `PageTab` on each button, selected state in
  `AccessibleDescription`, Left/Right arrows move between tabs, Tab enters the page. Selecting a tab shows
  its page panel and hides the others. The stock `TabControl` is not used: it cannot be themed dark.
- `Dwm.ApplyWindowChrome(Form, dark)`: `DwmSetWindowAttribute` for the dark title bar (attribute 20) and
  rounded corners (33) on Windows 11, silently ignored where unsupported.
- Buttons stay `FlatStyle.Flat` with a 1px themed border; primary buttons use the accent fill. They are
  not rounded: rounding a WinForms button needs owner drawing, which would lose the system focus cue.

## Dashboard

- Root: two columns, left pane 40%, content 60% (today 44/56).
- Left pane on Background: app title (`_titleLabel`) and subtitle, search box, Connected only + Star
  selected, "Physical adapters" caption, the adapter `ListView` (owner-drawn already: selected row gets the
  Selected fill and a 3px accent bar on its left edge, rows a little taller), list hint, licence notice.
- Content on Layer with a top-left rounded edge:
  1. Pending banner restyled as an info bar (same label and Keep change / Restore all / Dismiss buttons).
  2. Header: selected adapter name (`_selectedTitleLabel`, 20pt) and status line on the left, public IP
     and location stacked on the right, Refresh button.
  3. `PivotBar` with five pages, each a scroll host holding cards:
     - **MAC address**: current MAC + vendor, permanent MAC; Change address card (manual MAC + hint,
       preset row, Random MAC, Apply MAC, Randomize MAC + IP, Restore original, Restore saved).
     - **Local network**: IPv4, Mode, Link; DNS combo + Apply DNS; Randomize local IP and IP preflight.
     - **Public IP and VPN**: public IP + location, VPN combo + hint, Connect / Disconnect.
     - **Adapter details**: driver, interface index, NetworkAddress property, interface GUID.
     - **Safety and settings**: safety summary, Allow DHCP IP randomization, Show public IP location,
       Dark mode, Enable startup MAC randomization, Notification center.
  4. Status bar: `_operationStatusLabel` with a status dot, always visible under every page.
- The selected page is remembered for the session only (not saved).
- Dark mode is the default (user request, 2026-11-09): with no saved theme preference, or one that is
  unreadable or not "light", the app starts dark. A saved "light" is still honoured, so existing users who
  chose light keep it. High contrast still wins.
- Header Dark mode checkbox moves to the Settings page; it is the same control and event.

## Notification center

- Header: "Notifications" title, search box (`_searchBox`), Refresh, Clear history.
- `PivotBar` with **History** (the existing list and details split, the details pane as a card with the
  five action buttons in a 2-column grid) and **Preferences** (Popups card: Show popups, Play sound,
  Collapse repeats, Quiet hours, From/to, Popup seconds; Updates card: manifest URL, expected signer;
  Apply preferences, Check for updates, Send test notification).
- Footer: Close.
- The "Notification preferences" `GroupBox` is replaced by the two cards; tab order stays reading order.

## Popups

- Card surface, 1px border, Windows 11 rounded corners via DWM, 400px design width (scaled as today).
- Severity glyph: a filled circle in the severity colour with a dark glyph, painted, with the severity in
  its accessible name. Title semibold, message in secondary text, first action button accent.
- Sizing logic from the DPI fix (`ApplySize`/`Measure`) is kept as is.

## Tray menu

- `TrayPalette` takes its colours from `FluentPalette`; rows 30px design height; the hover highlight is a
  4px-rounded rectangle inset 4px from the edges; section headers in secondary semibold; rounded menu
  corners via DWM on the drop-down window. Items, order, checks and wording unchanged.

## Testing

- `.\test.ps1` must pass, including `LayoutTests`, `ControlSweepTests`, `AccessibilityTests`,
  `ContrastTests`, `TrayThemeTests`.
- Pages that are hidden are skipped by the sweeps, so the overlap, clipping, label-height and naming
  sweeps are extended to select each dashboard page and each notification center page in turn.
- New tests: the pivot exposes PageTabList/PageTab roles and arrow-key navigation; every control that
  existed before is reachable on some page; the status label is visible on every page.
- Real-DPI screenshots from a harness compiled with a copy of `src\app.manifest` (asInvoker): every
  window at 100% and 150%, light and dark, each dashboard and center page, checked for clipped or
  overlapping text. 100% needs a 96-DPI run; on this 150% machine that is done by a harness whose manifest
  declares no DPI awareness (Windows bitmap-scales it, but the layout is computed at 96 DPI).

## Out of scope

- Mica/acrylic backdrops (need Windows App SDK or undocumented APIs).
- Animations, icons beyond the few inline glyphs, new settings.
