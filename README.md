# MacRando

MacRando is a TMAC-inspired Windows GUI application, with an optional system-tray shortcut, for changing the identity of a **physical** network adapter. The dashboard uses a two-pane layout with a physical-adapter list on the left and MAC, IP, VPN, and safety controls for the selected adapter on the right.

Repository: <https://github.com/SpaceJamp/MacRando>

Releases and the update manifest are published from that repository.

MacRando is inspired by the general concept of MAC-address randomizing tools such as TMAC. It is an independent implementation and is not affiliated with, endorsed by, or derived from any other project.

It can:

- Generate and apply a locally administered, unicast random MAC address.
- Apply a specific unicast MAC address entered in the GUI, with the same verification and restore workflow.
- List all physical adapters, including disconnected Wi-Fi adapters, with current and permanent MAC addresses visible.
- Select an apparently unused IPv4 address in the adapter's current subnet and apply it as a temporary static address.
- Save the original MAC, DHCP/static state, gateway, DNS servers, and address so they can be restored.
- Dark mode with a persistent GUI preference.
- Run a read-only diagnostics/preflight report from the tray menu.
- Show non-activating in-app status notifications with the application icon; click a notification to open the dashboard.
- Save per-adapter action presets and apply them through the same confirmation/backup workflow.
- Bind a preset to a specific network and optionally apply it automatically when you join that network, with consent required and several refusals built in.
- On exit, automatically restore verified changes made during the current session without prompting.
- Show the current public IP by querying `api.ipify.org`.
- Connect or disconnect Windows VPN profiles that already exist in Windows Settings. A VPN is the supported way to change the public Internet IP; local software cannot assign a different public IP by itself.

## Build

The project intentionally uses the .NET Framework compiler included with Windows, so a Visual Studio installation or the .NET SDK is not required.

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

For a debug build:

```powershell
.\build.ps1 -DebugBuild
```

Run the non-network mock-provider regression tests with:

```powershell
.\test.ps1
```

The executable is written to `bin\MacRando.exe`.

To create a versioned release archive:

```powershell
.\package.ps1
```

For an unzipped, runnable folder that includes the public certificate and the trust helper:

```powershell
.\package.ps1 -Portable
```

To run the whole release in one command (tests, build, sign, package, manifest, checklist):

```powershell
.\release.ps1 -Version 1.3.0 -SignThumbprint <CERTIFICATE-THUMBPRINT> -Portable
```

`release.ps1` refuses to continue if the requested version does not match `AppInfo.Version`, and it verifies the compiled file version after the build so a release cannot ship a binary that reports a different version than the tag.

## Code signing

`sign.ps1` applies a SHA-256 Authenticode signature to a built Windows executable. Signing is explicit; builds do not select a certificate implicitly.

For a certificate already installed in `CurrentUser\My`, pass its thumbprint:

```powershell
.\build.ps1 -SignThumbprint <CERTIFICATE-THUMBPRINT>
.\package.ps1 -SignThumbprint <CERTIFICATE-THUMBPRINT>
```

For a locally generated development certificate, create one in the current user's store and use its thumbprint:

```powershell
$cert = New-SelfSignedCertificate `
  -Type CodeSigningCert `
  -Subject 'CN=MacRando Development' `
  -CertStoreLocation 'Cert:\CurrentUser\My' `
  -KeyAlgorithm RSA `
  -KeyLength 3072 `
  -HashAlgorithm SHA256 `
  -KeyUsage DigitalSignature `
  -KeyExportPolicy NonExportable `
  -NotAfter (Get-Date).AddYears(2)
