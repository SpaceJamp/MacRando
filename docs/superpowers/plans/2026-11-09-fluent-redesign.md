# MacRando Fluent redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle and restructure the dashboard, notification center, popups and tray menu in a Windows 11 Fluent look with no behaviour or accessibility change.

**Architecture:** One new file, `src/FluentTheme.cs`, holds the palette, a tab strip (`PivotBar`), card painting and DWM window chrome. Each window then rebuilds its layout on top of it and keeps every existing control field, event and text.

**Tech Stack:** .NET Framework 4.x WinForms, C# 5, `csc.exe` (`build.ps1`, `test.ps1`).

**Spec:** `docs/superpowers/specs/2026-11-09-fluent-redesign-design.md`

## Global Constraints

- C# 5 only (no `?.`, no `$""`, no `nameof`, no expression-bodied members).
- Every form: `SuspendLayout()` before `AutoScaleMode = AutoScaleMode.Dpi`, `ResumeLayout(false); PerformLayout();` after building. No size scaled twice.
- Keep every private control field name and type the tests reflect on (list in the spec, "Constraints from the codebase").
- Keep every control's text, tooltip, event and tab-order relation (search < adapter list < MAC box < Apply MAC).
- High contrast overrides dark and light; under high contrast every colour comes from `Accessibility.BuildPalette(true, ...)`.
- `Accessibility.BuildPalette(false, false).Surface == Color.White`.
- New source files are added to the explicit `$sources` list in `test.ps1`; new test classes are called from `tests/NetworkServiceMockTests.cs` the way `LayoutTests.Run()` is.
- Body font 9pt; "Segoe UI Variable Text" if `FontFamily.Families` has it, else "Segoe UI".
- No commits, pushes or releases: the user asked to be consulted first. Each task ends with `.\test.ps1` green instead of a commit.
- The user's name never appears in files; copyright holder is SpaceJamp.

## Review Focus

- A control that existed before ends up on no page, or on a page nobody can reach: `EveryOldControlIsOnAPage` (Task 3).
- An operation status message posted while the user is on another page goes unseen: `TheStatusLineIsVisibleOnEveryPage` (Task 3).
- Overlap or clipping on a page other than the default one, at 980x620 and 150%: per-page sweeps (Task 3, Task 4).
- Keyboard user cannot reach page content after the tab strip, or arrows wrap wrongly: `PivotKeyboard` (Task 2).
- DWM calls throwing on Windows 10 or on a form whose handle is not created: `ChromeIsSafeWithoutHandle` (Task 2).

---

### Task 1: Fluent palette

**Files:**
- Create: `src/FluentTheme.cs`
- Modify: `src/Accessibility.cs` (normal branch of `BuildPalette`), `src/TrayTheme.cs` (`TrayPalette.Build`)
- Test: create `tests/FluentThemeTests.cs`; add both files to `test.ps1`; call `FluentThemeTests.Run()` from `tests/NetworkServiceMockTests.cs`

**Interfaces:**
- Produces: `internal sealed class FluentPalette` with `Color` properties `Background, Layer, Card, CardBorder, ControlBorder, Text, TextSecondary, Accent, TextOnAccent, Selected, InfoBackground, Success, Warning, Error` and `bool IsHighContrast`; `static FluentPalette Build(bool highContrast, bool dark)`; `static string BodyFontFamily()`.

- [ ] **Step 1: Write failing tests** in `FluentThemeTests`:
  - `TokensMatchTheSpec`: light `Background == #F3F3F3`, `Card == Color.White` (ARGB compare), `Accent == #005FB8`; dark `Background == #202020`, `Card == #2D2D2D`, `Accent == #60CDFF`, `TextOnAccent == #000000`.
  - `TextIsReadable`: contrast ratio ≥ 4.5 for `Text` and `TextSecondary` on `Background`, `Layer`, `Card`, `Selected`, `InfoBackground`; `TextOnAccent` on `Accent`; both modes. Reuse the contrast formula in `tests/ContrastTests.cs`.
  - `HighContrastIsSystemDerived`: `Build(true, true).Text == SystemColors.WindowText`, `IsHighContrast`.
  - `AccessibilityAndTrayFollowFluent`: `Accessibility.BuildPalette(false, true).Surface == FluentPalette.Build(false, true).Card`; `TrayPalette.Build(true).Surface == FluentPalette.Build(false, true).Card`.
