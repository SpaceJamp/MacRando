# MacRando Changelog

## 1.16.5 - 2026.10

### Fixed

- **The public IP location was never actually shown on screen.** The feature reported its result only as a tooltip on the public IP label, so enabling it changed nothing a user could see. The location now appears in the header, directly under the public IP, and in the "Public IP and VPN" card. The header is the only part of the window that is never scrolled, and the card that also shows the address sits at the bottom of a scroll area, so the header is where this belongs.
- **Enabling the setting did nothing until you pressed Refresh.** Ticking "Show public IP location" saved the preference and stopped there. The row now shows "Locating..." on the click that gives consent, and the lookup runs immediately instead of waiting for an unrelated manual refresh.
- **Turning the setting off left the location on screen.** A refresh already in flight could still hand over a populated identity, and it was displayed even after the opt-in was withdrawn. The opt-in is now checked before the data is used, so the location clears the moment the box is unticked.
- **Public IP and geolocation failures were completely silent.** Both paths swallowed the exception, so "Unavailable" with an empty tooltip was indistinguishable from a fetch that had not run. Both are now written to the log.

### Added

- The Location row states which of three things is true: the setting is off, the lookup failed, or the location itself. These are different problems with different fixes and were previously indistinguishable.

### Tests

- `ThePublicIpLocationIsReportedOnScreen` asserts the location is on the same always-visible surface as the address, at all 66 tested window sizes, and that it clears when the opt-in is withdrawn. A tooltip-only implementation fails it.

## 1.16.4 - 2026.10

### Fixed

- **Overlapping controls in the Safety and status section.** `CreateCard("Safety and status", 1, 6, ...)` created a 6-row body but `SetBodyRows` supplied 7 heights after the geolocation checkbox was added. The 7th row had no RowStyle, causing the last control to overlap the one above it. Fixed by updating `CreateCard` to accept 7 rows. A new `NoControlIsClippedAtAnySize` layout test now catches any control that falls entirely outside the form bounds at any tested size.

## 1.16.3 - 2026.10

### Fixed

- **Public IP geolocation: ipwhois.io was returning 404 for every request, so the feature was silently failing.** Switched to ipinfo.io (50k requests/month free without auth, HTTPS, returns all needed fields). The ASN is extracted from the `org` field (e.g. "AS15169 Google LLC" → ASN "AS15169", ISP "Google LLC").

## 1.16.2 - 2026.10

### Added

- **Public IP geolocation (opt-in).** When enabled, the app sends your public IP to ipwhois.io (HTTPS, no API key, 10k req/month free, GDPR compliant) and displays country, region, city, ISP, ASN, and timezone in the Public IP tooltip. Off by default for privacy; the checkbox "Show public IP location (sends IP to ipwhois.io)" is in the Safety card. The setting is persisted and synced across launches. The geolocation is fetched on each refresh when the IP changes, and cached for the session. If the lookup fails, the IP is still shown without location data.

## 1.16.1 - 2026.09

### Fixed

- **Start with Windows now works.** It never has. The registration was a value in the Run key, and MacRando's manifest requires administrator, which Explorer cannot satisfy for a Run key entry. Windows recorded the attempt and its completion in the same second with PID 0, meaning no process was ever created. On the machine this was found on: 10 attempts across 3 days, all silent, no error dialog, and nothing in MacRando's log because the application never started to write one.

  Start with Windows is now a scheduled task at logon registered to run with the highest privileges, which is the mechanism Windows provides for an application that legitimately needs elevation. It starts silently, with no prompt at every logon. The dead Run value is removed when the task is registered, and on uninstall, because a machine upgrading from an earlier version still carries one.

  The setting can no longer claim something untrue. If the task cannot be created the toggle reports the failure rather than showing a tick beside a setting that did nothing, and at startup a missing registration is recreated rather than the setting being quietly turned off, which would have made every upgrading user lose their existing choice on first launch.

- **The installer launches MacRando after install.** A postinstall entry runs once the wizard has closed and Setup is back on the signed-in user's token, and the default launcher is CreateProcess, which cannot start a process whose manifest asks for a higher integrity level. It failed with "CreateProcess failed; code 740. The requested operation requires elevation" and showed the user an error instead of the application. It now launches through ShellExecute, which honours the manifest. No test ran the installer, and a silent install skips postinstall entries entirely, so this only ever appeared for a person installing interactively.

- **The installer's start with Windows option registers the scheduled task** rather than writing a Run value, and the uninstaller removes it. The uninstaller uses schtasks rather than PowerShell, because Inno reads braces in a parameter value as a constant reference, so a try/catch one-liner does not compile.

### Notes

- Registration passes the installation path through the environment rather than interpolating it into a command, since that directory is user-visible and can contain spaces or a quote. Checking whether the task exists reads a single file rather than running schtasks, because it is asked on every startup and a process spawn taking hundreds of milliseconds on the application's constructor path is a real cost for a question with a cheap answer.

## 1.16.0 - 2026.09

### Fixed

- **The version in "Apps & features" now updates when MacRando updates itself.** Windows reads the version in Add or remove programs from the `DisplayVersion` value in the uninstall registry key, and only the installer writes that key. The updater replaced the executable and nothing else, so after an in-app update the Settings list carried on showing whichever version the installer last wrote, permanently. It is a cosmetic string and the application was working, so nothing reported it.

  The elevated update helper now publishes the installed version after it swaps the file. The value is read back out of the installed executable rather than taken on trust from the environment, so a mismatch publishes what is actually on disk. Both rollback paths put the version back with the binary, so a reverted install does not advertise a build that is no longer there.

  Deliberately forgiving, because it is a label in a settings list. A copy run from a ZIP has no uninstall entry and must not grow one, so the write is skipped when the key is absent, and a failure is logged rather than raised rather than refusing to finish an install over a version string.

  The Inno Setup `AppId` is now a constant in the application with a test comparing it against `installer.iss`. The two are edited separately and a mismatch is silent by nature: the write targets a key that does not exist, is skipped, and the version stays stale exactly as before.

