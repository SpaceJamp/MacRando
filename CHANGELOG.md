# MacRando Changelog

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
