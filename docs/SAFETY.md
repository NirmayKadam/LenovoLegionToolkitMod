# Safety: what LLT writes, and how to undo it

This page lists every kind of change Lenovo Legion Toolkit (LLT) can make to the laptop's firmware, GPU and boot files, whether it survives a restart, what limits the value, and how to get back.

LLT's updater only checks this fork's GitHub releases and opens the release page. It never downloads or installs anything.

To see the current state of these rows on your laptop, open **Safety report** (Settings → App behaviour, or the About page). It is read-only and changes nothing. It reads LLT's own settings and state and the settings folder's file list (names, sizes, dates), and makes one read-only Lenovo WMI query for the current power mode, the same one the dashboard makes. It calls no hardware driver (PawnIO, NVIDIA). **Copy as text** gives a version to paste into a bug report.

Settings backups now include `backup_info.json`: the LLT version and laptop model (machine type and name; no serial number) that made them. Restoring a backup made on another model warns first. LLT checks every Custom mode and fan value against this laptop's own ranges anyway.

It was written by reading the code (2026-11-13). **Whether the firmware forgets a value on restart has not been checked on a laptop yet**; rows that depend on that say "expected".

## What LLT can and cannot promise

LLT cannot see inside the firmware, so it cannot prove what the firmware does with a value. What LLT does guarantee:

1. It never sends a power, temperature, fan or GPU value outside the range the firmware or the GPU driver reports as allowed.
2. A GPU overclock that froze or crashed the laptop is not applied again automatically at the next start.
3. Every change that survives a restart is in the table below, with a way back.

LLT does **not** flash the BIOS or any other firmware. The Packages page only downloads Lenovo's own update files; you run them yourself.

## Changes that survive a restart