$thumbprint = $cert.Thumbprint
.\build.ps1 -SignThumbprint $thumbprint
```

A self-signed signature is useful for local testing only. Windows will report the certificate as untrusted and may show **Unknown publisher** until the public certificate is explicitly trusted on the test machine. For public distribution, use an OV or EV Authenticode certificate from a trusted CA and keep the private key in a certificate store or hardware-backed signing service. Do not commit `.pfx`/`.p12` files or passwords to this project.

A trusted release certificate should also be timestamped:

```powershell
.\package.ps1 -SignThumbprint <CERTIFICATE-THUMBPRINT> -TimestampServer 'http://timestamp.digicert.com'
```

Verify the embedded signature with:

```powershell
Get-AuthenticodeSignature .\bin\MacRando.exe
```

The ZIP contains the signed executable, but Authenticode applies to the executable rather than to the ZIP archive itself.

The executable uses the supplied burger artwork as its embedded Windows icon. Separate transparent assets are generated at `assets\MacRando.ico` and `assets\MacRandoTray.ico`.

Startup preferences are stored at:

```text
%LOCALAPPDATA%\MacRando\settings.json
```

An optional Inno Setup template is included at `installer.iss`.

## Run

1. Double-click `bin\MacRando.exe`.
2. Approve the Windows administrator prompt. Adapter changes require elevation.
3. The MacRando dashboard opens automatically. Choose a physical adapter and one of the actions.
4. The notification-area icon is also available for quick access after the window is closed.
5. Verified changes made during the current session are restored automatically when you choose **Exit**. If an automatic restore fails, MacRando stays open and reports the error.
6. Use the **Dark mode** checkbox in the upper-right corner; the choice is saved locally.
7. Leave DHCP IP randomization disabled unless you explicitly accept the address-conflict risk.
8. Optional startup settings are available from the tray menu and the Safety & Status card: **Start minimized to tray**, **Start with Windows**, and **Randomize MAC on startup (risky)**. The last option is disabled by default, requires explicit confirmation, targets the selected adapter by GUID, and is skipped whenever a restore profile is already pending. When enabled, startup randomization runs immediately without a countdown, and the saved adapter configuration is restored automatically when you choose **Exit**. Startup never changes anything unless that explicit option was enabled.
9. Open **Notification center** from the tray or Safety & Status to search recent notifications, filter by severity, copy details, open the affected adapter, or retry/restore an operation. Popup actions include **Open dashboard**, **Restore now**, **Retry**, **Diagnostics**, and **Dismiss**.
10. Run **IP preflight (read-only)** before a live local-IP change. It reports the current and proposed address, prefix, DHCP plan, gateway route plan, DNS plan, and a per-setting list of what would change versus what would be preserved, without changing adapter settings.

## Tray menu

The notification-area menu is grouped so the items you reach for are not buried:

| Group | Contains |
|---|---|
| *(top, not clickable)* | version, status line, public IP |
| *(primary actions)* | Open dashboard, restore a saved adapter, restore all pending, Adapters, VPN profiles |
| **Reports (read-only)** | Refresh, diagnostics, IP preflight, device tracking, IP change history, diagnostic bundle |
| **Application** | Check for updates, Notification center, restore-data folder, logs folder |
| **Startup** | Start minimized to tray, Start with Windows, Randomize MAC on startup |
| *(bottom)* | About, License, export signing certificate, Exit |

Group titles are disabled menu items, so they look like headings and cannot be clicked.

The **status line** is the one piece of text that needs no click to be useful, so it carries the state that decides what you do next:

- `Ready` — nothing outstanding.
- `Ready - 2 adapters need restoring` — amber, and the count is in the words as well as implied by the colour. This is the only state with a consequence for your machine rather than just for the app, because it means an adapter is not in the configuration you left it in.
- `Administrator permission required` — red, and it takes priority over the pending count, since nothing can be done about either without elevation.

The same information appears in the tray tooltip, which is often the only MacRando text visible at all.

## Recovery after a crash or forced termination

If MacRando is killed, crashes, or loses power mid-change, the saved restore profile survives. Recover with either of these:

- The **pending-restore banner** on the dashboard, which appears whenever any adapter still has a profile.
- **Restore all pending profiles** in the tray menu. It restores each adapter one at a time and verifies the result before starting the next, then reports which adapters succeeded and which still need attention.

A routine uninstall deliberately leaves restore profiles alone. See the uninstall section below.

## Accessibility and high contrast

Every interactive control carries an accessible name, and the ones that change something carry a description of the consequence. A button reading only "Randomize local IPv4 address" does not convey that a restore profile is saved first, or that the DHCP consent is per operation and never stored, so those facts are attached to the control rather than left to be inferred from the visible label. This covers the dashboard, the notification center, and the notification popups.

The two fields where this matters most are the update manifest URL and the expected signer thumbprint. Both are values you paste from elsewhere, and the description states that an update is refused unless the downloaded file matches both the hash and the signer.

Tab order follows the reading order of the page, not the order the controls happen to be constructed. The pending restore banner's **Restore all** leads when it is visible. In a notification popup, the visible **Dismiss** button precedes the corner close glyph, because the two do the same thing and reaching a duplicate first would be a trap.

High contrast is honoured. Every colour comes from `SystemColors` so the user's own choice is what renders, the dark mode toggle is ignored while it is active, the flat custom button styling is undone so the system draws the borders, and the low-contrast secondary text becomes full contrast.

## Adapter list

- **Search** filters by adapter name or hardware description.
- **Connected only** hides disconnected adapters.
- **Star selected** marks an adapter as a favorite. Favorites are stored per interface GUID, listed first, and shown with a star. An adapter that disappears and returns keeps its favorite status because the GUID is the key.
- The **Adapter details** card shows the driver description, interface index, whether the driver exposes the `NetworkAddress` property, and the interface GUID MacRando uses to target the adapter.

## Network-aware presets

A preset can be bound to the network you are on when you save it, and can then apply itself when you join that network later. This is off unless you turn it on.

**Binding.** Saving a preset offers *Bind this preset to the current network*, which records a key built from the Wi-Fi SSID when there is one, otherwise the Windows connection profile name, plus the default gateway. The gateway is included on purpose: two different networks can share a name, such as any two guest networks called `Free WiFi`, and they should not be treated as the same place. The binding is shown next to the preset in the list. Binding only records where the preset belongs; it does not arm it.

**Automatic apply.** *Apply automatically when this network appears* is a separate, explicit opt-in. When it is on, MacRando watches for network changes and applies the preset without asking, because there is nobody at the keyboard to ask.

It will not do this:

- while a restore profile is pending, because a new change would compound one already in flight;
- while the restore data cannot be read, because a profile that cannot be read back is not a safety net;
- more than once every 10 minutes per preset, so an unstable gateway cannot make it churn the adapter;
- for IP randomization on an adapter using DHCP, because that consent is per operation and is never stored in a preset;
- if more than one preset is bound to the same network with automatic apply enabled, because picking one would be a guess.

Anything it does change still goes through the normal backup, verification, and restore workflow, and each automatic apply is recorded in notification history.

**Startup is separate.** Binding a preset has no effect at launch. Startup randomization remains its own setting, and startup randomization still never runs while a restore profile is pending.

## Device tracking identifiers

**Inspect device tracking identifiers (read-only)** in the tray menu reports what Windows keeps on the machine that can be used to identify it, and what each item actually means. It changes nothing, and there is deliberately no way to change anything from it.

The report covers:

- the **Global Device ID**, in both documented forms: the plain 16-character hex local LID, and the `g:`-prefixed global form;
- the **Microsoft account identity entries** for the current account, as a count only. No account address is written to the report, because the report is meant to be attachable to a bug report;
- the connected-devices platform token cache, the identity negative cache, and the connected-devices data folder;
- the **diagnostic data level** (`AllowTelemetry`), which location set it and which one wins, and what the value means *on this Windows edition*;
- the customer experience program, the per-user advertising ID, and the machine GUID.

Identifiers are masked. A value present but not in a documented shape is reported as unrecognized rather than guessed at.

Three things the report tries hard not to let you get wrong:

- **An identifier is a cache, not a switch.** The value on disk is what the platform fetched. Deleting it does not withdraw it, and it can be written back on the next connection to Microsoft, so a manual edit can look like it worked while doing nothing.
- **`AllowTelemetry = 0` is not "off" on Pro or Home.** Microsoft treats it as `1` there, so required data, including hardware inventory, crash reports, and update status, still leaves the machine. Only Enterprise, Education, and Server honour it fully. The report says which case you are in rather than repeating the setting value.
- **The machine GUID is not a telemetry handle.** It is a machine identity used for activation, user SID creation, and DPAPI, and regenerating it is a documented way to break activation. The report flags it as leave-alone.

The report is also included in the diagnostic bundle as `device-tracking.txt`, and its two conclusions are summarised in the bundle summary.

## History and diagnostics

**View IP change history** in the tray menu lists recent local-IP changes with the original and proposed address, prefix, DHCP state, gateway, and outcome: applied, verified, rolled back, or failed. Addresses are masked before they are written to disk, exactly like notification history.

**Export diagnostic bundle** writes a timestamped ZIP to `%LOCALAPPDATA%\MacRando\diagnostic-bundles` containing a summary, the diagnostics report, the IP preflight, notification history, IP change history, the last 400 log lines, and the license. MAC and IP addresses are masked, so a bundle is safe to attach to a bug report. The summary also records the current network key, the last network key seen, and how many presets are network-bound, because those decide whether a network-aware preset can match at all.

## What MacRando keeps on disk

Everything lives under `%LOCALAPPDATA%\MacRando`. The footprint is bounded, so a long-running install does not grow without limit:

| What | Bound |
|---|---|
| `macrando.log` | live log, rotated at 1 MB |
| `macrando-<timestamp>.log` | newest **5** archives kept, older removed |
| `diagnostic-bundles\*.zip` | newest **10** kept, older removed |
| `state.json` | one file, with a `.bak` alongside |
| `*-latest.txt` | overwritten each run, one copy of each |

Rotation alone is not retention: moving a file aside frees nothing, so the archives are actively deleted. A file that cannot be deleted, for instance one another process holds open, is left in place rather than treated as an error.

Nothing is written outside that folder, and nothing is uploaded anywhere.

## Trusting the development certificate

Release builds are signed with a self-signed development certificate, so Windows shows an unknown-publisher warning until that certificate is trusted on the machine running the build.

1. Export the public certificate from the tray menu: **Export signing certificate (public)**. Only the public `.cer` is written; the private key never leaves the Windows certificate store.
2. Install it, after reading the script:

```powershell
# dry run first: prints what would happen and changes nothing
powershell -NoProfile -ExecutionPolicy Bypass -File .\trust-certificate.ps1

