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

## Recovery after a crash or forced termination

If MacRando is killed, crashes, or loses power mid-change, the saved restore profile survives. Recover with either of these:

- The **pending-restore banner** on the dashboard, which appears whenever any adapter still has a profile.
- **Restore all pending profiles** in the tray menu. It restores each adapter one at a time and verifies the result before starting the next, then reports which adapters succeeded and which still need attention.

A routine uninstall deliberately leaves restore profiles alone. See the uninstall section below.

## Adapter list

- **Search** filters by adapter name or hardware description.
- **Connected only** hides disconnected adapters.
- **Star selected** marks an adapter as a favorite. Favorites are stored per interface GUID, listed first, and shown with a star. An adapter that disappears and returns keeps its favorite status because the GUID is the key.
- The **Adapter details** card shows the driver description, interface index, whether the driver exposes the `NetworkAddress` property, and the interface GUID MacRando uses to target the adapter.

## History and diagnostics

**View IP change history** in the tray menu lists recent local-IP changes with the original and proposed address, prefix, DHCP state, gateway, and outcome: applied, verified, rolled back, or failed. Addresses are masked before they are written to disk, exactly like notification history.

**Export diagnostic bundle** writes a timestamped ZIP to `%LOCALAPPDATA%\MacRando\diagnostic-bundles` containing a summary, the diagnostics report, the IP preflight, notification history, IP change history, the last 400 log lines, and the license. MAC and IP addresses are masked, so a bundle is safe to attach to a bug report.

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

If the marker never appears, the helper stops the unresponsive process, restores the previous build, and launches it again. A failed update therefore leaves you with a working MacRando rather than a broken executable. Every step is recorded in:

```text
%LOCALAPPDATA%\MacRando\update-install.log
```

Install is refused, with the reason shown in the report, when the download is not fully verified, when a restore profile is still pending, when an adapter, restore, or VPN operation is running, or when the manifest version is not newer than the installed version.

Rollback covers a build that fails to *start*. A build that starts and later fails during an adapter refresh is left in place, because replacing the executable over a transient network error would cause more harm than it solves.

The manifest for this repository is served from:

```text
https://raw.githubusercontent.com/SpaceJamp/MacRando/main/update.json
```

`raw.githubusercontent.com` is only reachable anonymously for a **public** repository. If the repository stays private, the in-app updater cannot read the manifest and an unauthenticated HTTPS host is required instead.

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

The state file and its backup are encrypted with Windows DPAPI for the current user. Legacy plaintext state files are migrated automatically on first load. A backup copy, `state.json.bak`, is kept when possible. The tray menu includes **Open restore-data folder**, **Open logs folder**, and **Run read-only diagnostics**.

The diagnostics report checks adapter discovery, the selected adapter's `NetworkAddress` support, IP/DHCP visibility, elevation, and VPN profile enumeration without changing adapter settings.

## License

Copyright (c) 2026 SpaceJamp

MacRando is licensed under the [Apache License, Version 2.0](LICENSE). The full license text is in `LICENSE` and is also included in every release archive and installation, as required by section 4(a) of the license.

Unless required by applicable law or agreed to in writing, the software is provided on an **AS IS** basis, without warranties or conditions of any kind. MacRando modifies live network adapter settings and requires administrator rights, so you are responsible for testing it on a network you are permitted to reconfigure and for complying with your local policies and applicable laws.

## Project layout

- `src/NetworkService.cs` — adapter discovery, Windows PowerShell commands, IP selection, VPN actions, and public-IP lookup.
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
- `test.ps1` / `tests` — non-network mock-provider, settings, and upgrade-regression tests.
