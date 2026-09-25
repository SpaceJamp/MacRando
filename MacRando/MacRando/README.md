# MacRando

MacRando is a TMAC-inspired Windows GUI application, with an optional system-tray shortcut, for changing the identity of a **physical** network adapter. The dashboard uses a two-pane layout with a physical-adapter list on the left and MAC, IP, VPN, and safety controls for the selected adapter on the right.

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

## Project layout

- `src/NetworkService.cs` — adapter discovery, Windows PowerShell commands, IP selection, VPN actions, and public-IP lookup.
- `src/TrayContext.cs` — tray menu and operation workflow.
- `src/NotificationPopup.cs` — non-activating in-app notifications with the application icon.
- `src/NotificationCenterForm.cs` — searchable notification history and notification preferences.
- `src/DashboardForm.cs` — small dashboard window.
- `src/StateStore.cs` — DPAPI-protected persistent restore profiles and presets.
- `src/DiagnosticsService.cs` — read-only diagnostics and preflight reporting.
- `src/AppLogger.cs` — local, sanitized diagnostic logging.
- `src/AppSettings.cs` — startup and display preferences.
- `src/PowerShellRunnerService.cs` — injectable PowerShell runner boundary.
- `assets/` — supplied multi-size application and tray icons.
- `tools/GenerateBurgerIcons.cs` — reproducible icon generation from the supplied source image.
- `build.ps1` — reproducible Windows build script with optional Authenticode signing.
- `package.ps1` — versioned release archive builder with optional signing.
- `sign.ps1` — SHA-256 Authenticode signing helper.
- `installer.iss` — optional Inno Setup installer template.
- `test.ps1` / `tests` — non-network mock-provider regression tests.