- [ ] **Step 2: Run `.\test.ps1`**, expect compile failure (no `FluentPalette`).
- [ ] **Step 3: Implement** `FluentPalette` with the spec's palette table. Success/warning/error light `#0F7B0F/#9D5D00/#C42B1C`, dark `#6CCB5F/#FCE100/#FF99A4`. Point `Accessibility.BuildPalette` normal branch and `TrayPalette.Build` at it.
- [ ] **Step 4: Run `.\test.ps1`**, expect all OK. `ContrastTests`, `TrayThemeTests`, `NormalPalettesAreUnchanged` must still pass. If one fails on a colour, fix the colour, not the test.

**Dark by default (same task, `src/DashboardForm.cs:2216` `LoadThemePreference`):**

- [ ] **Step 5: Write failing test** `DarkIsTheDefaultTheme` in `FluentThemeTests`, against a new `internal static bool DashboardForm.ParseThemePreference(string saved)`: `null` → true, `""` → true, `"dark"` → true, `" LIGHT \r\n"` → false, `"light"` → false, `"garbage"` → true.
- [ ] **Step 6: Implement.** `ParseThemePreference` returns `!string.Equals((saved ?? "").Trim(), "light", OrdinalIgnoreCase)`. `LoadThemePreference` passes the file text, or null when the file is missing; the `catch` returns true. Run `.\test.ps1`; tests that assumed a light start must set `_darkModeCheckBox` explicitly. Fix those tests, not the default.

### Task 2: PivotBar, cards, window chrome

**Files:**
- Modify: `src/FluentTheme.cs`
- Test: `tests/FluentThemeTests.cs`

**Interfaces:**
- Consumes: `FluentPalette` (Task 1).
- Produces:
  - `internal sealed class PivotBar : FlowLayoutPanel`: `int AddTab(string label, Control page)`, `int SelectedIndex {get; set;}`, `int TabCount {get;}`, `Button TabButton(int index)`, `event EventHandler SelectedIndexChanged`, `void ApplyPalette(FluentPalette p)`.
  - `internal static class FluentCard`: `static void Attach(TableLayoutPanel card, Func<FluentPalette> palette)` (paints the 8px DPI-scaled rounded fill and border on `Paint`).
  - `internal static class FluentChrome`: `static void Apply(Form form, bool dark)` and `static void Apply(IntPtr hwnd, bool dark)` (DWM attribute 20 dark title bar, 33 rounded corners, value 2 = round; runs on `HandleCreated` if no handle yet; every P/Invoke in try/catch).

- [ ] **Step 1: Write failing tests:**
  - `PivotShowsOnlyTheSelectedPage`: 3 tabs over 3 panels; after `SelectedIndex = 2` only page 2 `Visible`; `SelectedIndexChanged` raised once.
  - `PivotIsAccessible`: `AccessibleRole == PageTabList`; each `TabButton(i).AccessibleRole == PageTab`; selected button's `AccessibleDescription == "Selected"`, others empty.
  - `PivotKeyboard`: Right on the last tab stays on the last (no wrap); Left on tab 1 selects tab 0; a tab button and the first control of its page are consecutive in `TabIndex` order (send keys via the protected `ProcessCmdKey` path through a test subclass or `PreviewKeyDown`; pick one and document it in the test).
  - `ChromeIsSafeWithoutHandle`: `FluentChrome.Apply(new Form(), true)` does not throw and does not create the handle (`IsHandleCreated` stays false).
- [ ] **Step 2: Run `.\test.ps1`**, expect failure.
- [ ] **Step 3: Implement.** Tab buttons: `FlatStyle.Flat`, border size 0, `AutoSize`, selected one semibold with a 3px accent bar painted under its text (`Paint` handler, height scaled by `DeviceDpi`-independent `CurrentAutoScaleDimensions.Height / 96` of the parent form, as in `NotificationPopup.ApplySize`).
- [ ] **Step 4: Run `.\test.ps1`**, expect all OK.

### Task 3: Dashboard restructure

**Files:**
- Modify: `src/DashboardForm.cs` (`BuildRootLayout`, `BuildHeader`, `BuildListPane`, details-pane builder around lines 1830-2000, `ConfigureSafetyBody`, theme method around line 2367, `CreateCard`)
- Test: `tests/LayoutTests.cs`, `tests/ControlSweepTests.cs`, `tests/AccessibilityTests.cs`

