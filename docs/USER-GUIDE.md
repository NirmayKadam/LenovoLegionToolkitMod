# User guide

What Lenovo Legion Toolkit (LLT) does and how to use it, page by page. Which options appear depends on your laptop: LLT asks the hardware what it supports and hides what it doesn't have.

## What it is

LLT replaces Lenovo's own apps (Vantage, Legion Zone, Legion Space) on Legion, LOQ and related Lenovo gaming laptops. It's one small app for:

- power modes, including Custom (God Mode) power limits and fan curves;
- battery charging modes;
- the graphics mode (hybrid / discrete GPU);
- display refresh rate, resolution, HDR and overdrive;
- keyboard lighting (white, 4-zone RGB, Spectrum per-key RGB, Windows Dynamic Lighting);
- automations ("when I plug in the charger, switch to Performance");
- keyboard macros;
- driver and BIOS downloads from Lenovo;
- an on-screen display and a command-line tool.

This mod adds bug and security fixes ([BUG-FIXES.md](BUG-FIXES.md), [SECURITY.md](SECURITY.md)) and three UI styles with a live status strip ([SKINS.md](SKINS.md)).

## Requirements

- A Lenovo gaming laptop LLT knows: Legion, Legion Slim/Pro, LOQ, Legion Go and some related models. On other PCs it shows an "unsupported" message.
- Windows 10 version 2004 or Windows 11, 64-bit.
- The .NET 9 Desktop Runtime. The installer downloads and installs it if it's missing.
- Administrator rights: LLT talks to firmware and drivers.

## Installing this mod

Build an installer as described in [BUILDING.md](BUILDING.md) (`make.bat`, signed with your own certificate) and run it. Keep the default **Program Files** location, because Windows only starts the app from a protected folder.

**Before you start:**
- **Smart App Control** must be off. It doesn't trust self-made certificates.
- **Lenovo's apps:** Vantage, Legion Zone and Legion Space fight LLT for control of the same hardware. LLT can turn their services off for you (Settings → Software control).
- **Updates:** don't accept the app's update offers. They install the official release, which replaces this mod.

## The window

The left rail has the pages. The title bar shows the version and, if logging is on, a "Log" badge you can click to open the log. Closing or minimizing can send LLT to the tray (Settings → App behavior), where it keeps running automations and listening for Fn keys.

**Hover help (this mod).** Every setting, dashboard card, automation action and trigger has a small ⓘ next to its name. Hover over it (or tab to it with the keyboard) to see what the setting does, when to use it and, where there is one, the risk. Turn this off or change the delay in Settings → App behavior → Help on hover.

### Dashboard

The status strip comes first (this mod; turn it off in Customize):

- one tile each for **power mode, CPU, GPU, fans and battery**;
- a **power-mode selector** with one button per mode your laptop has.

Below it are the **sensors** (temperatures, clocks, fan speeds, usage; right-click for the refresh rate) and the **cards**. **Customize** at the bottom lets you reorder, hide and regroup the cards, or show/hide the sensors and status strip.

**Power mode details (this mod)** sits under the selector. It shows what the current power mode sets and what changed from the previous mode ("55 → 80 W"), whether you switched from the dashboard, Fn+Q or an automation:

- CPU long-term, short-term and peak power limits, and the CPU temperature limit;
- GPU power limit (cTGP), Dynamic Boost and GPU temperature limit;
- the fan curve in use: yours (see *Fan curves per power mode*), the Custom preset's, or "firmware built-in";
- live clocks, and a **now** value read back from the firmware for the active mode, so you can see whether the laptop really applied it.

Values come from the firmware's own per-mode table, or from the active preset in Custom mode. A value the laptop doesn't report shows "—". Two things are deliberately not shown rather than made up: Lenovo's firmware doesn't expose its built-in fan curves, and no Intel or Lenovo interface exposes a CPU frequency/voltage curve. The card can be collapsed.