- **The update helper no longer reports publishing a version it did not publish.** The registry write relied on the script-wide `ErrorActionPreference` to make a denied write terminating. Without that, a denied write is a non-terminating error, the catch never ran, and the helper went on to log success. Found by running the real extracted helper unelevated against the real key. The call now asks for a terminating error itself.

## 1.15.0 - 2026.09

### Added

- **The tray context menu follows the dashboard's dark mode.** It was the last surface with no theme at all and stayed light next to a dark dashboard. A `ToolStripMenuItem` is a `ToolStripItem`, not a `Control`, so none of the dashboard's theming reached it and the whole menu needed a custom renderer.

  The status line needed its own values, not just a background colour. It was `SystemColors.ControlText` when ready, which is near-black and invisible on a dark menu, and its amber was tuned for a light surface. Each state now has a light and a dark value: ready, needs-restoring, and the not-elevated red that means the app cannot act at all. The three are asserted to stay visually distinct from each other, because a status line that reads the same in every state is worse than no colour.

  Every foreground and background pairing the menu can produce is contrast-checked in both modes, 158 checks in total. Disabled items are drawn at a deliberate readable dimness rather than the system default, which assumes a light surface. High contrast still overrides the dark preference, as everywhere else in MacRando.

- **The tray adapter submenu respects the new adapter classification.** A virtual machine or tunnel adapter is listed with its reason, and its three randomize actions are disabled, matching the dashboard rather than differing from it for the same adapter.

### Fixed

- **A silent WinForms trap in the tray theme.** Assigning a custom `Renderer` puts a `ToolStrip` into `Custom` render mode, and then assigning `RenderMode.Professional` replaces that renderer with a stock `ToolStripProfessionalRenderer` drawing a light palette. The menu then looks almost right, because item foregrounds are set separately and still apply, while the background stays white. Only the renderer is assigned now, and a test asserts the mode is `Custom` and the renderer is the tray one.

### Notes

- ProtonVPN, which this build's user runs, is covered explicitly: its Windows client installs a WireGuard adapter, so the interface may be named `ProtonVPN`, `Proton VPN`, `ProtonVPN Secure Core` or `ProtonVPN (IKEv2)`, and its driver string may be a WireGuard driver, a Wintun driver, or empty depending on version. All of those classify as a tunnel and are left alone, while the physical adapter beside them stays fully usable. ProtonVPN is not installed on the machine these tests run on, so this is covered by unit tests against those names rather than against a live adapter.

## 1.14.0 - 2026.09

### Added

- **Virtual machine and tunnel adapters are recognised and left alone.** The adapter list used to filter with `Get-NetAdapter -Physical and not .Virtual`, which drops most virtual adapters but misses the ones that matter: a Hyper-V, VMware or VirtualBox host adapter, and a third-party tunnel such as WireGuard, Cloudflare WARP or Tailscale, all register a genuine NDIS miniport and are not flagged at all. They arrived in the list looking exactly like hardware, and randomising one either does nothing useful or disrupts something MacRando does not own and cannot put right.

  Adapters are now classified as physical, virtual machine, tunnel or other virtual, from the driver strings and the media type. Nothing is hidden: an off-limits adapter stays in the list, labelled and greyed, with the reason in its tooltip and in the detail pane. An adapter that silently disappears gives a user no way to tell it was excluded on purpose or why.

  The refusal is enforced at the point of change rather than only in the interface, because the network-change watcher has no interface to disable. A preset records an adapter key, and that key can later belong to a tunnel after a driver change or a re-enumeration, so auto-apply now checks and skips with a logged reason. Restoration is deliberately not blocked: MacRando must always be able to put back something it changed.

- **The query-only PowerShell scripts are now run for real by the test suite.** They were only ever checked as text, which cannot catch a syntax error. `Get-NetAdapter` runs live on the machine and its payload must parse and name every adapter; the per-adapter scripts, which refuse to run without a GUID, are parse-checked with PowerShell's own parser instead. The adapter listing and the combined snapshot are then compared, and they must report the same adapter count. Checked because these scripts run at startup, so a mistake in one means the app shows no adapters on every machine.

### Changed

- **One PowerShell launch per refresh instead of two.** The adapter list and the VPN profile list were two sequential process launches for two independent questions. Folding them into one script halves the process starts, and the public IP is still fetched on its own so a dashboard refresh does not wait on a third-party service. If the combined call fails, the refresh falls back to the two separate calls rather than losing the adapter list, so an unanswerable VPN query cannot cost the user their adapters. Measured on this machine: about 150ms saved per refresh, with a test that fails if it ever becomes a regression.

- **The NetworkAddress lookup is hoisted out of the per-adapter loop.** `Get-NetAdapterAdvancedProperty -AllProperties` enumerates the advanced properties of every adapter on the machine, and it was being called once per adapter, re-scanning the whole set N times to pick out a single row. It is now called once and keyed by interface prefix. This was the most expensive query in a refresh.

## 1.13.2 - 2026.09

### Fixed

- **The updater could not fetch the update manifest. This is why "Check for updates" did nothing.** From 1.11.0 onward the update client refused every HTTP 3xx, on the reasoning that a redirect could downgrade the transfer to plain HTTP. The reasoning is sound but it was wrong about how GitHub works: a release-asset URL answers `302` pointing at a signed, expiring blob URL, as a matter of design. So every build from 1.11.0 to 1.13.1 threw on its own manifest URL and could never see a release. The error even advised setting the manifest URL to the final address, which is not possible to do by hand because that address carries a short-lived signature.

  Redirects are now followed by hand, with the property that actually mattered kept: a hop that would leave HTTPS is refused, and the chain is capped. Integrity never depended on this alone, since the download is checked against both the SHA-256 and the Authenticode signer named in a manifest that was itself fetched over TLS.

  The old test asserted that every 3xx was refused, so it locked the bug in. It now covers what GitHub really sends, refuses every downgrade scheme including `file://` and `ftp://`, accepts a relative location resolved against the HTTPS base, and a new test fetches the live published manifest end to end, so a URL that stops resolving, stops redirecting, or redirects off HTTPS fails the suite.

  Builds 1.11.0 to 1.13.1 cannot be updated by the updater, because the bug is in the code doing the updating. 1.13.2 has to be installed once by hand; the updater works from here.