**Interfaces:**
- Consumes: `FluentPalette`, `PivotBar`, `FluentCard`, `FluentChrome`.
- Produces: private field `PivotBar _pivot` on `DashboardForm` with tabs in this order: 0 "MAC address", 1 "Local network", 2 "Public IP and VPN", 3 "Adapter details", 4 "Safety and settings". Tests select pages through `_pivot.SelectedIndex`.

- [ ] **Step 1: Write failing tests:**
  - LayoutTests `EveryOldControlIsOnAPage`: for each of `_randomizeMacButton, _applyMacButton, _randomizeBothButton, _restorePermanentButton, _restoreButton, _macTextBox, _presetCombo, _savePresetButton, _applyPresetButton, _dnsCombo, _applyDnsButton, _randomizeIpButton, _ipPreflightButton, _vpnCombo, _connectVpnButton, _disconnectVpnButton, _driverLabel, _guidLabel, _allowDhcpIpCheckBox, _showPublicIpLocationCheckBox, _darkModeCheckBox, _startupRandomizeButton, _notificationCenterButton, _safetySummaryLabel`, some `_pivot` index makes it visible (`Visible(c, form)` non-empty) at 1120x720.
  - LayoutTests `TheStatusLineIsVisibleOnEveryPage`: `_operationStatusLabel` visible on all 5 pages at 980x620.
  - Extend `NothingOverlapsAtAnySize`, `NoControlIsClippedAtAnySize`, `NothingOverlapsUnderHighDpiScaling`, `NoLabelIsShorterThanItsText` and the `ControlSweepTests` dashboard sweep to loop `_pivot.SelectedIndex` over 0..4, putting the page name in the failure message.
  - AccessibilityTests: extend `AuditNames` to every page. `LiveControlsAreInReadingOrder` stays as it is: page 0 is the default.
- [ ] **Step 2: Run `.\test.ps1`**, expect failure (`_pivot` missing).
- [ ] **Step 3: Implement the spec's "Dashboard" section.** Body columns 40/60. The left pane holds the title, subtitle, search, filter row, caption, list, hint and licence. The content column has rows: banner (AutoSize), header (AutoSize), `_pivot` (AutoSize), page host (Percent 100, each page a `Panel AutoScroll` holding a `CreatePageLayout()` of cards), status bar (AutoSize). `_darkModeCheckBox` moves to page 4. Header keeps `_headerPublicIpLabel`/`_headerPublicIpLocationLabel` in one AutoSize cell. `CreateCard` calls `FluentCard.Attach`. Theme method colours from `FluentPalette.Build(_highContrastActive, _darkMode)`, plus `FluentChrome.Apply(this, dark && !highContrast)`. Adapter list owner-draw: selected row fill `Selected`, 3px `Accent` bar on its left edge, text `Text` (drop the old blue selected fill and white selected text).
- [ ] **Step 4: Run `.\test.ps1`**, expect all OK. Fix the layout until the tests pass. Never loosen a test.

### Task 4: Notification center

**Files:**
- Modify: `src/NotificationCenterForm.cs` (constructor layout ~lines 76-340, theme walk ~lines 735-816, `MakeButton`)
- Test: `tests/AccessibilityTests.cs`, `tests/ControlSweepTests.cs`, `tests/LayoutTests.cs`

**Interfaces:**
- Consumes: Task 1-2 types.
- Produces: private `PivotBar _pivot` on `NotificationCenterForm`, tab 0 "History", tab 1 "Preferences".

- [ ] **Step 1: Write failing tests:** `CenterPreferencesAreOnTheirPage` (with `_pivot.SelectedIndex = 1`, `_updateUrlBox`, `_signerBox`, `_quietStart`, `_duration` visible; with index 0 they are not). Extend `NotificationCenterControlsAreNamed`, `NotificationCenterHasReadingOrderTabSequence`, `HighContrastAppliesToTheCenter` and the center sweep in `ControlSweepTests` to both pages. Add a center overlap check at 900x600 and 1280x800, both pages, light and dark, reusing `FindOverlaps` (make it accept a `Form`, if it takes `DashboardForm` now).
- [ ] **Step 2: Run `.\test.ps1`**, expect failure.
- [ ] **Step 3: Implement the spec's "Notification center" section.** Header row: title label "Notifications", `_searchBox`, Refresh, Clear history. Then `_pivot`, then the page host, then the footer with Close. Replace the `GroupBox` with the Popups and Updates cards (`FluentCard.Attach`), with the three buttons in a row under them. The details pane becomes a card with a 2-column grid of the five action buttons. Colours come from `FluentPalette`, plus `FluentChrome.Apply`.
- [ ] **Step 4: Run `.\test.ps1`**, expect all OK.

