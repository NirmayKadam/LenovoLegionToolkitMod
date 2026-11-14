<div align="center">
  <img height="112" src="assets/logo.png" alt="Logo">

  # Lenovo Legion Toolkit — Hardened Mod

  **A bug-fixed and security-hardened fork of [Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) v2.36.0.0**

  ![Base](https://img.shields.io/badge/based%20on-LLT%20v2.36.0.0-blue)
  ![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
  ![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)
  ![Tests](https://img.shields.io/badge/tests-170%20passing-brightgreen)
  ![License](https://img.shields.io/badge/license-GPL--3.0-informational)
</div>

> [!IMPORTANT]
> This is an **unofficial, personal modification**. It is not affiliated with, endorsed by, or supported by Lenovo or the LenovoLegionToolkit-Team. "Lenovo" and "Legion" are trademarks of Lenovo. The software comes with **no warranty** (see [LICENSE](LICENSE)). For the original app, use the [official releases](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/releases).

---

## What is this?

Lenovo Legion Toolkit (LLT) is a lightweight Windows utility for Lenovo Legion, LOQ and related laptops that replaces Lenovo Vantage / Legion Zone / Legion Space: power modes, God Mode (custom power limits and fan curves), battery modes, keyboard lighting, automation, a CLI and more. See the [original README](docs/upstream/README.md) for the full feature list and screenshots.

This fork keeps all of that and adds three things:

1. **Fixing bugs** found during a code review of v2.36.0.0.
2. **Closing security holes.** LLT runs as administrator (and starts at logon with top privileges), but it trusted several files and folders that any normal program can write to. A program with ordinary user rights could use that to get administrator rights silently.
3. **New looks.** Three UI styles (Fluent glass, Neon, Retro CRT), an animation setting, and a live status strip with a sliding power-mode selector on the dashboard.

Every change is listed with file names in [docs/CHANGES.md](docs/CHANGES.md).

## Highlights

### Security fixes

| | Problem in v2.36.0.0 | Fix in this mod |
|---|---|---|
| 🔴 | Any DLL in `%LOCALAPPDATA%\…\Plugins` was loaded into the admin app at every start | Plugins load only from the install folder (admin-only); old plugins are ignored with a notice |
| 🔴 | Settings in `%LOCALAPPDATA%` (automation "Run program" steps, macros, startup flags) could be edited by any program | Settings move to a per-user folder under the install folder that only administrators can change |
| 🔴 | A crafted settings or lighting-profile file could run code while being loaded (Json.NET "gadget"; reproduced) | Strict allow-list of types that settings files may contain |
| 🔴 | Restoring a shared backup could silently add programs to run | Restore shows programs, macros and startup flags inside the backup and asks first |
| 🔴 | Driver-check helpers were run as admin from `%TEMP%`, reusing any file already there | Always freshly downloaded, locked, and must be validly signed by Lenovo |
| 🟠 | Update installer could be swapped between check and launch; signature check didn't verify the file | Real Authenticode verification (WinVerifyTrust) plus a lock until launch |
| 🟠 | `--proxy-allow-all-certs` disabled HTTPS certificate checks for all traffic | Removed |
| 🟠 | Boot-logo images were copied byte-for-byte to the EFI partition (LogoFAIL-style risk) | Static images always re-encoded; GIFs pass a strict structural validator |
| 🟠 | Script console with a bypassable "sandbox" | Removed |
| 🟠 | Autostart (highest privileges) could point to a portable copy in a writable folder | Only allowed from protected locations |
| 🟠 | CLI pipe open to every signed-in user, could be hijacked | Per-user pipe, current user only, client verifies the server is LLT |
| 🟠 | CI exposed the signing key to pull-request builds | PR builds are unsigned; all GitHub Actions pinned to commit SHAs |

Details: [docs/SECURITY.md](docs/SECURITY.md)

### Bug fixes

- Scheduled automations fired on the **wrong day** for users outside UTC (e.g. India), and could miss their second entirely
- Scripts that print more than ~4 KB **froze all automations**
- CLI replies over 1 KB (or with Chinese/Japanese text) were **corrupted**; one bad client could stop the CLI server
- "Check if already running" treated anything under `System32` as running
- Automation cancellation never actually cancelled anything
- Settings writes are now atomic (no half-written settings after a crash)
- Plus Smart Fn Lock, God Mode, sensor and build fixes

Details: [docs/BUG-FIXES.md](docs/BUG-FIXES.md)

### UI styles and animations

- **Settings → Appearance → Style:** *Fluent glass* (the original look, default), *Neon* (dark graphite, cyan, color-coded power modes, soft glow) or *Retro CRT* (green, amber or white phosphor, scanlines, monospaced text). Applies instantly.
- **Animations:** Full, Reduced (fades only) or Off. Full needs Windows animation effects and the app's hardware acceleration, which LLT has always left off by default.
- **Dashboard status strip:** power mode, CPU, GPU, fans and battery at a glance, plus a power-mode selector with a sliding highlight. Every value is a real reading; anything the hardware doesn't report shows "—". Toggle it in *Dashboard → Customize*.
- Every style keeps text contrast at or above 4.5:1 (unit-tested). Windows High Contrast always uses Fluent.

Details: [docs/SKINS.md](docs/SKINS.md)

## Project status

| Check | Status |
|---|---|
| Builds (Release, app + CLI) | ✅ 0 errors, no new warnings |
| Automated tests | ✅ 170 passing ([docs/TESTING.md](docs/TESTING.md)) |
| Tested on a Legion/LOQ laptop as an installed, elevated app | ⏳ **Not yet.** See the manual checklist in [docs/TESTING.md](docs/TESTING.md) |

Treat this as **pre-release** until the hardware checklist has been completed.

## Building

Requirements: Windows 10/11 x64 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
git clone https://github.com/NirmayKadam/LenovoLegionToolkitMod.git
cd LenovoLegionToolkitMod
dotnet build LenovoLegionToolkit.WPF\LenovoLegionToolkit.WPF.csproj -c Release
dotnet test  LenovoLegionToolkit.Tests\LenovoLegionToolkit.Tests.csproj -c Release
```

Building a runnable **installer** needs Inno Setup and your own code-signing certificate, because the app requests `uiAccess`, which Windows only allows for signed executables in a protected folder. See [docs/BUILDING.md](docs/BUILDING.md).

## Things to know before using it

- **The built-in updater still points at the official project.** Accepting an update installs the official LLT, which replaces this mod. To keep the mod, set the update check frequency to *Never* in Settings → Updates, or start the app with `--disable-update-checker`.
- **Settings location changed.** On first start of an installed build, settings are copied once from `%LOCALAPPDATA%\LenovoLegionToolkit` to `<install folder>\UserData\<your user ID>`.
- **Plugins** must be placed in `<install folder>\Plugins` (needs admin).
- **Autostart** only works for installed copies, not portable ones.
- **CLI:** `llt.exe` from this build only talks to the app from the same build.

## Documentation

Start at the [documentation index](docs/README.md).

| Document | Contents |
|---|---|
| [docs/USER-GUIDE.md](docs/USER-GUIDE.md) | Using the app: every page, the CLI, startup options, where data lives, troubleshooting |
| [docs/CODEBASE-GUIDE.md](docs/CODEBASE-GUIDE.md) | Understanding the code from scratch: tech stack, tools, reading order, exercises, worked example, glossary |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Projects, layers, core patterns, hardware access, startup, data flows, where to change what |
| [docs/SECURITY.md](docs/SECURITY.md) | Threat model, each security fix, remaining limitations |
| [docs/BUG-FIXES.md](docs/BUG-FIXES.md) | Each bug, its cause and its fix |
| [docs/SKINS.md](docs/SKINS.md) | UI styles, animation levels, the dashboard status strip, and how styles work |
| [docs/TESTING.md](docs/TESTING.md) | Test project, testing rules, manual hardware checklist |
| [docs/BUILDING.md](docs/BUILDING.md) | Building, testing, installers, signing, CI |
| [docs/CHANGES.md](docs/CHANGES.md) | Dated list of every modified, added and removed file (GPL notice) |
| [docs/upstream/](docs/upstream/README.md) | Original README (English, 日本語, Русский, 简体中文) |
| [docs/controls.md](docs/controls.md), [docs/icons.md](docs/icons.md), [docs/localization.md](docs/localization.md) | Upstream developer notes |

## Credits

- **Bartosz Cichecki**: original author of Lenovo Legion Toolkit
- **LenovoLegionToolkit-Team** (Kaguya, Dr. Skinner and contributors): maintainers of v2.36.0.0, on which this fork is based
- Third-party components and their licenses: [NOTICE](NOTICE)
- Modifications in this fork: Nirmay Kadam, 2026

## License

Lenovo Legion Toolkit is licensed under the **GNU General Public License v3.0**, and so is this modified version. See [LICENSE](LICENSE). Modified files are listed with the date of change in [docs/CHANGES.md](docs/CHANGES.md), as required by GPL-3.0 section 5. Third-party components keep their own licenses ([NOTICE](NOTICE)).