## 1.13.1 - 2026.09

### Fixed

- **The test suite was writing to the real state file.** A test that constructs a `TrayContext` registers an `Application.Idle` handler that performs a genuine refresh, and the tests pump the message loop, so a context left on the default data root re-read and re-wrote the user's real `state.json`. This was introduced in 1.9.0 when the tray menu tests were added. It was not visible as a test failure, and the only symptom was the pending-profile count in the real state file oscillating during a test run. `TrayContext` now takes an optional data root, the tests use a temporary directory, and a test asserts the real state file is left byte-for-byte identical.
- **The updater's refusal was a bare error dialog.** The install guard refuses while any restore profile is pending, because installing closes MacRando and the profile must be resolved first. That is correct, but it named the condition rather than the thing blocking it, so a user hitting it had no way to tell which adapter to restore. The refusal now names the adapters and says the banner can be used to resolve them.

## 1.13.0 - 2026.09

### Added

- **A changed MAC address can now be kept instead of restored.** A **Keep change** button appears on the pending-restore banner whenever the selected adapter has a MAC change that can be kept. MacRando is built so everything is reversible, which left no way to say "yes, leave that one", and the only way to get a permanent address was to make the change outside the app with no record of the original.
- **Keeping is entirely optional and per-change.** The default is unchanged: exiting still restores, and nothing becomes permanent unless you press the button. There is deliberately no global switch that disables automatic restore, because that would remove the guarantee the application is built around for the sake of one case.
- **The original address is recorded before the profile is discarded.** `KeptMacRecords` in the state file holds the hardware address alongside the one that was kept, capped at 50 entries, newest first. Keeping therefore does not mean forgetting: the way back stays available inside MacRando, and **Restore original** continues to work because it reads the driver's stored value rather than a profile.
- **Keeping applies to the MAC address only.** An IP change left in place after a reboot tends to break connectivity, so those are always restored. If one operation changed both, keeping the MAC half is allowed and the IP half is still restored, which the confirmation says before anything happens.
- The confirmation shows both the current and original address, states that the change will survive restarts and reboots, defaults to **No**, and is refused outright while the restore data is unreadable.

### Fixed

- The Keep button was left without an accessible name. It is hidden until a keepable change exists, so the pass that names the banner's on-demand buttons never reached it, and it would have appeared unlabelled in exactly the state where it first becomes visible. Caught by the accessibility test walking the live control tree in both banner states.

### Tests

- Thirty-three assertions covering the refusals rather than the happy path, because keeping is the one action MacRando cannot undo. Covered: a profile with no changes, an IP-only change, a profile that does not record which address was applied, a profile that does not record the original (which would make keeping a one-way door with no record), and a missing profile. Each asserts the refusal explains itself, since a silent refusal is worse than a visible one.
- The record is asserted to hold the original, to stay one entry per adapter across repeated keeps, to survive a second keep of the same adapter, to be capped with the newest kept and the oldest dropped, and to tolerate null entries from a hand-edited state file.

## 1.12.0 - 2026.09

### Added

- **A real installer.** `MacRando-<version>-setup.exe` installs MacRando to Program Files, with Start menu and optional desktop shortcuts, a proper uninstaller, and the license, changelog, README, and the certificate trust script installed alongside the executable.
- **The installer is signed, not only the executable inside it.** It is the file a user downloads and runs, so an unsigned setup would show Windows' unknown-publisher warning with no publisher at all, which is a worse first impression than the self-signed build it installs.
- **Per-machine install.** MacRando already requires administrator to run, so there is no privilege argument for a per-user install, and putting the executable in Program Files means it is not writable by anything running as the signed-in user. That removes the class of local tampering where the executable itself is replaced.
- An optional task offers to **trust the signing certificate for the current user**, which suppresses the unknown-publisher warning. It is off unless explicitly ticked, because adding a certificate to a trusted store is a decision about the machine rather than about the application, and only the current user's store is touched. The public certificate is installed either way, so the included `trust-certificate.ps1` still works for anyone who declines.
- An optional task registers **Start with Windows** using the same registry key the application writes itself, so the installer's choice and the application's setting cannot disagree. The uninstaller removes that key regardless of how it was added.

### Changed

- The version is no longer written into `installer.iss`. It is passed in from `src\Models.cs`, which is now the single source of truth for every artifact name, so the installer cannot claim a different version from the executable it installs. The previous template sat at 1.4.4 while the application moved on through nine releases; that is not possible now.
- The `LicenseFile` wizard page is gone. Inno Setup only accepts a `.txt` or `.rtf` there, and the Apache-2.0 text ships verbatim as `LICENSE` rather than being duplicated into a second file that could drift from it.
- `package.ps1` skips the installer with a clear warning when Inno Setup is not installed, rather than failing, so a contributor without it can still build and package.

### Safety

- **Uninstalling never touches user data.** `%LOCALAPPDATA%\MacRando` holds the restore profiles, and a profile deleted while an adapter is left randomized is the one failure this application is built to avoid. The uninstaller says where the data was left and why it was kept.
- The installer closes a running MacRando before installing, because the updater swaps the executable and an open file cannot be replaced.

### Fixed

- Several path and directive errors in the installer template that had prevented it from building at all: the file references resolved outside the project, the registry root used the long form where Inno wants `HKCU`, a line beginning with a `#` character was read as a preprocessor directive, and `WizardImageFile` was given an icon where a bitmap is required.

### Tests

- The installer was installed and uninstalled for real, in a throwaway directory, and the results checked: every required file arrives, the installed executable is byte-identical to the packaged one and still carries the expected signature, an unselected certificate task does not touch the trusted store, an unselected startup task does not write the Run key, the uninstaller removes the installation, and the user's data directory survives intact with all 14 of its files.

## 1.11.1 - 2026.09

### Security

