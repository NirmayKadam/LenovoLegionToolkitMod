# Testing

## Automated tests

The `LenovoLegionToolkit.Tests` project (xUnit) has **565 tests**. Run them with:

```powershell
dotnet test LenovoLegionToolkit.Tests\LenovoLegionToolkit.Tests.csproj -c Release
```

They need Windows (they use real Windows APIs) but no Legion hardware and no administrator rights.

| Area | Folder | What is covered |
|---|---|---|
| Automation | `Automation/` | Local-time trigger matching (incl. UTC+5:30 and midnight-crossing ranges), skipped-second catch-up, pipeline cancellation with real steps, process-name matching |
| CLI | `CLI/` | Pipe messages from 100 B to 200 KB and multi-byte text over real named pipes, client disconnects, per-user pipe name, server identity check, `FirstPipeInstance` |
| Security | `Security/` | Folder ACL checks, plugin discovery, settings migration and permissions, JSON type allow-list (including a live gadget control test), backup inspection, Lenovo signature checks, locked downloads, autostart location, boot-logo GIF validation and re-encoding, certificate validation |
| System | `System/` | Running processes that write large stdout/stderr |
| Settings, Utils, Controllers | | Atomic writes, backups of unreadable settings files (once per content, newest five kept, old temp files removed), Authenticode verification (valid / tampered / unsigned), fan-table mapping, CPU base clock. GPU overclock crash guard over a real `gpu_oc.json` (note saved before apply, next run turns overclock off, stable values, clean exit) and the limits on clock offsets and voltage lock. God Mode range rule: nearest step, default when out of range, offsets can't widen the firmware's range |
| UI | `UI/` | Style palettes: required keys, keys that really exist in WPF-UI, contrast for every style and phosphor, layering over WPF-UI across Light/Dark switches with real WPF-UI calls. Motion-level rules, number formatting, status-strip formatting and "—" for missing readings, selector placement, shared power-mode rules, Retro text helpers. Hover help: section resolution, and **coverage** (every XAML card header, code-built header, dashboard card and item, automation action and trigger has help text; reads the real sources and resources), and the real card header rebuilt several times with help on and off. OSD hotkey parsing/format and the "Toggle OSD" quick action. Game-safe mode: anti-cheat service detection, what it pauses and resumes, driver-free OSD snapshot. Power-mode details: profile building, "—" for missing values, diffs. Fan curves: which curve applies, write/re-check/retry, two mode changes close together (the earlier curve is never written again), failed calls are not "rejected", only the user-started test records the firmware's answer, one Custom-preset fallback per mode. Quick result with the re-check in the background (and a later "rejected" when the firmware undoes it twice), no overlap with the next apply, the test's single check and putting the earlier table back, Apply now without saving. Idle detection without hooks |

> **Smart App Control:** with Windows Smart App Control in enforcement mode, freshly built unsigned DLLs can be refused ("An Application Control policy has blocked this file", 0x800711C7), and the tests that need them fail before running. Which files it blocks varies between runs. Those failures come from Windows, not from the code. Check the Code Integrity event log (`Microsoft-Windows-CodeIntegrity/Operational`, events 3033/3077) for the blocked file names.

## Testing rules used in this fork

