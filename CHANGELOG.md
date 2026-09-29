# MacRando Changelog

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