- **A verified update is re-verified immediately before it is installed.** The download sits in `%TEMP%\MacRando\updates`, a per-user folder that any process running as the same user can write to, and the verification happened in the main process while the copy is performed later by an elevated helper. Anything running as you could replace the file in that window, and the helper would copy whatever it found into the elevated install target. The SHA-256 is now recomputed in the helper, as close to the copy as possible, and the install is refused on a mismatch.
- The hash is passed to the helper through `MACRANDO_SHA256` alongside the other paths, and a missing hash is a refusal rather than a skip, so the check cannot be bypassed by simply not supplying one.
- `CanInstall` performs the same re-check when the install is authorised, so a file that has already been replaced is reported as changed rather than discovered to be bad after the user has committed to restarting.
- The comparison is against the hash the manifest recorded, which is the value that was verified against those exact bytes. A file replaced with something signed by a different key is still refused.

### Tests

- Two new end-to-end cases in the updater test, which run the real helper script against a stub. One replaces the verified download with different bytes after the hash was taken and confirms the target executable is left byte-for-byte untouched and the log names the hash mismatch as the reason. The other supplies no hash and confirms the install is refused.
- Three assertions added to the install guard, including that the refusal explains the download changed rather than reporting a generic failure.

## 1.11.0 - 2026.09

### Security

- **The update manifest URL and the expected signer thumbprint are now encrypted.** These two values together are the trust anchor for code execution: MacRando runs elevated, so anything able to rewrite the manifest URL could aim an update at a server of its choosing. The Authenticode check limits what that achieves, since the download still has to be signed by the expected thumbprint, but the redirect itself should not be writable by anything that is not the user. They now live in `settings.json.trust`, DPAPI-protected against the current user with entropy tied to the product and field names.
- **The clear text copies are removed from `settings.json` on the next save.** The remaining preferences stay in plain text deliberately: they have no security consequence, and encrypting them would make them unreadable to someone trying to work out why something is misbehaving.
- **Redirects are no longer followed when fetching the manifest or the download.** The scheme check already refused a plain `http` URL, but a server answering `https` with a `302` to `http` would have downgraded the transfer *after* that check passed, which is the position an active network attacker wants. Any 3xx is now treated as a failure, with the status in the message so a user pointing the manifest at a link-shortener is told what happened. The refusal is set on the HTTP handler rather than on `ServicePointManager`, because the latter is process-wide and would change behaviour for every other request in the process.
- An existing plaintext settings file is honoured on load and migrated on the next save, so upgrading does not silently disable update checks for someone who had configured them. Verified against a copy of a real pre-upgrade settings file: every preference survived, the two trust values moved, and the resulting trust file discloses neither the host nor the thumbprint.
- An unreadable trust file falls back to the plain values with a warning rather than refusing to start, because refusing would leave no route back short of deleting a file the user may not know exists.

### Tests

- Thirty-three assertions. The important ones assert the *absence* of a leak: the encrypted file must not contain the manifest URL, the thumbprint, or even the host name, and the plain settings file must no longer contain either value after a save. An earlier version of the envelope did leak the URL, because the clear values were still public properties on the type being serialized; the test caught it and the record written to disk is now a separate type.
- Garbage, truncated blobs, and a blob carrying a foreign format marker are all refused rather than throwing, and a delete-the-plain-file case is covered so a user clearing settings does not silently lose where updates come from.
- The redirect test asserts a *default* handler follows redirects, so the check that the update path refuses them cannot pass for the wrong reason, and confirms a 200 is not refused.

## 1.10.0 - 2026.09

### Added

- **Accessible names and descriptions throughout the notification center and the notification popups.** The 1.8.0 pass covered the dashboard only, which left the two surfaces a user is most likely to meet first without accessible naming. This closes that gap.
- The **update settings fields** are the reason this mattered beyond tidiness. The manifest URL and the expected signer thumbprint are both values a user pastes from somewhere else, and a bare edit box says neither what it wants nor what happens if the thumbprint is wrong. Their descriptions now state that an update is refused unless the downloaded file matches both the hash and the signer.
- The **quiet-hours spinners** are each named for which end of the range they are. Two bare numeric boxes sitting side by side tell a screen reader user nothing about what they are setting.
- **High contrast in the notification center and popups.** Both previously kept their own colours regardless. Under high contrast the custom styling is replaced rather than layered over, so the system draws the borders, and the footer text becomes full contrast rather than the deliberately muted shade that is the first thing to become unreadable.
- The popup's close button is named **Close notification**. Its visible text is a multiplication sign, which reads as "times" or nothing at all, so the name has to carry the meaning. It is also last in the tab order, after the visible Dismiss button, since reaching a second identical control first would be a trap.

### Tests

- Twenty-three more assertions, walking the live notification center and a live popup and requiring an accessible name on every interactive control, skipping only `NumericUpDown`'s internal edit child, which is a private type with no visible text to name it from. The spinner owner is asserted named instead.
- The popup's tab order is asserted specifically for Dismiss preceding the close glyph, since that relationship is the reason the ordering exists.
- The high contrast path is asserted to handle every control type present in the center, so a control added later without a case in the pass fails the build rather than quietly keeping the app's colours on a high contrast display.
- The dashboard's high contrast test already asserted its own negative case, that the custom styling still applies when high contrast is off. The center's equivalent asserts the same baseline, so a check that passed because the pass did nothing could not pass silently.

## 1.9.0 - 2026.09

### Changed

- **The tray menu is now grouped into labelled sections.** It was a flat list of twenty-four items with no structure, so the two things a user opens MacRando to do, viewing an adapter and restoring a pending one, sat a dozen unrelated diagnostics and startup settings apart. The order is now: current state, then the primary actions, then a "Reports (read-only)" group, then application items, then startup settings, then About, License, and Exit last. Group titles are non-clickable menu items, so they cannot be mistaken for an action or clicked into doing nothing.
- **The status line reports the pending restore count.** It previously read only "Status: ready (administrator)", which said nothing about whether the machine was in the state the user left it in. A pending restore is now called out in the text, not just implied by a menu item being enabled, because it is the only MacRando state with a consequence for the machine rather than just for the app. The count is in the words rather than the colour alone, since colour is invisible to a screen reader and to anyone who cannot distinguish amber from grey.
- The status line is coloured amber when something needs restoring and red when administrator permission is missing. Not permanently coloured on purpose: a status line that is always coloured teaches a user to ignore the colour.
- **The tray tooltip reports the pending count**, since it is often the only MacRando text visible at all. It stays inside the 63-character limit the notification area enforces.