1. **Real inputs wherever Windows allows it.** Tests use real files, folders, ACLs, named pipes, child processes, signed binaries, XAML sources and resources. There is no mock that just returns the expected answer (see rule 3 for the hardware exception).
2. **A test is never weakened to make it pass.** When a test failed, the cause was found first, and the evidence is recorded in the test where relevant. In several cases the test itself was wrong (byte offsets, pipe ordering, an encoder that hadn't finished writing); those were fixed without changing what is asserted.
3. **Hardware is never faked in the product.** Code that talks to the firmware, Windows services or input can't be driven in a test, so a few classes take a small injected source in an `internal` constructor: the fan table (`IFanTableAccess`), the current power mode (for the fan-curve controller), the anti-cheat service query, and the last-input time. Settings classes that tests write to (`FanProfileSettings`, `GPUOverclockSettings`) take a folder the same way, so tests use real files without touching your settings. Tests pass scripted firmware/Windows answers through it (each test says so in a comment) to check LLT's *decisions*. The product never uses them; what the real hardware does is on the manual checklist below.
4. **Negative tests prove their reason.** Where a test checks that something is rejected, a paired test shows the same input passes once only that one limit is relaxed, or a control test shows the attack really works without the fix.

## Manual checklist (needs a Legion/LOQ laptop)

These can only be checked on real hardware with an installed (Program Files), elevated build. **None have been completed yet.**

- [ ] The app starts with no red "Unexpected exception" message and the dashboard loads all its groups
- [ ] First start migrates settings to `Program Files\LenovoLegionToolkit\UserData\<SID>`, and all settings (God Mode presets, automations, keyboard) are still there
- [ ] A DLL placed in `%LOCALAPPDATA%\LenovoLegionToolkit\Plugins` is **not** loaded and the one-time notice appears
- [ ] A time trigger set ~2 minutes ahead on today's day fires once, at the right local time
- [ ] CLI (enable it in Settings → Integrations): `llt quickAction --list` works, including with many or non-English quick-action names; killing a client mid-request doesn't break the next command
- [ ] A "Run program" step that prints a lot (with "wait") finishes, and later automations still run
- [ ] Restoring a backup containing a "Run program" automation shows the warning
- [ ] Driver update check (Packages page) still works and lists updates
- [ ] Custom boot logo: a static image and an animated GIF both show at boot
- [ ] Autostart can be enabled from the installed copy and starts LLT at logon; a portable copy shows the "not allowed" message
- [ ] God Mode, power modes, fan curves, Fn Lock and keyboard lighting work as before
- [ ] Each style (Fluent, Neon, Retro with each phosphor) looks right on every page, in dialogs, menus, the tray popup and notifications
- [ ] The status strip shows real temperatures, fans and battery; a sleeping discrete GPU shows "—"
- [ ] The power-mode selector switches modes like the card (including the God Mode settings button and warning); Fn+Q updates both
- [ ] Idle CPU in Task Manager: about 0% with the window minimized in every style; no clear rise over Fluent with it visible
- [ ] With hardware acceleration off, Animations reports Reduced and the glow options say they need the GPU
- [ ] OSD hotkey (e.g. Ctrl+Shift+O) shows and hides the OSD, also inside a full-screen game; a combination another app owns is reported as taken
- [ ] The "Toggle OSD" quick action on a special key shows and hides the OSD
- [ ] Starting Valorant (Vanguard) and an Easy Anti-Cheat game turns game-safe mode on within ~5 s: notification, GAME-SAFE label on the OSD, driver-only values show "—", macros stop; quitting the game turns it all back on. No anti-cheat warning or kick during a session (record the result; no guarantee is claimed)
- [ ] An automation with the user-inactivity trigger still fires after the set idle time and when you come back
- [ ] Power mode details: after Fn+Q, the values change and match God Mode's defaults for that mode; the "now" values agree; missing values show "—"
- [ ] Fan curves: **Test now** result on the LOQ 15IRH8 (record Accepts/Rejects); with Accepts, fan RPM follows the custom curve after Fn+Q; with Rejects, the created Custom preset has the mode's limits and the curve
- [ ] Fan curves, two modes with curves: press Fn+Q quickly several times; the curve in use is the one for the mode you end in
- [ ] Fan curves: sleep and resume do not change the recorded Accepts/Rejects; the curve is back after resume and after restarting LLT
- [ ] Fan curves: turning the feature off brings the firmware's curve back without changing mode
- [ ] Round 1: start and quit games several times over a few days; LLT keeps running (no new "Lenovo Legion Toolkit.exe" crash in the Windows Application log)
- [ ] Round 1: change the custom boot logo once and turn it off once; both show correctly at the next boot
- [ ] Round 1: `error_*.txt` appears in `%LOCALAPPDATA%\LenovoLegionToolkit\log` only when something actually fails
- [ ] LLT in the tray, untouched for 2 minutes: near 0% CPU in Task Manager, memory steady (compare with the 2026-11-14 measurement: 7–17% of one core, 320–380 MB)
- [ ] Dashboard open for 1 minute: CPU clearly lower than before (32–48% of one core); then compare Retro with Fluent
- [ ] Dashboard sensors, status strip, battery page, HWiNFO values and a package download still update; Fan curves window shows its busy ring
- [ ] With a macro set: start and quit Valorant (or an Easy Anti-Cheat game); afterwards the macro works and typing has no lag
- [ ] Start a long repeating macro, then start an Easy Anti-Cheat game: no key or mouse button stays held down
- [ ] Switching to Custom mode is not slower than switching to Balance; the details card's values match the God Mode window
- [ ] Fn+Q with a fan curve set: the details card says "yours" once applied, or "yours wasn't kept" if the firmware refused it
- [ ] Fan curves window: **Save** on the active mode shows its result in about half a second; the controls are locked while it runs
- [ ] Fan curves window: **Test now** with the list on another mode switches to the active mode, names it, finishes in about 4 s, and the fans go back to the previous curve without a visible power mode change
- [ ] Fan curves window: **Apply now** changes the fans at once without saving; closing the window puts the saved curve back
- [ ] Fan curves: **Create Custom preset** clicked twice leaves one preset for that mode in God Mode
- [ ] GPU overclock: a mild value survives an LLT restart after it has been applied for more than a minute
- [ ] GPU overclock crash guard: apply a new value, end LLT from Task Manager within 60 s, start it again: overclock is off, values kept, warning shown (this imitates a crash without causing one)
- [ ] GPU overclock: the voltage lock slider stops at the GPU's own top voltage; the result message matches what happened
- [ ] God Mode: the preset and its fan curve apply as before; no slider goes past the firmware's minimum or maximum; the offset boxes only accept narrowing values
- [ ] Restart check for `docs/SAFETY.md`: set a fan curve and a God Mode limit, restart with LLT not starting, and read the values back: are they the firmware's defaults? (record the answer)
- [ ] Every card's ⓘ shows its help; turning Help on hover off hides them; the delay setting works
- [ ] Settings → Power explainer reads correctly next to the mapping cards
- [ ] Uninstall, answer **Yes** to deleting settings: `Program Files\LenovoLegionToolkit` (including `UserData`) and `%LOCALAPPDATA%\LenovoLegionToolkit` are gone. Reinstall, uninstall, answer **No**: `UserData` is kept, and a reinstall finds the old settings.