| Card | What it does |
|---|---|
| Power mode | Quiet, Balance, Performance, plus Extreme and Custom where supported. The gear opens Balance (AI) or Custom (God Mode) settings. Same as Fn+Q. |
| Battery mode | Conservation (Lenovo's mode that holds the battery below full to slow wear), Normal, Rapid charge |
| Night charge | Slow overnight charging (some models) |
| Always-on USB | Off; on when sleeping; or always on (USB ports stay powered so you can charge devices) |
| Instant boot | Powers the laptop on when the charger is connected: AC adapter, USB Power Delivery, or both |
| Hybrid mode / GPU working mode | Which GPU drives the screen: Hybrid, Hybrid-iGPU, Hybrid-Auto, UMA, or dGPU (discrete only). Options depend on the model; some changes need a restart. |
| Discrete GPU | Shows whether the NVIDIA GPU is active and which apps keep it awake; can deactivate it |
| GPU overclock | Core/memory offsets for the NVIDIA GPU |
| Resolution, Refresh rate, DPI scale, HDR, Overdrive, Auto color management | Display settings for the built-in screen |
| Turn off monitors | Turns the screens off without sleeping |
| Microphone | Mutes or unmutes all microphones |
| Flip to start | Opening the lid powers the laptop on |
| Touchpad lock, Fn lock, Win key lock | Keyboard/touchpad locks |
| White keyboard backlight, Panel logo, Ports backlight | Simple lighting on/off and levels |
| ITS mode | Lenovo's power profiles on ITS models (IdeaPad/Yoga-class) |

#### Custom mode (God Mode)

Available on supported models when the charger is connected. You set:

- CPU and GPU power limits;
- temperature limits;
- fan curves.

Presets can be saved and switched (also by automations). If you're unsure, start from the defaults the dialog loads. Wrong limits can make the laptop hot or loud.

Every value is checked against the range the firmware reports each time the preset is applied, not only when you edit it. A value outside that range (for example from a settings backup made on another laptop) is replaced by the firmware's default and never sent. The **Minimum offset** and **Maximum offset** fields under Advanced can only narrow a range in this mod; they can no longer take a slider past the firmware's own minimum or maximum.

#### Lightness (this mod)

All animations in LLT run at 30 frames per second, and nothing animates while LLT is in the tray. Sensor and battery readings are shared between the dashboard, the status strip and the HWiNFO integration, so the laptop's sensors are read once per second at most, not once per screen.

#### GPU overclock safety (this mod)

- **Crash guard.** When you apply GPU values that haven't proven stable yet, LLT notes it. After 60 seconds without a crash the note is cleared. If the laptop freezes or restarts before that, LLT turns GPU overclock **off** at the next start, keeps your values so you can lower them, and shows a warning on the GPU overclock card. A crash that only happens later under load is not caught this way; the laptop still starts normally.
- **Limits.** Clock offsets are kept inside the range the NVIDIA driver reports for your GPU. The voltage lock can't go above the top of the GPU's own voltage range, and isn't set at all if that range can't be read.
- **Honest result.** The settings window says whether the driver accepted the values, instead of always "applied".

`docs/SAFETY.md` lists every change LLT can make to the laptop, whether it survives a restart, and how to undo it.

#### Fan curves per power mode (this mod)

Open it from **Fan curves…** in the Power mode details card or in Settings → Power. Pick a mode (Quiet, Balance, Performance, and Extreme where present), draw a curve with the same editor as Custom mode, tick **Use my curve in …**, and **Save**. After every mode change, from Fn+Q, the dashboard or an automation, and once at start and after sleep, LLT writes that mode's curve and reads it back. **Save** reports as soon as that read-back matches (about half a second); LLT then checks once more about 3 seconds later in the background, writes the curve again if the firmware put its own back, and shows a warning in the window if that happens twice. If you change mode again in the meantime, the earlier curve is dropped: a curve is only ever written for the mode the laptop is in.

While Save, Test now or Apply now runs, the window's controls are locked and a status line under the editor says what is happening and how it ended.

**Apply now** writes the curve in the editor to the fans without saving it, so you can hear it first. It works only on the mode the laptop is in. If you close the window without saving, the fans go back to what is saved for that mode (your saved curve, or the firmware's).

Many Lenovo firmwares only honour custom curves in Custom mode and put their own curve back after a mode change. So:

1. **Test now** (it asks first) tests the curve of the mode the laptop is **in**; if the list shows another mode, it switches to the active one first. It writes the curve, checks about 3 seconds later whether the firmware kept it (about 4 seconds in all), and remembers the answer. Afterwards the fans go back to the curve they had before, without a power mode change, unless this was your saved curve for that mode and the firmware kept it. Only this test records the answer; if the firmware didn't respond or the mode changed during the test, nothing is recorded and you can test again.
2. If the firmware keeps it, your curves are used from then on.
3. If it doesn't, LLT stops trying and offers **Create Custom preset**: a Custom preset with that mode's own firmware power and temperature limits plus your curve. Choose it in Custom mode. Fn+Q keeps cycling the built-in modes with the firmware's curves. Creating it again for the same mode replaces that preset; it doesn't add another.

Curves below the laptop's minimum fan levels are refused, full speed stays off, and turning a curve off, or the whole feature off, brings the firmware's curve back at once.

### Battery

Live battery details:

- charge, charging state and discharge rate (with min/max);
- design vs. full-charge capacity, and health;
- cycle count and battery temperature;
- manufacture and first-use dates;
- time on battery.

It warns when a low-wattage charger is connected.

### Keyboard

Depends on your keyboard:

- **4-zone RGB:** presets with effect, speed, brightness and a color per zone.
- **Spectrum (per-key RGB):** profiles, brightness, and effects per key or zone, including screen-sync ambient effects.
- **Dynamic Lighting (LampArray):** on newer models such as the Legion Pro 7 Gen 10, the zones Windows exposes, with effects.

### Actions (automation)

Build **pipelines**: a **trigger** ("when…") and **steps** ("do…"). Examples:

- *When AC adapter connected* → Power mode Performance, Refresh rate 240 Hz.
- *When AC adapter disconnected* → Power mode Quiet, Refresh rate 60 Hz, Battery mode Conservation.
- *When a game starts* → God Mode preset "Gaming"; *when games stop* → back to Balance.
- *At sunset every day* → Keyboard backlight preset "Warm".

**Triggers:**
- the charger connected or disconnected (and low-wattage charger);
- battery percentage;
- power mode changed or God Mode preset changed;
- games or specific processes started or stopped;
- a time, or a time range (sunrise/sunset too);
- Wi-Fi connected or disconnected;
- display on/off, or an external display connected;
- lid opened/closed;
- session lock/unlock, user inactivity;
- startup, resume, or periodic;
- and/or combinations.

**Steps:** nearly every dashboard setting, plus delays, notifications, running a program, closing an app, playing a macro, playing a sound, and the OSD.

You can also make **quick actions**: pipelines without a trigger, run from the tray menu or `llt quickAction`. **Run** tests a pipeline immediately.

### Macro

Record a series of key presses and play it back with the number pad. Macros can also be used as an automation step. LLT must be running for macros to work.

### Downloads

Lists Lenovo driver, BIOS and software updates for your exact machine type and Windows version, from Lenovo's catalogs (primary and secondary source). Filter, sort, show only updates, and download to a folder you choose. Installing is up to you. Read the disclaimer, especially for BIOS updates.

### Settings

| Tab | Highlights |
|---|---|
| **App behavior** | Help on hover, game-safe mode, start at login (starts in the tray), minimize to tray, minimize on close, always on top, compact mode, window size lock and position, notifications, OSD, sensor options, excluded processes, startup arguments, logging, settings backup and restore, reset |
| **Appearance** | Language, **Style** (Fluent glass / Neon / Retro CRT), **Animations**, theme, accent color, background image, backdrop, hardware acceleration |
| **Display** | Custom boot logo, brightness sync across power plans |
| **Power** | What power mode, Windows power plan and Windows power mode each do (see below); link power modes to Windows power plans or Windows power modes; fan curves per power mode; reset the "on battery since" timer |
| **Software control** | Turn Lenovo Vantage, Legion Zone, Legion Space and Lenovo's hotkey software off or back on |
| **Special keys** | Smart keys, Smart Fn Lock, key discovery, refresh rates to skip when cycling |
| **Integrations** | The **CLI** pipe (off by default) and adding `llt` to PATH, HWiNFO sensor sharing (fan speeds and battery temperature, updated every 2 s, HWiNFO's own default; turn it off if you don't use HWiNFO) |
| **Updates** | Update checks, channel, frequency and method |

**Settings backup** saves your settings to a file. When restoring a backup, this mod shows any programs, macros or startup options inside it and asks before applying them.

### Power mode, Windows power plan, Windows power mode

Three different things with similar names:

| | What it is | What it changes |
|---|---|---|
| **Power mode** | The laptop's own mode, set in the firmware (Fn+Q, the dashboard) | Fan behavior and CPU/GPU power and temperature limits |
| **Windows power plan** | Windows' classic plan (Control Panel → Power Options) | Sleep and screen-off timers, minimum/maximum processor state |
| **Windows power mode** | The Windows 11 slider: Best power efficiency / Balanced / Best performance | How Windows schedules work on the CPU; only applies with the Balanced plan |

Settings → Power can switch the Windows plan or the Windows power mode whenever the laptop's power mode changes. On laptops with Modern Standby, keep the Balanced plan and map to Windows power modes instead.

## On-screen display and notifications

Changing a mode (from the app, Fn+Q or an automation) shows a short notification. The **OSD** is an always-on-top overlay with FPS, temperatures and usage. It is its own window drawn over the game; LLT never injects into a game or reads its memory. Turn it on and position it in its settings window, or toggle it from an automation.

**Quick toggle (this mod).** In the OSD settings window:

- **Hotkey:** click the box and press a combination with Ctrl, Alt or Win (for example Ctrl+Shift+O). LLT registers it with Windows' `RegisterHotKey`, the same way media apps do; there is no keyboard hook. If another app already uses the combination, you're told so.
- **Fn / special key:** **Create quick action** makes a "Toggle OSD" quick action, and **Open special keys** lets you put it on a key.

The OSD no longer needs hardware sensors. Without them it shows what Lenovo's firmware, the NVIDIA driver and Windows report.

## Anti-cheat and game-safe mode (this mod)

**Game-safe mode** (Settings → App behavior, on by default) checks every 5 seconds whether an anti-cheat service is running: Riot Vanguard (`vgc`), Easy Anti-Cheat (`EasyAntiCheat`, `EasyAntiCheat_EOS`) or BattlEye (`BEService`). It only reads the service's status and never looks at the game. While one runs:

- detailed sensor reads through the PawnIO driver stop. The OSD keeps going with driver-free readings and shows a **GAME-SAFE** label; values that need the driver show "—";
- macros are stopped and their keyboard hook is removed;
- the Caps Lock / Num Lock keyboard hook is removed;
- FPS keeps working through Windows' event tracing (ETW). LLT finds the game in the process list without opening it.

Everything comes back when the service stops. The user-inactivity trigger doesn't use hooks at all: it reads Windows' last-input time.

**What can't be promised:** only the anti-cheat vendors decide what they flag. LLT avoids what they're known to look for: injection, opening the game's process, reading its memory, simulated input, and driver use during play. Two limits remain. Pausing stops LLT's driver requests but doesn't unload the PawnIO driver, and Vanguard decides by itself at boot whether that driver may load. No ban-safety guarantee is made.

## The command line (`llt.exe`)

1. Turn on **Settings → Integrations → CLI**.
2. Run `llt.exe` from the install folder, while the app is running.

```bash
llt quickAction --list
llt quickAction "Gaming"
llt feature --list
llt feature get power-mode
llt feature set power-mode --list
llt feature set power-mode performance
llt spectrum brightness set 50
llt rgb set 2
```

Only your own Windows account can talk to your running LLT.

## Startup options

Add these to the shortcut or the command line:

| Option | Effect |
|---|---|
| `--minimized` | Start in the tray |
| `--trace` | Write a detailed log to `%LOCALAPPDATA%\LenovoLegionToolkit\log\` |
| `--skip-compat-check` | Start on a model LLT doesn't recognize (features may not work) |
| `--disable-update-checker` | Don't check for updates |
| `--allow-all-power-modes-on-battery` | Allow Performance/Custom without the charger |
| `--force-disable-rgbkb`, `--force-disable-spectrumkb`, `--force-disable-lenovolighting` | Skip a lighting subsystem (if it misbehaves) |
| `--disable-tray-tooltip`, `--disable-conflicting-software-warning` | Quieter UI |
| `--proxy-url`, `--proxy-username`, `--proxy-password` | Network proxy for update checks and downloads |
| `--debug` | Developer mode: console window, every feature shown as available |

## Where your data is

| What | Where |
|---|---|
| Settings (installed copy) | `C:\Program Files\LenovoLegionToolkit\UserData\<your user ID>\`; only administrators can change it |
| Settings (portable or Debug copy) | `%LOCALAPPDATA%\LenovoLegionToolkit\` |
| Logs | `%LOCALAPPDATA%\LenovoLegionToolkit\log\` |

On first start after installing this mod, settings from the old location are copied to the new one.

## Troubleshooting

| Problem | What to do |
|---|---|
| "Smart App Control blocked part of this app" | Smart App Control rejects self-signed builds. Turn it off, or sign the app with a trusted certificate ([BUILDING.md](BUILDING.md)). |
| The app won't start after installing to a custom folder | Reinstall to Program Files (uiAccess needs a protected folder) |
| "Unsupported" message | Your model isn't in LLT's list. `--skip-compat-check` lets you try at your own risk. |
| LLT closed by itself, or showed an error | Look in `%LOCALAPPDATA%\LenovoLegionToolkit\log` for `error_*.txt`. LLT writes one there for any unexpected error, even with logging off, and keeps the newest 20. They stay on your PC; nothing is sent anywhere. Attach the newest one when reporting a problem. |
| A card is missing | Your laptop doesn't support it, or Lenovo's software is interfering (turn it off in Software control) |
| Settings snap back or modes change by themselves | Vantage, Legion Zone or Legion Space is still running |
| Performance/Custom is refused | You're on battery; plug in the charger (or use `--allow-all-power-modes-on-battery`) |
| Animations seem minimal | Hardware acceleration is off by default. Turn it on in Appearance and restart. |
| Anything else | Turn on logging (App behavior, or `--trace`), reproduce the problem, then read or share the log |

## Uninstalling

Use Windows Settings → Apps. The uninstaller removes the app and the certificate it trusted. At the end it asks whether to delete all settings:

- **Yes:** deletes the settings of every Windows user on this PC (`Program Files\LenovoLegionToolkit\UserData\`), plus `%LOCALAPPDATA%\LenovoLegionToolkit` (logs and settings from older versions). The install folder goes too, unless you put plug-ins in it.
- **No:** keeps everything, so a later reinstall picks your settings up again.

If you turned Lenovo's services off, turn them back on (Settings → Software control) **before** uninstalling.