### Fixed

- The menu's `BuildMenu` had been left in a state where the item wiring and the item order were interleaved through a single flat list. Reordering it by hand meant finding the right line in a long block and risking dropping an item, so the two concerns are now separate: one place declares every item and wires it, another places them.

### Tests

- The tray menu is now covered. It is read by reflection because the menu is built in the constructor and would otherwise need a live `ApplicationContext` with a real `NotifyIcon`.
- Every item the old flat menu had is asserted to still be reachable, so a reorganisation cannot silently remove a feature. The test failed on its first run against a stale item name, which is the class of mistake worth catching.
- The ordering itself is asserted: the version and status come first, the primary actions sit above the reports and settings groups, and Exit is last.
- Section headers are identified by bold font rather than by their text, because a real report item also ends in a parenthesis and matching on that would either miss a header or flag a report. The test also asserts a real action is not styled as a header, which is the failure a bold-everything refactor would introduce.
- The status wording is tested for singular and plural forms, for the permission case outranking the pending count, for the count appearing in the text, and for the tooltip staying inside the notification area's length limit.

## 1.8.0 - 2026.09

### Added

- **Accessible names and descriptions for every interactive control.** MacRando previously set no accessible name anywhere in the codebase, so a screen reader reached the public IP address, the adapter list, the MAC entry box, and all fourteen action buttons as unlabelled controls announcing only their type. The names now say what the control acts on, and the dangerous ones carry a description of the consequence: that a restore profile is saved before a change, that DHCP consent is per operation and never stored, that a VPN is the supported way to change the public IP, and that a passwordless sign-in is the supported way to restore. That information was on screen for sighted users and had no route to anyone else.
- **An explicit tab order** following the reading order of the page: search and filter, the adapter list, then the actions for the selected adapter. Traversal previously followed the order controls happened to be created and added, which is layout plumbing rather than reading order, so a keyboard user could land on Apply before the box whose contents it applies. The pending restore banner's Restore all leads the order when it is present, because it is the control a user reaches for when an adapter has been left changed.
- **High contrast support.** The application used a hard-coded palette throughout, so a user who enables high contrast gets MacRando's colours overriding the system's. Every colour now comes from `SystemColors`, the dark mode toggle is ignored because the user's stated need outranks a preference they cannot see, flat custom button styling is undone so the system draws the borders, and the deliberately low-contrast secondary text collapses to full contrast, since a muted colour is the first thing to become unreadable for someone who turned high contrast on.

### Fixed

- **The pending restore banner's buttons had no accessible name, and were never named at all.** They are created by a layout helper and the banner is hidden until a restore is outstanding, so naming them once at startup was not enough. They are now named whenever the banner appears, and a lookup that finds no matching button is logged rather than failing silently, because a renamed button would otherwise leave an unlabelled control on screen with nothing to indicate it.

### Tests

- Sixty-one assertions. The high contrast palette is checked to contain no colour that is not a `SystemColors` member, so it cannot drift into a hand-picked value, and the live form is checked with high contrast forced on to confirm the palette is actually applied to a button, a label, a panel, and a text box rather than only being computed and never used.
- The high contrast test asserts its own negative case: outside high contrast the flat style and the application's own colours must still apply, so a check that passed because the method did nothing could not pass silently.
- The naming test walks the live control tree in both banner states and in both themes, and reports the parent of anything unnamed. The first run of it found three unnamed buttons, which is how the banner defect above was found.

## 1.7.0 - 2026.09

### Fixed

- **Log rotation never deleted anything.** The log was moved aside at 1 MB and left there, so rotation freed nothing and the folder grew by a megabyte per rotation forever. A user with a failing adapter in a loop would accumulate archives indefinitely. The newest 5 archives are now kept and older ones removed.
- **Diagnostic bundles accumulated without bound.** The `diagnostic-bundles` folder was never pruned by anything, and each bundle is a few hundred kilobytes. The newest 10 are kept and older ones removed as new bundles are written.
- **Two log rotations inside the same second could lose the log.** Archive names used second resolution, so a second rotation in the same second targeted an existing file, the move threw, and the `catch` swallowed it. Names now carry milliseconds.

### Added

- `RetentionPolicy`, with the ordering rules separated from the deletion so they can be tested without a filesystem. Files are ordered by last-write time then by name, so a tie is broken deterministically instead of by whatever order the filesystem enumeration returned, which matters because the enumeration is not stable and a tie otherwise risks pruning the same file twice.
- Retention refuses any name containing invalid path characters, so a file name can never escape the directory it was selected from. The current log is never a candidate: it has no timestamp in its name and so never matches the archive pattern.

### Tests

- Twenty-nine assertions covering the ordering, the limits, oldest-first deletion, deterministic tie-breaking, and the real deletion against temporary directories, including that a file held open by another process is skipped while the deletable ones beside it are still removed. A file that cannot be deleted is left alone rather than turned into a startup failure.

## 1.6.1 - 2026.09

### Fixed

- **The two on-demand dialogs showed the generic Windows application icon.** The read-only report dialog, used for diagnostics, the IP preflight, and the device tracking report, and the preset editor, were both created as a bare `Form`, which WinForms fills in with `SystemIcons.Application`. They now use the MacRando icon, loaded once and shared with the dashboard so all three agree. The icon is loaded lazily and disposed with the tray context, and a failure to load it leaves the dialogs working rather than blocking the report, since the report is often the thing explaining a problem.

### Verified

- The fix was checked by comparing rendered pixels rather than object identity: the default form icon and the icon the dialogs now load hash differently, and a dialog with the handle created renders the same artwork as the loaded MacRando icon. Object identity alone would not have been sufficient, because a bare `Form` reports a non-null icon, which makes a naive "is it null" check look like there was nothing to fix.