### Task 5: Popups

**Files:**
- Modify: `src/NotificationPopup.cs` (constructor content ~lines 85-460, `MakeActionButton` ~line 530, `OnPaint`)
- Test: `tests/LayoutTests.cs` (`PopupSizesFollowTheDisplayScale` must keep passing), `tests/AccessibilityTests.cs`

**Interfaces:**
- Consumes: `FluentPalette`, `FluentChrome`.

- [ ] **Step 1: Write failing tests:** `PopupFirstActionIsPrimary` (after `SetActions` with ≥2 actions, the first button `BackColor == palette.Accent`, `ForeColor == palette.TextOnAccent`, the others `BackColor == palette.Card`). `PopupSeverityIsNamed`: the glyph control's `AccessibleName` contains "Error" for an error popup and "Information" for an info popup (match the strings `ToSeverity` produces).
- [ ] **Step 2: Run `.\test.ps1`**, expect failure.
- [ ] **Step 3: Implement the spec's "Popups" section.** The glyph is a fixed 20x20 design-px `Label` that paints a filled circle and a glyph character (✓ ! × i) in black. Remove the unused 4px accent bar paint in `OnPaint`. Call `FluentChrome.Apply(this, dark)`. Keep the `ApplySize`/`Measure` logic untouched.
- [ ] **Step 4: Run `.\test.ps1`**, expect all OK.

### Task 6: Tray menu

**Files:**
- Modify: `src/TrayTheme.cs` (`TrayThemeRenderer`), `src/TrayContext.cs` (menu construction ~line 3274: item padding and the drop-down `Opened` hook for chrome)
- Test: `tests/TrayThemeTests.cs`

- [ ] **Step 1: Write failing test:** `ItemsUseFluentHeight` (every non-separator top-level item `Height >= 30` design px × the menu's scale, using `TrayPalette` from Fluent). `TheMenuGetsACustomRenderer` and the other existing tests stay as they are.
- [ ] **Step 2: Run `.\test.ps1`**, expect failure.
- [ ] **Step 3: Implement the spec's "Tray menu" section.** Item `Padding` gives 30px rows. The highlight is a 4px-rounded rectangle inset 4px (`GraphicsPath`). Call `FluentChrome.Apply(IntPtr, bool)` on `_menu.Handle` and on each drop-down's handle in its `Opened` event.
- [ ] **Step 4: Run `.\test.ps1`**, expect all OK.

### Task 7: Real-DPI screenshot verification

**Files:**
- Create (scratchpad, not repo): `shots/Harness.cs`, `shots/app-asinvoker.manifest` (copy of `src\app.manifest` with `requestedExecutionLevel level="asInvoker"`), `shots/unaware.manifest` (the same, with the `dpiAware` element removed).

- [ ] **Step 1:** Write the harness. It builds each window with sample data (reuse the data `Populate` uses in `tests/LayoutTests.cs`), shows it off-screen (`StartPosition = Manual`, `Location = (-32000, -32000)`), and settles it. For every combination of light/dark, dashboard pages 0..4, center pages 0..1, two popups (info with 2 actions, error with 3 actions) and the open tray menu, it calls `DrawToBitmap` and saves `<window>-<page>-<theme>-<dpi>.png`.
- [ ] **Step 2:** Compile twice with `/win32manifest:`: once with `app-asinvoker.manifest` (150%, 144 DPI) and once with `unaware.manifest` (96-DPI layout). Run both.
- [ ] **Step 3:** Inspect every PNG for clipped, overlapping or cut-off text. Fix what is found in the owning task's file and re-run `.\test.ps1`. Then add a test that would have caught it.
- [ ] **Step 4:** Send the user the screenshots. Report what was not covered (125%/200% only arithmetic; tray drop-downs' DWM corners are not visible in `DrawToBitmap`).
