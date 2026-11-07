# Security fixes

This document explains the security problems found in Lenovo Legion Toolkit v2.36.0.0 and how this fork addresses them. The fixes are numbered by severity. Each one was implemented and tested on its own.

## Threat model

LLT's manifest requests `requireAdministrator` and `uiAccess="true"`, and its autostart task runs **with highest privileges at logon**. The main question is therefore:

> Can a program running with **normal user rights** (for example malware that never got an admin prompt) make the elevated LLT do something on its behalf?

In v2.36.0.0 the answer was yes, in several ways: LLT read code, settings and helper programs from places such a program can write to. A second question is whether **files from other people**, such as a shared settings backup or lighting profile, can run code. In v2.36.0.0 they could.

Out of scope: an attacker who already has administrator rights, and physical attacks.

---

## High severity

### 1. Plugins loaded from a user-writable folder
**Before:** every `*.dll` in `%LOCALAPPDATA%\LenovoLegionToolkit\Plugins` (and `Plugin`) was loaded and its code run at every start. There was no signature check and no prompt.

**Now:** plugins load only from `<install folder>\Plugins`, and only if that folder can be changed by administrators alone (checked from its ACL by `DirectoryProtection`). Portable copies don't load plugins. DLLs in the old folder are never loaded; a one-time message tells the user where to move trusted plugins.

Files: `WPF/Station/Core/ExtensionManager.cs`, `Lib/Station/Core/PluginDiscovery.cs`, `Lib/Utils/DirectoryProtection.cs`.

### 2. Settings that the admin app acts on were user-writable
**Before:** automations (including "Run program" steps), macros (replayed as keystrokes from a `uiAccess` process) and `args.txt` (startup flags, proxy settings) lived in `%LOCALAPPDATA%`.

**Now:** for an installed, elevated app, settings live in `<install folder>\UserData\<user SID>`. That folder has explicit permissions: SYSTEM and Administrators full control, the user read-only. Existing settings are copied there once. Portable or non-elevated (debug) runs keep the old folder and log a warning. Logs stay in `%LOCALAPPDATA%`, because they are written but never trusted.

Files: `Lib/Utils/Folders.cs` (`Folders.Settings`, migration) and every settings reader and writer.

### 3. Code execution from crafted settings or profile files
**Before:** all settings files, and imported LampArray lighting profiles, were read with Json.NET `TypeNameHandling.Auto` and no type restrictions. A file could name any .NET type. The classic `ObjectDataProvider` gadget **was reproduced**: it starts `cmd.exe` while the file is being loaded, against LLT's own JSON options.

**Now:** a `SafeSerializationBinder` allows only LLT's own types, enums, simple values, colors, and standard collections of those. Unknown assemblies are rejected before they are loaded.

Files: `Lib/Utils/SafeSerializationBinder.cs`, `Lib/Settings/AbstractSettings.cs`, `Lib/Controllers/SpectrumKeyboardBacklightController.cs`.
Tests include a control test that proves the gadget runs **without** the allow-list, so the protected test is meaningful.

### 4. Restoring a shared backup
**Before:** a restore copied any `automation.json`, `macro.json` or `args.txt` from the chosen folder without showing what was inside.

**Now:** before restoring, LLT lists every program the backup would run, the number of macros, and risky startup flags (proxy, debug and similar; values are never shown), and asks for confirmation.

Files: `Lib.Automation/Utils/BackupInspector.cs`, `WPF/Windows/Settings/SettingsBackupWindow.xaml.cs`.

### 5. Driver-check helpers run from %TEMP%
**Before:** some Lenovo driver packages use a small helper program to detect whether an update is needed. LLT downloaded it to `%TEMP%` and ran it as admin, and it **reused any file already at that path**. A planted file would run as admin when the user checked for driver updates.

**Now:** the helper is always downloaded into a new folder and locked against changes. It runs only if its Authenticode signature is valid **and** the signer's organization is `Lenovo`, and only if the catalog command runs exactly that file. It was checked against the three real helpers in the catalog of an 82XV (LOQ 15IRH8): all are signed `O=Lenovo` and pass.

Files: `Lib/PackageDownloader/Detectors/Rules/ExternalDetectionRule.cs`, `Lib/Utils/AuthenticodeVerifier.cs`, `Lib/Utils/LockedFile.cs`.

## Medium severity

### 6. Update installer integrity
**Before:** the "signature check" only read the certificate's thumbprint (`X509Certificate.CreateFromSignedFile` does not verify that the file matches its signature). It was skipped for GitHub downloads. The file stayed writable in `%TEMP%` between the check and its launch.

**Now:** `WinVerifyTrust` verifies the signature, then the existing thumbprint pin applies, for both download sources. The installer stays locked from verification until it starts. The real v2.36.0.0 installer is accepted and an unsigned one is rejected.

### 7. "Accept any certificate"
`--proxy-allow-all-certs` turned off HTTPS certificate validation for update, driver and warranty traffic. It has been removed; old `args.txt` files still load, and the flag is ignored with a log line.

### 8. Boot logo passed to firmware
The boot logo is decoded by the UEFI firmware before Windows starts, and firmware image parsers have had serious bugs ("LogoFAIL", 2023). Static images are now always decoded and re-encoded. GIFs, which can't be re-encoded without losing animation timing, must pass a strict validator: known blocks only, frames inside the screen, LZW data that is valid and never refers to colors outside the palette or produces more pixels than the frame holds, and nothing after the trailer. The same bytes that were validated are the ones written.

### 9. Script console removed
A hidden debug console ran C# inside the admin app behind a name-based blocklist that was easy to bypass. The console, its engine and the Roslyn scripting and AvalonEdit packages have been removed.

### 10. Autostart from writable folders
The logon task (highest privileges) pointed to wherever LLT was started from. It is now refused for unprotected folders, and an existing task pointing to one is removed. A portable copy can no longer re-point the task to itself.

### 11. CLI pipe
The pipe accepted any signed-in user, used one global name, and the CLI trusted whoever served it. Now each user gets their own pipe, only that user may connect, the server refuses to share a name someone else created (`FirstPipeInstance`, with backoff), and `llt.exe` checks that the server process is `Lenovo Legion Toolkit.exe` in its own folder.

### 12. Signing key in CI
Pull-request builds received the signing key while running code from the PR. They now build unsigned with no secrets, and all GitHub Actions are pinned to full commit SHAs.

---

## Remaining limitations

- **Root certificate.** The installer adds the project's self-signed certificate to `LocalMachine\Root`, because `uiAccess` requires a trusted signature. It is limited to code signing (checked: end-entity, Code Signing EKU), so it can't be used to intercept HTTPS. Anyone holding its private key can still sign programs that LLT users' PCs trust. This needs a properly issued certificate to fix.
- **Same-user CLI access.** Any program running as the same user can still use the CLI (when enabled in settings) to drive the elevated app. The CLI is off by default.
- **Hardware verification pending.** These fixes are covered by automated tests but have not yet been run on a laptop as an installed, elevated app. See [TESTING.md](TESTING.md).

## Reporting

Security issues specific to this fork: open an issue in this repository. Issues that also affect the official app should go to the [upstream project](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit).