| Change | Where it is stored | What limits it | How to undo |
|---|---|---|---|
| Custom boot logo | A UEFI variable, plus one image file in `EFI\Lenovo\Logo` on the EFI partition | The image is checked and re-encoded before it is copied; the firmware compares the file with a checksum LLT writes and shows the stock logo when they differ | Turn the custom logo off in LLT. That deletes the file, clears the checksum and the "enabled" flag |
| Flip to Start | A UEFI variable | On or off only | Toggle it in LLT, or in BIOS setup |
| Hybrid mode / GPU working mode | A BIOS setting (`SetBiosSetting` + save), or a firmware flag on newer models | One of a fixed list of modes | Toggle it in LLT, or in BIOS setup |
| G-Sync, OverDrive, Instant Boot, Always-on USB, Fn Lock, touchpad lock, Windows key lock, battery charge mode, keyboard backlight | The embedded controller / firmware flags | On/off or a fixed list of modes | Toggle it in LLT. Most are also in BIOS setup or on an Fn key |
| Charge threshold (only on laptops with Lenovo's power management driver, mostly ThinkPads) | Lenovo's power management settings in the registry (`HKLM\SOFTWARE\WOW6432Node\Lenovo\PWRMGRV`), and the same values sent to that driver | Start and Stop between 2 and 99 %, Start below Stop; anything else is refused before it is written | Turn it off on the card; the battery charges to 100% again |
| Power button light (where the firmware has it) | The firmware | Brightness 1 to 4, or following the keyboard backlight; each change is read back | Change it on the card |
| Startup entries of Lenovo Vantage and Legion Zone (Settings → Software control) | Windows' list of enabled startup apps (the same switch as Task Manager → Startup apps) | Only entries that start Lenovo Vantage or Legion Zone | Enable the software again in Software control, or in Task Manager |
| AMD Ryzen Master driver installed by Legion Zone (only when Legion Zone's folder exists) | The driver's start setting, disabled together with Legion Zone | Only that driver | Enable Legion Zone again in Software control |
| LLT's own settings (God Mode presets, fan curves, GPU overclock values, automations) | Files in `Program Files\LenovoLegionToolkit\UserData\<SID>` | See the rows below for what is sent to the hardware. Settings saved by an older version with names that have since changed are read with the new names | Change them in LLT, or delete the file while LLT is closed |

## Changes expected to be gone after a restart

These are sent to the running firmware or GPU driver. LLT applies them again when it starts or when the power mode changes; without LLT running, the laptop is expected to use its own defaults.

| Change | What limits it | How to undo |
|---|---|---|
| Power mode (Quiet / Balance / Performance / Custom) | Fixed list of modes | Fn+Q, or LLT |
| God Mode: CPU and GPU power limits, temperature limits | Each value is checked against the firmware's own minimum, maximum and steps **every time it is applied** (the ranges are read once per run and again after sleep). A setting the preset doesn't save gets the firmware's own Performance (or Extreme) default on current models. The "min/max value offset" fields can only narrow that range. When the ranges can't be read, your saved values are not sent; the firmware's own Performance (or Extreme) default for each setting is sent instead | Switch to another power mode; the firmware loads that mode's own limits |
| Fan curve (Custom mode, and per-mode fan curves) | Never below the laptop's minimum curve, never above level 10. Every write is read back. A curve is only written for the mode the laptop is in at that moment | Switch power mode, or turn per-mode fan curves off; the firmware's curve returns. **Use firmware curve** (Fan curves window, or the power-mode details card) removes your curve for one mode or all modes and, if that curve is in use, applies the same power mode again; it writes no fan table itself |
| Fan full speed | On or off | Turn it off in LLT, or switch power mode |
| GPU clock offsets (core, memory) | Sent through the GPU's voltage/frequency curve when the driver offers it (as MSI Afterburner does), otherwise as a plain offset. LLT's fixed limits, and inside the range the NVIDIA driver reports for this GPU (per clock). When the driver allows only one value (e.g. 0 when overclocking is locked), only that value is sent. **Nothing is sent while MSI Afterburner runs**, not even the zero offsets of switching it off, and the same goes for the voltage lock/cap and LLT's P-state lock | Turn GPU overclock off in LLT, or restart with LLT closed |
| GPU voltage lock / voltage cap | Within LLT's fixed 700–1200 mV, and placed on a point that exists on the GPU's own voltage/frequency curve (never above its highest point). If that curve cannot be read, no lock or cap is sent and LLT says so | Set it to Off in LLT, or restart with LLT closed |
| GPU P-state lock (Discrete GPU window), with the pin mode (the state's highest or lowest clock) or the rated TDP | Only the P-states the driver reports for this GPU | Set it back to Dynamic, or restart with LLT closed |
| GPU power target offset (PCF) | The firmware's own setting: its reported range, as for God Mode (values above 255 W can only go through NVIDIA's power control, and only when the firmware's range allows them; the max offset field never widens it). NVIDIA's power control setting on GPUs that have one: 0 to 500 W over the default, and the driver applies its own limits | Switch power mode; LLT resets it |
| AMD Precision Boost Overdrive / Curve Optimizer (AMD models only, and only when BIOS overclocking is enabled) | Fixed ranges (scaler 1–7, boost 0–200 MHz, curve 0–20), checked every time they are applied | Turn overclocking off in the God Mode preset |

### Undo within 30 seconds

After a God Mode (Custom mode) Save, a fan-curve Save, or **Use firmware curve** / **Reset all modes**, an **Undo (30 s)** bar appears in that window and in the main window. Undo:

- puts back exactly what was saved before (the same file contents) and saves it at once;
- for God Mode: if the laptop is still in Custom mode, sends the earlier preset again. If that Save had switched the laptop to Custom, it goes back to the earlier mode instead;
- for fan curves: only the mode the laptop is in is touched, and only if its curve changed. The earlier curve is written and checked, or the mode is applied again to bring the firmware's curve back;
- leaves a power-mode change made since (Fn+Q) alone.

Nothing is read back from the firmware: "the earlier values" are the earlier saved values, sent again. Undo is kept in memory only. After 30 seconds, a newer change, or a restart, the new values simply stay. If the settings were put back but the laptop couldn't be set to them right then, the bar says so and the values are used at the next mode change.

## Changes a game profile makes to other software

Only while a profiled game runs, and only to apps and services you listed. LLT recognises the game from the names Windows reports and from the process list, and never opens, reads or changes the game's process. Every change is written to `game_profile_session.json` **before** it is made.

| Change | What limits it | How it is undone |
|---|---|---|
| Efficiency mode and below-normal priority for listed apps | Only apps in your session and outside the Windows folder; never the game, LLT, or a real-time process. Efficiency mode only when Windows reports the app's current setting and it is off | When the game ends, when LLT exits, or at LLT's next start if it was ended mid-game: the app's earlier priority and efficiency setting come back, only for the same process (an id reused by another process is left alone). Without LLT: closing the app, or a restart |
| Stopping listed services | Only running Win32 services that accept a stop and that no running service depends on (LLT never stops a cascade). A fixed list (Windows core, UAC elevation, audio, network, security, Windows Update, input and display, NVIDIA display drivers, Lenovo's services, anti-cheats, Gaming Services, Print Spooler) can't be added | Started again when the game ends, when LLT exits, or at LLT's next start if it was ended mid-game. A service already running again is left as it is. Without LLT: a restart starts automatic services again |
| Closing listed apps | A normal close request (like clicking X); an app may ask to save, and one that doesn't close in 10 s is left open. LLT never ends a process | **Not reversible**: closed apps aren't reopened. The Game profiles window says so before the first use |
| Game-safe mode for the game | The same pauses as automatic game-safe mode | Ends with the game |

**Game mode** (one profile for any game without its own) makes the same changes, with the same limits and the same put-back.
- **What counts as a game:**
  - a game Windows has recognised on this PC;
  - the program in front when Windows Game Mode turns on.
- **What doesn't count:**
  - a program that only uses the NVIDIA GPU (a browser, Discord);
  - Windows' own apps.
- Every running game's processes are off-limits, also ones with the same name that LLT didn't see start.
- If a program was eased before LLT recognised it as a game, its priority is put back at once.

## Background services (normal use)

| Change | What limits it | How it is undone |
|---|---|---|
| Stopping the services of the presets you choose (telemetry, SysMain, Search indexing, unused extras, Xbox) while LLT runs | The same rules as game profiles: the protected list (63 services), drivers, services that can't stop and services others depend on are never stopped. Each stop is written to `background_services_session.json` first. **Windows' own service settings (startup type) are never changed** | Started again when LLT exits, when a preset is unticked or the feature turned off ("Start them all again"), and at LLT's next start if it was ended. A service something else stopped, or a game profile paused, is never started by it. Without LLT: a restart starts them as usual |

## Automations

Automations change the same settings as the rest of LLT, with the same limits. What LLT adds around them:

- **Steps that can't work are shown.** Each automation lists the problem steps by number:
  - steps that do nothing (AI Engine outside Balance mode, a power mode the laptop is already in);
  - steps a later step overrides;
  - steps after **Close app**, which never run;
  - steps this laptop doesn't support.

  **Add step** greys out the steps that can't work in that automation and gives the reason. A listed step still runs as before. The one exception: a step this laptop doesn't support is now skipped instead of failing at every run.
- **Loops are stopped.**
  - When you edit automations that can start each other (for example `Performance → set Quiet` with `Quiet → set Performance`), each one shows the loop.
  - While running: if power mode or Custom preset changes made by automations start the same automation more than 3 times within 60 s, LLT pauses it and shows a notification. It stays paused until you save your automations again or restart LLT.
  - Changes you make yourself (Fn+Q) never count.
  - Quick actions that run each other no longer crash LLT.
- **Run steps can start their program without administrator rights.** "Run as standard user" is on for new Run steps; steps saved before it existed keep running as administrator, so nothing that worked stops working.
  - With it on, the program gets the same rights as when you start it yourself (those of your desktop). If Windows can't do that (for example with User Account Control off), the program **isn't started** and a notification says why. It never quietly runs as administrator instead.
  - Its output isn't captured in this mode.
- **Run steps with "Run as standard user" off start their program as administrator**, because LLT runs elevated.
  - LLT warns when the program or its folder can be changed by programs that aren't elevated, when no folder is given, or when the program is missing. The warning appears on the step, under the automation and in the safety report.
  - The program still runs: you chose warn only.
  - **Limit:** only the program itself is checked, not a script named in its arguments (`cmd /c script.bat`).
- **Sunrise and sunset** times need your location. Leave Settings → App behaviour → "Location for sunrise and sunset" empty, and LLT asks ip-api.com once a day (unencrypted). If that fails (offline, or the service refuses), it asks again after an hour at the earliest. Only sunrise and sunset triggers ask at all. Type a latitude and longitude there, and nothing is looked up online.
- **The MSI Afterburner profile step** only ever starts the genuine program.
  - The file must be named `MSIAfterburner.exe` and validly signed by Micro-Star; otherwise it isn't started and a notification says why.
  - The file is locked from the check until it has started.
  - Run steps that only switched an Afterburner profile were converted to it once. The automations from before are kept in `automation_before_round14.json` in the settings folder.
  - The profile is applied at once, also mid-game.

## Power mode while a game runs

A game profile, or Game mode, can switch the laptop to a power mode while its game runs.
- **When the game starts:** the earlier mode is written to the session note first, then the new mode is set the same way Fn+Q sets it. High-power modes on battery follow your battery setting.
- **When the game ends:** the earlier mode comes back only if the laptop is still in the mode LLT set. A mode you chose during the game is kept.
- **If LLT was ended mid-game:** the next start does the same.

## Keyboard listeners for the extra Fn keys

Fn+I, Fn+K and the media keys (Fn+F1 to F3, Fn+F12, Fn+Home/End/PgUp/PgDn) can be given an action in Settings → Special keys. Two listeners make that work: a low-level keyboard hook, and a raw-input listener for the media keys. They only read key presses (and, for a key with an action, keep Windows from also doing that key's own action, such as changing the volume). They change nothing that lasts.

- They are only installed while one of these keys has an action, or while the key discovery window listens.
- **Game-safe mode removes both** while an anti-cheat game runs, like the macro hook, and they come back afterwards. At start they are installed only after game-safe mode has checked once for such a game.

## Game processes are never opened

Kernel anti-cheats (Vanguard, Easy Anti-Cheat, BattlEye) watch for other programs that open the game's process. Indra never does, with or without game-safe mode:
- **Finding games** (game detection, for AI mode and automations): from the names Windows reports when programs start and stop, the process list, the known-games list, the GPU driver's list of programs on the discrete GPU, and Windows Game Mode.
- **File paths** are read from Windows' process list by process id, without opening the process.
- **A game counts as ended** when Windows reports it stopped, or when the process list no longer shows it.
- **The only exception is your own action:** Kill in the discrete GPU window opens the program you chose, to end it.

## Safe start

If Indra's previous start didn't finish (it crashed, froze and was ended, or the laptop went down while it was starting), the next start is a safe start:
- **Skipped once:** God Mode and the Windows power settings, ITS mode, the GPU overclock and locked P-state, AMD overclocking, the first fan-curve apply, and automations that run at start-up. A notification says so, and the dashboard shows "Safe start".
- **Still done:** putting back what a game profile or the background services changed, and anything you do yourself.
- The note behind it, `startup_in_progress`, sits next to the log folder, not with the settings. It is removed 30 seconds after a start finishes and on every normal exit. The next start is normal again.

## Game session report

After each game (one minute or longer), Indra shows a short summary and keeps the last 30 in `game_sessions.json` in the settings folder (not part of backups).
- **It only reads:** temperatures, GPU clock and load (Lenovo and NVIDIA sensors), GPU power when detailed sensors are on and not paused, and FPS when the OSD measures it.
- **A GPU driver error during a game** is shown with the GPU clock, temperature, load and power from up to 5 seconds before it. GPU voltage isn't read, because that would need riskier NVIDIA calls.
- **While it is on, game detection keeps running.** Detection never opens a game's process. You can turn the report off on its card in the Games group.
- **The MSI Afterburner profile** listed with a session is one Indra applied (by an automation or a game profile). A profile you switch in Afterburner itself isn't seen.
- **Export CSV** in the History window writes one file, only where you choose in the Save dialog.

## Freeze watchdog

While Indra runs, a small background thread checks every 2 seconds that Indra's own window still answers.
- **If the window doesn't answer for 15 seconds,** it writes an error report with the last 60 lines of the log to the log folder. When the window answers again, a notification says how long it was stuck.
- **It only watches:** it never ends, restarts or changes anything, in Indra or elsewhere.
- **Sleep isn't a freeze:** a long gap between two checks (sleep, hibernate, a paused process) starts the count again. The watchdog starts after start-up and stops first thing on exit.

## GPU driver errors

While LLT runs, it watches Windows' System log, read only, for NVIDIA driver errors and display driver resets.
- Each error brings a notification with what had just happened: a power mode change, an Afterburner profile LLT applied, the game running.
- The safety report lists the last 24 hours of these errors from the same log.
- LLT changes nothing because of them.

## Health check

The Health page only reads Windows; it changes nothing and sends nothing anywhere.
- It checks the low-battery action, Windows Update, virus protection, security warnings, memory integrity, disabled services, GPU driver errors, disk space and battery wear.
- Each button opens the Windows page where you can change that setting yourself.
- It runs two minutes after LLT starts and then once a day, and notifies only about problems that are new since the last notification. Its state is kept in `health.json` in the settings folder.

## Detailed log

Settings → App behaviour → **Detailed log** is off unless you turn it on.
- It writes to the log folder on this PC only.
- Older log files beyond 50 MB in total are removed when LLT starts.
- Logs can contain program names, Wi-Fi network names and file paths, so read them before sharing.

## If a GPU overclock makes the laptop unstable

- Before LLT applies GPU values that have not yet proven stable, it notes that an apply is in progress. After 60 seconds without a crash the note is removed.
- If LLT starts and finds that note from the previous run, the last apply never finished. LLT then **turns GPU overclock off**, keeps your values so you can lower them, and tells you.
- Limit: a crash that only happens later (for example ten minutes into a game) is not detected this way. The laptop still starts normally, and you can lower the values.
- Manual way out at any time: close LLT and delete `gpu_oc.json` in the settings folder, or start Windows in Safe Mode (LLT does not start there) and do the same.

## When Windows or the driver doesn't answer

Indra waits a limited time for Windows, the Lenovo driver and Windows services: from a few seconds to a minute, depending on the request. If there is no answer by then, Indra reports that the setting couldn't be read or applied and carries on, instead of freezing. A request that was already sent may still finish later; Indra doesn't send a second one on top of it.

## Access Indra gives itself for a moment

Some Windows power settings are protected. To change one, Indra may give your account access to that registry key for the length of the write, and removes it right after (since 2.36.18.0; earlier versions left it in place). Privileges such as changing firmware variables are turned on only while they are needed and turned off afterwards.

## Not reviewed in depth

- The separate AMD overclocking module (`LenovoLegionToolkit.Lib` `Overclocking/Amd`). It only runs on AMD laptops and was outside this review. It does refuse values that can only come from a damaged profile (negative limits, Curve Optimizer values beyond AMD's widest range), but it hasn't been tested on AMD hardware.
- Keyboard RGB and Spectrum lighting. These send lighting data only.
