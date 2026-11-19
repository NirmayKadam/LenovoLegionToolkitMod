# Safety: what LLT writes, and how to undo it

This page lists every kind of change Lenovo Legion Toolkit (LLT) can make to the laptop's firmware, GPU and boot files, whether it survives a restart, what limits the value, and how to get back.

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
| LLT's own settings (God Mode presets, fan curves, GPU overclock values, automations) | Files in `Program Files\LenovoLegionToolkit\UserData\<SID>` | See the rows below for what is sent to the hardware | Change them in LLT, or delete the file while LLT is closed |

## Changes expected to be gone after a restart

These are sent to the running firmware or GPU driver. LLT applies them again when it starts or when the power mode changes; without LLT running, the laptop is expected to use its own defaults.

| Change | What limits it | How to undo |
|---|---|---|
| Power mode (Quiet / Balance / Performance / Custom) | Fixed list of modes | Fn+Q, or LLT |
| God Mode: CPU and GPU power limits, temperature limits | Each value is checked against the firmware's own minimum, maximum and steps **every time it is applied** (the ranges are read once per run and again after sleep). A setting the preset doesn't save gets the firmware's own Performance (or Extreme) default on current models. The "min/max value offset" fields can only narrow that range. When the ranges can't be read, your saved values are not sent; the firmware's own Performance (or Extreme) default for each setting is sent instead | Switch to another power mode; the firmware loads that mode's own limits |
| Fan curve (Custom mode, and per-mode fan curves) | Never below the laptop's minimum curve, never above level 10. Every write is read back. A curve is only written for the mode the laptop is in at that moment | Switch power mode, or turn per-mode fan curves off; the firmware's curve returns. **Use firmware curve** (Fan curves window, or the power-mode details card) removes your curve for one mode or all modes and, if that curve is in use, applies the same power mode again; it writes no fan table itself |
| Fan full speed | On or off | Turn it off in LLT, or switch power mode |
| GPU clock offsets (core, memory) | LLT's fixed limits, and inside the range the NVIDIA driver reports for this GPU (per clock). When the driver allows only one value (e.g. 0 when overclocking is locked), only that value is sent. **Nothing is sent while MSI Afterburner runs**, not even the zero offsets of switching it off, and the same goes for the voltage lock/cap and LLT's P-state lock | Turn GPU overclock off in LLT, or restart with LLT closed |
| GPU voltage lock / voltage cap | Never above the highest voltage on the GPU's own stock voltage/frequency curve. If that curve cannot be read, the voltage lock is not applied | Set it to Off in LLT, or restart with LLT closed |
| GPU power target offset (PCF) | Firmware-reported range, as for God Mode | Switch power mode; LLT resets it |
| AMD Precision Boost Overdrive / Curve Optimizer (AMD models only, and only when BIOS overclocking is enabled) | Fixed ranges (scaler 0–7, boost 0–200 MHz, curve 0–20), checked every time they are applied | Turn overclocking off in the God Mode preset |

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
| Stopping listed services | Only running Win32 services that accept a stop and that no running service depends on (LLT never stops a cascade). A fixed list (Windows core, audio, network, security, Windows Update, input and display, NVIDIA display drivers, Lenovo's services, anti-cheats, Gaming Services, Print Spooler) can't be added | Started again when the game ends, when LLT exits, or at LLT's next start if it was ended mid-game. A service already running again is left as it is. Without LLT: a restart starts automatic services again |
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
| Stopping the services of the presets you choose (telemetry, SysMain, Search indexing, unused extras, Xbox) while LLT runs | The same rules as game profiles: the protected list (62 services), drivers, services that can't stop and services others depend on are never stopped. Each stop is written to `background_services_session.json` first. **Windows' own service settings (startup type) are never changed** | Started again when LLT exits, when a preset is unticked or the feature turned off ("Start them all again"), and at LLT's next start if it was ended. A service something else stopped, or a game profile paused, is never started by it. Without LLT: a restart starts them as usual |

## If a GPU overclock makes the laptop unstable

- Before LLT applies GPU values that have not yet proven stable, it notes that an apply is in progress. After 60 seconds without a crash the note is removed.
- If LLT starts and finds that note from the previous run, the last apply never finished. LLT then **turns GPU overclock off**, keeps your values so you can lower them, and tells you.
- Limit: a crash that only happens later (for example ten minutes into a game) is not detected this way. The laptop still starts normally, and you can lower the values.
- Manual way out at any time: close LLT and delete `gpu_oc.json` in the settings folder, or start Windows in Safe Mode (LLT does not start there) and do the same.

## Not reviewed in depth

- The separate AMD overclocking module (`LenovoLegionToolkit.Lib` `Overclocking/Amd`). It only runs on AMD laptops and was outside this review.
- Keyboard RGB and Spectrum lighting. These send lighting data only.