## 1.6.0 - 2026.09

### Added

- **Inspect device tracking identifiers (read-only)**, a tray item that reports what Windows keeps on the machine that can be used to identify it, and what each item means. It changes nothing, and offers no way to change anything, which is the point: the identifiers are not switches, so nothing here honestly deserves a button.
- The report covers the Global Device ID in both documented forms, the Microsoft account identity entries for the current account, the connected-devices token cache, the identity negative cache, the connected-devices data folder, the diagnostic data level, the customer experience program, the per-user advertising ID, and the machine GUID. Every read is independent, so one denied key costs that line rather than the whole report, and HKLM is read through the 64-bit view so the answer does not depend on whether the process started as 32-bit.
- The diagnostic data level is interpreted against the Windows edition rather than merely repeated. `AllowTelemetry = 0` is honoured only on Enterprise, Education, and Server; on Pro and Home it is treated as `1`, so required data including hardware inventory, crash reports, and update status still leaves the machine. Editions outside the enterprise family are reported as still sending that floor, which is deliberately the pessimistic reading, because overstating what a setting achieves would be the worse error for a report whose purpose is to be believed.
- The report is included in the diagnostic bundle as `device-tracking.txt`, with its two conclusions summarised in the bundle summary.
- `GdidKind.Classify` distinguishes the 16-character hex local LID, the `g:`-prefixed global form, and anything else. A value that is present but not a documented shape is reported as unrecognized rather than guessed at, and the machine GUID is flagged as leave-alone with the reason, because it is a machine identity used for activation and DPAPI rather than a tracking handle.

### Fixed

- **The first version of the report inferred a Microsoft account state from a registry cache, and contradicted itself.** With no `g:` cache it said the absence was "consistent with not being signed in with a Microsoft account", one line after reporting that an account is signed in. The cache is populated by its own service, so its absence says nothing about account state. The report now states the absence and says explicitly that it is not evidence the machine is untracked, and the signed-in identity count is what answers the account question.
- **A disabled advertising ID was reported as stored.** The check asked whether the key held any value, and the key holds the `Enabled` flag itself, so a machine with the advertising ID switched off reported a stored identifier. It now looks for the `AdvertisingId` value by name.

### Tests

- Sixty-two assertions covering identifier classification, including that free text, wrong lengths, and non-hex values are never treated as identifiers; that the identifier never appears in clear text in the rendered report or the bundle; that no account address is written out; and the edition table for the diagnostic data level, covering Enterprise, Server, Education, Professional, Home, Core, an unknown edition, an unset value, and an undocumented level.
- One test pins the self-contradiction above directly, so a future reword of the same wrong inference fails the build.

## 1.5.0 - 2026.09

### Added

- **Network-aware presets.** A preset can be bound to a specific network, and optionally apply itself when you join that network. This is the first feature that can change an adapter without a confirmation dialog, so it is opt-in twice: binding a preset records only where it belongs, and a separate checkbox arms it.
- The network is identified read-only from the Wi-Fi SSID when there is one, otherwise the Windows connection profile name, combined with the default gateway. Including the gateway is deliberate: any two guest networks called `Free WiFi` must not be treated as the same place. The key is lowercased and whitespace-trimmed so casing and padding differences between sources cannot silently stop a match, and the separator is stripped from names so a crafted network name cannot impersonate another key.
- Automatic apply is refused while a restore profile is pending, while the restore data cannot be read, more than once every 10 minutes per preset, for IP randomization on a DHCP adapter, and when two presets are bound to the same network with automatic apply enabled. Each refusal is logged with its reason and the resulting network is named.
- `NetworkIdentity` and `NetworkAutoApply` are a pure decision layer with no UI or network access, so the rules above are tested exhaustively rather than through the interface.
- The preset list shows the bound network next to the preset name, so a binding is visible without opening the editor.
- The diagnostic bundle summary records the current network key, the last network key seen, and how many presets are network-bound and armed, so "the preset did not run" is answerable from the bundle alone.

### Changed

- A preset applied automatically still goes through the normal backup, verification, and restore workflow, and is recorded in notification history with a distinct title.
- The network baseline is recorded during the initial refresh without acting on it. Without this, the first network change after launch would look like an arrival and would fire a bound preset on the network already in use.
- Network events are debounced and marshalled to the UI thread. Windows raises them repeatedly while a connection is still settling, and acting on each one in turn would churn the adapter.
- `AdapterPreset` gained `AdapterKey`, derived from the preset key rather than stored separately, so no new state has to be migrated.

### Fixed

- **A new PowerShell query failed silently and reported an unknown network.** The scripts return their result through a `Write-MacRandoJson64` marker, because PowerShell's default formatting turns a bare `[pscustomobject]` into a table. The first version of the network-identity script emitted the object directly, so the marker never appeared, the parse failed, and the error was swallowed into a harmless-looking "unknown network". The first symptom was that the feature could never match anything.
- **A preset on cooldown silenced every other preset bound to the same network.** The scan returned at the first refusal, so one preset's cooldown suppressed the rest. All bound presets are now considered, and the cooldown belongs to a single preset.
- **Two presets armed for one network resolved by dictionary order.** That is a guess about what the user meant, so it is now refused outright and reported.

### Tests

- Forty-nine assertions covering network key construction (SSID preference, identically named networks, casing and padding, separator injection, missing parts), the decision matrix (every refusal, cooldown including a backwards clock jump, the two-preset conflict, and a preset with a bind flag but no key), preset key parsing, and display of the bound network.
- Presets written by 1.4.x have none of the network fields and load as unbound, so an old preset cannot accidentally match whatever network the machine happens to be on.
- A new script-validation check requires every script that returns a result to emit it through `Write-MacRandoJson64`. This is the bug above, and it was invisible because the mock runner hands back whatever the test wants rather than what PowerShell would actually print. The check was confirmed by breaking the script on purpose and watching it fail.

## 1.4.4 - 2026.09

### Changed

