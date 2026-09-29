# MacRando Changelog

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