# trust the publisher for the current user
powershell -NoProfile -ExecutionPolicy Bypass -File .\trust-certificate.ps1 -Install

# or for every user on this machine (needs an elevated session)
powershell -NoProfile -ExecutionPolicy Bypass -File .\trust-certificate.ps1 -Install -Store LocalMachine
```

The script installs into `TrustedPublisher` only. It never writes to `Root`, so the certificate cannot become a general trust anchor, and it does nothing at all without `-Install`. Remove it again with `-Remove -Install`.

This is a convenience for testing. Public distribution still needs an OV or EV certificate from a trusted CA, and SmartScreen reputation warnings may continue until then.

## Optional update checks

The tray menu includes **Check for updates**. Configure an HTTPS update manifest URL and expected signer thumbprint in **Notification center** first. The manifest must contain `Version`, `DownloadUrl`, `Sha256`, and `SignerThumbprint` fields. MacRando downloads a newer release to a temporary folder and verifies its SHA-256 hash and Authenticode signer before it will do anything with it. The latest result is written to:

```text
%LOCALAPPDATA%\MacRando\updates-latest.txt
```

The manifest URL and the expected signer thumbprint are stored **encrypted** in `settings.json.trust`, DPAPI-protected against your Windows user account. They are the trust anchor for code execution, because MacRando runs as administrator, so anything that could rewrite them could point an update at a server of its own choosing. The remaining preferences stay in plain text: they have no security consequence and should be readable when you are trying to work out why something is misbehaving.

Upgrading from an earlier version is automatic. A plaintext settings file is still honoured on load and the two values move into the encrypted file the next time MacRando saves its settings.

Redirects are **not followed** when fetching the manifest or the download. A server answering `https` with a redirect to `http` would otherwise downgrade the transfer after the scheme check had already passed, so any 3xx is treated as a failure and the status is shown. If you point the manifest URL at a link shortener, you will be told rather than silently redirected.

Updates are checked twice. The manifest URL and download are verified when you run **Check for updates** (SHA-256 plus the Authenticode signer, matched against your expected thumbprint), and then the SHA-256 is **re-verified by the elevated helper immediately before the copy**. That second check exists because the download sits in a per-user temp folder that any program running as you can write to, and the copy happens later in a different process. A download that no longer matches is refused, and the install is abandoned rather than performed.

## Installing

Two forms are published for every release:

- **`MacRando-<version>-setup.exe`** — the installer. Installs to `Program Files`, adds a Start menu entry and uninstaller, and closes a running MacRando first. It offers two optional tasks, both off unless you tick them: trusting the signing certificate for your account, and starting MacRando with Windows.
- **`MacRando-<version>.zip`** — the portable folder. Unzip and run; nothing is installed.

The installer is per-machine because MacRando already requires administrator to run, so there is no privilege gained by installing per-user, and the executable in `Program Files` is not writable by anything running as your account.

**Uninstalling does not delete your data.** `%LOCALAPPDATA%\MacRando` holds your settings, restore profiles, notification history, and logs, and a restore profile may still describe an adapter that has not been put back. The uninstaller tells you where it left the data and why.

If you chose not to trust the certificate during install, the public certificate and `trust-certificate.ps1` are installed alongside the executable, so you can do it later.

### Installing an update

**Check for updates** is a single command. It fetches the manifest, downloads a newer release, verifies its SHA-256 hash and Authenticode signer, and then installs and restarts. There is no intermediate step.

If there is nothing to install, you get a notification. If a verified download cannot be installed yet, for example because an adapter restore profile is still pending, the report window explains why.

Installing asks for confirmation once, because it closes MacRando and replaces the executable. Everything up to that point is automatic.

A running executable cannot overwrite its own image, so MacRando hands the work to a short-lived helper process that:

1. waits for MacRando to exit,
2. copies the current build to `%LOCALAPPDATA%\MacRando\MacRando.previous.exe`,
3. copies the verified download over the executable,
4. starts the new build, and
5. watches for a startup marker the new build writes once it has genuinely started.

If the marker never appears, the helper checks whether the new process is still running. A build that is alive but slow to start, for example while antivirus scans the newly written file, is kept rather than replaced. Only a build that has actually exited without reporting success is rolled back: the helper restores the previous build and launches it again, so a failed update leaves you with a working MacRando rather than a broken executable. Every step is recorded in:

```text
%LOCALAPPDATA%\MacRando\update-install.log
```

Install is refused, with the reason shown in the report, when the download is not fully verified, when a restore profile is still pending, when an adapter, restore, or VPN operation is running, or when the manifest version is not newer than the installed version.

Rollback covers a build that fails to *start*. A build that starts and later fails during an adapter refresh is left in place, because replacing the executable over a transient network error would cause more harm than it solves. A build that hangs without ever starting looks the same as a slow start, so it is also left in place, with the previous build kept at the path above for a manual recovery.

The manifest is published as an asset on each GitHub release. Point the updater at the **latest release** copy rather than a file on `main`:

```text
https://github.com/SpaceJamp/MacRando/releases/latest/download/update.json
```

A `raw.githubusercontent.com` URL pointing at `main` also works, but branch files are served through a CDN that can lag a few minutes behind a push. During that window a user would be told they are up to date while a newer release already exists. The release asset only changes when a release is actually published, and the client also sends `Cache-Control: no-cache` for the same reason.

### Publishing a release

1. Bump the version in `src\Models.cs` (`AppInfo.Version`), `src\AssemblyInfo.cs`, `package.ps1`, and `installer.iss`.
2. Build, sign, and package locally, because the signing certificate is not on GitHub-hosted runners:

```powershell
.\test.ps1
.\package.ps1 -Version 1.2.1 -SignThumbprint 8804295F8D8615DC1D137720847672ABA1342FEC
```

3. Generate the update manifest. The script refuses to write one unless the artifact is signed and, when `-ExpectedThumbprint` is supplied, the signer matches:

```powershell
.\publish-update.ps1 -Version 1.2.1
```

4. Commit `update.json` and push, then create a `v1.2.1` tag. Pushing the tag also triggers `.github/workflows/release.yml`, which runs the tests, builds an **unsigned** archive, and creates the release.
5. Upload the signed `MacRando-1.2.1.zip` and `update.json` to that release so the download URL in the manifest resolves.

Automated release artifacts are clearly marked `-unsigned` so they can never be mistaken for a signed build.

## Uninstall behavior

`installer.iss` uses a single `AppId`, so a new version installs over the previous one and the uninstaller removes the installed program files and shortcuts. User data is intentionally left in place:

```text
%LOCALAPPDATA%\MacRando
```

That preserves `settings.json`, the DPAPI-protected `state.json` restore profiles, notification history, and logs. A routine uninstall must never discard a pending restore profile. The template does not delete that folder; a future "also remove user data" option should be an explicit, separate opt-in rather than automatic cleanup.

## Notifications

Notifications are shown as non-activating in-app popups with the MacRando icon. They update live while an adapter, restore, or VPN operation is running. Critical restore errors remain visible until dismissed and expose retry/restore actions.

Notification preferences are available in the Notification center:

- Show or hide popups
- Enable or disable sounds
- Collapse repeated notifications and group them by adapter
- Configure popup duration
- Set quiet hours

The most recent 100 notifications are stored in the DPAPI-protected restore state. Notification history is searchable and can be copied as text; opening an entry can deep-link to its adapter. Notifications never contain raw MAC or IP addresses because history and diagnostic text are sanitized before storage.

The executable embeds an application manifest requesting administrator rights. Windows may display a UAC prompt each time it is started.

## Important limitations

- A local/private IP is not a public IP. Changing it does not anonymize traffic on the Internet.
- DHCP local-IP randomization is disabled by default. The explicit DHCP consent checkbox is required because a random address can still conflict with another device, a DHCP lease, or a network policy. When enabled, MacRando performs repeated best-effort ARP checks and verifies the address after applying it, but no local program can reserve an address atomically on every network. Use a trusted network and keep the restore profile.
- Adapter changes are matched by the Windows interface GUID, not by a mutable adapter name. Restore profiles are deleted only after the resulting MAC/IP state has been verified.
- The IP action is intentionally limited to adapters with exactly one active preferred IPv4 address. Multi-homed adapters are left unchanged.
- The MAC action depends on the driver exposing the standard `NetworkAddress` advanced property. Some Wi-Fi drivers, virtual adapters, policies, or managed devices may reject it.
- Windows VPN profiles must be configured beforehand in Windows Settings. MacRando does not create VPN accounts, select random VPN servers, or bypass network policy.
- The public-IP lookup is an external HTTPS request to `api.ipify.org`; the displayed value is informational and may be unavailable while the network is changing.

## Restore data and diagnostics

Restore profiles are stored per user at:

```text
%LOCALAPPDATA%\MacRando\state.json
```

The state file and its backup are encrypted with Windows DPAPI for the current user. Legacy plaintext state files are migrated automatically on first load. A backup copy, `state.json.bak`, is kept when possible, and the newest readable copy is used automatically. The tray menu includes **Open restore-data folder**, **Open logs folder**, and **Run read-only diagnostics**.

### If the restore data cannot be read

This is the most serious thing that can go wrong, because a previous session may have left an adapter randomized and the saved profile is exactly what MacRando cannot read. When no restore-data file can be read:

- A persistent notification explains what happened and what to do. Nothing is deleted, and the files on disk are not modified.
- **Network changes are blocked.** A profile that cannot be read back is not a safety net, so MacRando will not touch an adapter until this is resolved.
- The incident is written to `restore-data-problem-<timestamp>.txt` in the data folder, because notification history lives inside the encrypted state that could not be read.
- **Restore data problem...** appears in the tray menu while the condition lasts, so the guidance stays reachable after the notification is dismissed.
- The diagnostic bundle includes the report and flags the condition.

If an adapter is currently randomized, reset its MAC in Windows or restart the computer, because MacRando cannot restore it for you. If the files are intact but still unreadable, they were most likely written by a different Windows user: DPAPI-encrypted data cannot be read by another account. Do not delete the files; they may be recoverable.

The diagnostics report checks adapter discovery, the selected adapter's `NetworkAddress` support, IP/DHCP visibility, elevation, and VPN profile enumeration without changing adapter settings.

## License

Copyright (c) 2026 SpaceJamp

MacRando is licensed under the [Apache License, Version 2.0](LICENSE). The full license text is in `LICENSE` and is also included in every release archive and installation, as required by section 4(a) of the license.

Unless required by applicable law or agreed to in writing, the software is provided on an **AS IS** basis, without warranties or conditions of any kind. MacRando modifies live network adapter settings and requires administrator rights, so you are responsible for testing it on a network you are permitted to reconfigure and for complying with your local policies and applicable laws.

## Project layout

- `src/NetworkService.cs` — adapter discovery, Windows PowerShell commands, IP selection, VPN actions, and public-IP lookup.
- `src/NetworkAutoApply.cs` — network identity and the pure decision logic for network-bound presets.
- `src/DeviceTrackingService.cs` — read-only device tracking and diagnostic data inspection.
- `src/RetentionPolicy.cs` — bounded on-disk footprint for rotated logs and diagnostic bundles.
- `src/Accessibility.cs` — accessible names, roles, and the high-contrast palette.
- `src/TrayMenuState.cs` — the tray menu's status wording, colour, and tooltip.
- `src/UpdateTrust.cs` — DPAPI protection for the update manifest URL and signer thumbprint.
- `src/TrayContext.cs` — tray menu and operation workflow.
- `src/NotificationPopup.cs` — non-activating in-app notifications with the application icon.
- `src/NotificationCenterForm.cs` — searchable notification history and notification preferences.
- `src/DashboardForm.cs` — small dashboard window.
- `src/AppLogger.cs` — local, sanitized diagnostic logging.
- `src/LicenseInfo.cs` — embedded license text and notice strings.
- `src/StateStore.cs` — DPAPI-protected persistent restore profiles, presets, and history.
- `src/DiagnosticsService.cs` — read-only diagnostics and IP preflight reporting with a planned-change diff.
- `src/UpdateService.cs` — optional HTTPS manifest update checks with hash/signature verification.
- `src/UpdateInstaller.cs` — verified install and restart, with a helper process and automatic rollback.
- `src/AppSettings.cs` — startup, display, notification, and adapter-view preferences.
- `src/PowerShellRunnerService.cs` — injectable PowerShell runner boundary.
- `assets/` — supplied multi-size application and tray icons.
- `tools/GenerateBurgerIcons.cs` — reproducible icon generation from the supplied source image.
- `build.ps1` — reproducible Windows build script with optional Authenticode signing.
- `package.ps1` — versioned release archive builder with optional signing and `-Portable` folder output.
- `release.ps1` — one-command release: test, build, sign, package, manifest, and checklist.
- `trust-certificate.ps1` — opt-in public-certificate trust helper for the TrustedPublisher store.
- `publish-update.ps1` — generates and verifies the `update.json` release manifest.
- `CHANGELOG.md` — release notes and change history.
- `LICENSE` — Apache License 2.0.
- `sign.ps1` — SHA-256 Authenticode signing helper.
- `installer.iss` — optional Inno Setup installer template.
- `.github/workflows/ci.yml` — build and mock-test checks on every push and pull request.
- `.github/workflows/release.yml` — tag-triggered test, build, and unsigned release staging.
- `test.ps1` / `tests` — non-network mock-provider, settings, upgrade-regression, contrast, update-helper end-to-end, and PowerShell script validation tests.