- **Unreadable restore data is now a loud, actionable failure instead of a generic error.** This is the worst failure MacRando can have, because a previous session may have left an adapter randomized and the saved profile is exactly what cannot be read. When no restore-data file can be read, MacRando now shows a persistent notification that says what happened, what it means, and what to do, including resetting a possibly randomized adapter in Windows or restarting the computer.
- **Network changes are blocked while the restore data is unreadable.** A profile that cannot be read back is not a safety net, so MacRando will not change an adapter until the problem is resolved. The refusal explains why and points at the details.
- The incident is recorded to a plain-text file next to the state, because notification history lives inside the encrypted state that could not be read. The original files are never modified or deleted.
- The tray menu gains **Restore data problem...** while the problem is active, so the guidance stays reachable after the notification is dismissed.
- The diagnostic bundle now includes the problem report and flags the condition in its summary, instead of reporting zero pending profiles and implying everything was fine.
- Refreshing the dashboard no longer repeats the restore-data error as a generic message every time.

### Added

- `RestoreStateUnreadableException` with a per-file status list, so the error can explain which of `state.json`, `state.json.tmp`, and `state.json.bak` was present, readable, or corrupt, and why.

### Tests

- Twelve assertions covering the corrupt-restore-data path: the specific exception is raised rather than a generic one, every candidate file is described, the details are actionable, a fresh install with no state file is not treated as an error, and a corrupt primary file still falls back to the backup silently.
- One test assumption was wrong and corrected: the backup copy only exists from the second save onward, because `File.Replace` needs an existing destination to move aside. The first save creates `state.json` only.

## 1.4.3 - 2026.09

## 1.4.3 - 2026.09

### Fixed

- **The updater could roll back a perfectly good build.** The watchdog treated a missing startup marker as failure. A new build that was alive but slow to start, for example while antivirus scanned a freshly written executable, was killed and replaced with the previous build. The helper now stops waiting early only when the new process has actually exited, and after the watchdog window it falls back to process liveness: a running build is kept and the slowness is logged, and only a build that has exited without reporting success is rolled back. A build that hangs without ever starting is indistinguishable from a slow start here, so it is left in place with the backup kept for manual recovery.
- **`publish-update.ps1` could write a manifest for the wrong build.** When the release archive was missing, which happens when the build step fails, the script only warned and then wrote a manifest labelled with the requested version but describing whatever executable happened to be on disk. A user could have been offered a stale binary as a newer release. A missing archive is now a hard failure, and the script additionally refuses to write a manifest unless the executable's own file version matches the requested version.

### Tests

- The update install helper end-to-end test is now part of the repository and runs in CI, instead of being a throwaway script. It extracts the real generated helper script, checks it parses as PowerShell, and runs it against a stub executable for all three outcomes: a build that reports success is installed, a build that crashes on launch is rolled back, and a slow but running build is kept rather than wrongly rolled back.
- New script validation tests cover the PowerShell that MacRando sends to Windows, which until now was never parsed or checked in any test. They drive the real service methods through a capturing runner and assert that every environment variable each script reads is actually supplied by the C# side. Because the scripts take their inputs through the environment rather than string interpolation, a typo such as `MR_NEW_IP` versus `MR_NEW_IPP` would compile cleanly, pass every mock test, and then silently read an empty value during a live change. The tests report 12 scripts, 56 assertions, and 33 environment variables.

### Reported, not changed

The script validation reports three environment variables that the C# side supplies but no script reads: `MR_ADAPTER_NAME`, `MR_GATEWAY`, and `MR_ORIGINAL_GATEWAY`. The scripts read the current gateway from `Get-NetIPConfiguration` themselves and verify it against `MR_EXPECTED_GATEWAY`, which is how the existing default route is preserved. The unused variables are harmless leftovers, left in place rather than churn working restore code, and are now surfaced on every test run so they cannot be forgotten silently.

## 1.4.2 - 2026.09

## 1.4.2 - 2026.09

### Fixed

- **The updater could be told "up to date" when a newer release already existed.** The manifest was hosted as a file on `main` and served through a CDN that lags a few minutes behind a push. Immediately after publishing 1.4.1, the committed manifest read 1.4.1 while the URL still returned 1.4.0, so a user on 1.4.0 would have been told there was nothing to install. The recommended manifest URL is now the release asset at `releases/latest/download/update.json`, which only changes when a release is published, and the client sends `Cache-Control: no-cache` for the manifest request.

## 1.4.1 - 2026.09

## 1.4.1 - 2026.09

### Changed

- **Check for updates is now a single command.** It checks the manifest, downloads the release, verifies the SHA-256 and the Authenticode signer, and installs and restarts, with no intermediate step for the user to click through. Previously it stopped at a report with an *Install and restart* button.
- The redundant **Install update and restart** tray item was removed, because it did the same work as the update check.
- When there is nothing to install, the result is a notification rather than a modal report window. A report window is still shown when the updater is unconfigured, or when a verified download cannot be installed yet, because those cases need an explanation the user can act on.
- Installing still asks for confirmation once, because it closes MacRando and replaces the executable. Everything up to that point is automatic.

## 1.4.0 - 2026.09

## 1.4.0 - 2026.09

### Added

- **A real updater.** A verified update can now be installed and restarted in place from the update report, or straight from the tray with **Install update and restart**. Previously the app only downloaded and verified the file and left the replacement to the user.
- The installer runs as a short-lived helper process, because a running executable cannot overwrite its own image. The helper waits for MacRando to exit, keeps a backup of the current build, swaps in the verified download, and starts the new build.
- **Automatic rollback.** The new build writes a startup marker once it has genuinely started. If the marker never appears, the helper stops the unresponsive process, restores the previous build, and relaunches it, so a user is never left with an executable that will not start.
- A helper log at `%LOCALAPPDATA%\MacRando\update-install.log` records every step, and the previous build is kept at `%LOCALAPPDATA%\MacRando\MacRando.previous.exe`.

### Safety rules enforced before anything is replaced

An install is refused, with the reason shown in the report, when:

- the download was not verified by both SHA-256 and the Authenticode signer;
- the verified download is no longer on disk;
- an adapter restore profile is still pending, since installing closes MacRando;
- an adapter, restore, or VPN operation is running;
- the manifest version is not newer than the installed version;
- no update manifest URL is configured.

