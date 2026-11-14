# Safety: what LLT writes, and how to undo it

This page lists every kind of change Lenovo Legion Toolkit (LLT) can make to the laptop's firmware, GPU and boot files, whether it survives a restart, what limits the value, and how to get back.

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
| God Mode: CPU and GPU power limits, temperature limits | Each value is checked against the firmware's own minimum, maximum and steps **every time it is applied** (the ranges are read once per run and again after sleep). Only settings the preset saves are sent. The "min/max value offset" fields can only narrow that range. A value whose range cannot be read is not sent | Switch to another power mode; the firmware loads that mode's own limits |
| Fan curve (Custom mode, and per-mode fan curves) | Never below the laptop's minimum curve, never above level 10. Every write is read back. A curve is only written for the mode the laptop is in at that moment | Switch power mode, or turn per-mode fan curves off; the firmware's curve returns |
| Fan full speed | On or off | Turn it off in LLT, or switch power mode |
| GPU clock offsets (core, memory) | LLT's fixed limits, and inside the range the NVIDIA driver reports for this GPU (per clock). When the driver allows only one value (e.g. 0 when overclocking is locked), only that value is sent | Turn GPU overclock off in LLT, or restart with LLT closed |
| GPU voltage lock / voltage cap | Never above the highest voltage on the GPU's own stock voltage/frequency curve. If that curve cannot be read, the voltage lock is not applied | Set it to Off in LLT, or restart with LLT closed |
| GPU power target offset (PCF) | Firmware-reported range, as for God Mode | Switch power mode; LLT resets it |
| AMD Precision Boost Overdrive / Curve Optimizer (AMD models only, and only when BIOS overclocking is enabled) | Fixed ranges (scaler 0–7, boost 0–200 MHz, curve 0–20), checked every time they are applied | Turn overclocking off in the God Mode preset |

## If a GPU overclock makes the laptop unstable

- Before LLT applies GPU values that have not yet proven stable, it notes that an apply is in progress. After 60 seconds without a crash the note is removed.
- If LLT starts and finds that note from the previous run, the last apply never finished. LLT then **turns GPU overclock off**, keeps your values so you can lower them, and tells you.
- Limit: a crash that only happens later (for example ten minutes into a game) is not detected this way. The laptop still starts normally, and you can lower the values.
- Manual way out at any time: close LLT and delete `gpu_oc.json` in the settings folder, or start Windows in Safe Mode (LLT does not start there) and do the same.

## Not reviewed in depth

- The separate AMD overclocking module (`LenovoLegionToolkit.Lib` `Overclocking/Amd`). It only runs on AMD laptops and was outside this review.
- Keyboard RGB and Spectrum lighting. These send lighting data only.
