# MacRando Changelog

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