Paths are passed to the helper through environment variables rather than a command line, so nothing has to be quoted or can be tampered with in transit. The helper inherits elevation from MacRando, so it can replace an executable installed under Program Files.

### Fixed

- The generated helper script was invalid PowerShell and would have failed on first use. `$` had been escaped with PowerShell backticks inside a C# verbatim string, where a backtick is a literal character, so every variable reference was emitted escaped. The script is now validated by the PowerShell parser.

### Tests

- Eleven assertions covering the install guard, including the safety-critical cases: refused while a restore profile is pending, refused while busy, refused for a downgrade or the same version, refused when unverified or missing, and allowed only for a verified newer build.
- An end-to-end test that extracts the real generated helper script, checks that it parses, and runs it against a stub executable for both outcomes: a healthy build is installed, and a build that never reports a successful start is rolled back.

### Deliberate limitation

Rollback triggers on a build that fails to *start*, not on a later crash. A new build that starts and then fails during an adapter refresh is left in place, because rolling back the executable in response to a transient network error would be worse than the problem it solved.

## 1.3.1 - 2026.09

## 1.3.1 - 2026.09

### Fixed

- The details pane is now clipped by a real scroll host. A `TableLayoutPanel` does not clip its children, so the MAC, network, and VPN cards previously drew on top of the Safety and status card on a short window, including button-on-button collisions such as *Notification center* overlapping *Randomize MAC + IP*. Content now scrolls instead of overlapping.
- The overlap audit used for regression checks now compares controls in absolute form coordinates rather than parent-relative bounds, so a control overflowing into a different container can actually be detected. It also covers `Label` controls and parent-boundary overflow, and runs across window sizes and DPI scales of 1.25x, 1.5x, and 2.0x in both themes.
- The release workflow now fails loudly when the release it just created does not exist, instead of reporting success after a silent no-op.

### Still open

The reported overlap in the adapter list area could not be reproduced. The audit reports zero overlaps in that region across 980x620, 1120x720, and 1400x900 in both themes and at 1.25x, 1.5x, and 2.0x scale. A screenshot or the names of the two overlapping elements would identify it.

## 1.3.0 - 2026.09

## 1.3.0 - 2026.09

### Safety and recovery

- **Restore all pending profiles** - one recovery action that restores every adapter with a saved restore profile, one at a time, verifying each before starting the next. This is the recovery path after a crash, forced termination, or power loss. The tray item shows the pending count and disables itself when nothing is pending.
- **Pending-restore banner** - the dashboard shows a banner whenever any adapter still has a restore profile, with a direct **Restore all** button.
- **Structured retry metadata** - notifications now store the operation kind (MAC, IP, or both), whether the MAC was randomized or entered manually, and the requested address. Retry no longer parses the human-readable action text. Entries written by 1.2.0 and earlier still load and fall back to the old parsing path, which is treated as untrusted.
- **Send test notification** - a notification-center button that exercises popups, sound, grouping, and history without touching the network.

### Adapter usability

- Adapter **favorites**, persisted per interface GUID, listed first and marked with a star.
- Adapter **search** by name or hardware description.
- **Connected only** filter, persisted.
- New **Adapter details** card showing driver description, interface index, `NetworkAddress` property support, and the interface GUID MacRando uses to target the adapter.

### Diagnostics and history

- **IP change history** - every local IP change records the original and proposed address, prefix, DHCP state, gateway, outcome (applied, verified, rolled back, failed), and whether it ran at startup. Addresses are masked before storage, like all other history.
- **Preflight diff** - the read-only IP preflight now lists what would change and what would be preserved: address, prefix, DHCP, gateway, DNS, default routes, and MAC.
- **Diagnostic bundle export** - a timestamped ZIP containing a summary, the diagnostics report, the IP preflight, notification history, IP change history, the last 400 log lines, and the license. No clear-text MAC or IP addresses are included.

### Distribution and trust

- The full license text is **embedded in the executable**, so the in-app license viewer works offline. The dashboard footer, About dialog, tray menu, and notification center all state the license.
- **Public certificate export** - exports only the public `.cer`. The private key is never exported.
- **`trust-certificate.ps1`** - opt-in helper that installs the public certificate into `TrustedPublisher` for the current user or the machine. It does nothing without `-Install`, never writes to `Root`, and never handles a private key.
- **`package.ps1 -Portable`** - produces an unzipped, runnable folder with the public certificate, the trust helper, and plain-language instructions.
- **`release.ps1`** - one command that tests, builds, signs, packages, verifies the compiled version matches the requested version, writes the update manifest, and prints the release checklist.
- **Upgrade regression tests** - 138 assertions covering older state and settings files, legacy retry fallback, and history normalization.

### Fixed

- Refreshing or filtering the adapter list no longer discards the current selection.
- A UTF-8 encoding regression that could corrupt non-ASCII characters in the dashboard source was repaired.


## 1.2.0 — 2026-09

### Added

- Read-only IP preflight reporting with proposed address, DHCP, gateway, and DNS plans.
- Integrated IP preflight results in the live change confirmation.
- Automatic rollback when a saved network change fails before it can be verified.
- Actionable notification center with searchable history, severity filters, and retry/restore actions.
- In-app notification popups with burger icon, live operation updates, and non-overlapping stacking.
- Notification preferences for sound, duration, quiet hours, and duplicate collapsing.
- Optional upgrade-manifest infrastructure for future verified release checks.

### Fixed

- DHCP adapters no longer fail when Windows removes the lease before the explicit address cleanup.
- IP changes preserve the existing default route instead of creating duplicate routes.
- Restore scripts tolerate missing addresses and preserve route state.
- Startup notifications no longer overwrite saved restore profiles before state loading completes.

### Security and distribution

- The release remains Authenticode-signed with the local self-signed development certificate.
- Windows will show an untrusted-publisher warning until the public certificate is explicitly trusted on the target machine.
- Restore state and notification history remain DPAPI-protected for the current Windows user.
