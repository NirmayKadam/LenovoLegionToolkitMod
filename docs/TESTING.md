# Testing

## Automated tests

The `LenovoLegionToolkit.Tests` project (xUnit) has **727 tests**. Run them with:

```powershell
dotnet test LenovoLegionToolkit.Tests\LenovoLegionToolkit.Tests.csproj -c Release
```

They need Windows (they use real Windows APIs) but no Legion hardware and no administrator rights. CI (`.github/workflows/ci.yml`) runs them on every push to `main`, after building with warnings as errors.

**Budget test** (`Performance/StartupBudgetTests.cs`): it builds the app's container from its four real modules, with start-up components skipped so no listener, driver or UI manager starts (a marker component proves that), and resolves a few plain singletons. Budgets: 10 ms and 2 MB, against 1.0 ms and 0.62 MB measured on the LOQ on 2026-11-18. It catches a change that makes this part of start-up much heavier. It does **not** measure the app's real start-up, which is mostly WMI, driver and UI work that needs the laptop.

| Area | Folder | What is covered |
|---|---|---|
| Automation | `Automation/` | Local-time trigger matching (incl. UTC+5:30 and midnight-crossing ranges), skipped-second catch-up, pipeline cancellation with real steps, process-name matching |
| CLI | `CLI/` | Pipe messages from 100 B to 200 KB and multi-byte text over real named pipes, client disconnects, per-user pipe name, server identity check, `FirstPipeInstance` |
| Security | `Security/` | Folder ACL checks, plugin discovery, settings migration and permissions, JSON type allow-list (including a live gadget control test), backup inspection, Lenovo signature checks, locked downloads, autostart location, boot-logo GIF validation and re-encoding, certificate validation |
| System | `System/` | Running processes that write large stdout/stderr |
| Settings, Utils, Controllers | | Every settings file is in the settings backup or left out on purpose (read from the real sources). Atomic writes, backups of unreadable settings files (once per content, newest five kept, old temp files removed), Authenticode verification (valid / tampered / unsigned), fan-table mapping, CPU base clock. The GPU check loop runs only while held or recently read (two holders, reader expiry, refresh before answering a read, a hold racing the stop), survives a failed round (reported once) and ends cleanly when it can't start. Nothing reaches the GPU driver while MSI Afterburner runs (overclock, its zeroing, and the P-state lock). GPU overclock crash guard over a real `gpu_oc.json` (note saved before apply, next run turns overclock off, stable values, clean exit) and the limits on clock offsets and voltage lock. God Mode apply against a scripted laptop: other Lenovo software blocks it, saved values sent masked and in order, out-of-range values get the firmware default, offsets only narrow, unsaved values get the Performance default (or nothing on non-gaming models), the fallback when ranges can't be read, non-gaming cTGP offset and no fan table, a CPU limit failure stops later limits but the fan step still runs, GPU failures skipped, a fan curve not kept tried 3 times, AMD overclock clamped and only with BIOS OC, ranges read once until forgotten. God Mode range rule: nearest step, default when out of range, offsets can't widen the firmware's range. God Mode undo over a real `godmode.json`: the file's bytes come back exactly, a deep copy, the earlier preset sent again only in Custom mode, back to the earlier mode only when the Save switched it, nothing after a Fn+Q, an unreadable mode reported |
| UI | `UI/` | Style palettes: required keys, keys that really exist in WPF-UI, contrast for every style and phosphor, layering over WPF-UI across Light/Dark switches with real WPF-UI calls. Motion-level rules, number formatting, status-strip formatting and "—" for missing readings, selector placement, shared power-mode rules, Retro text helpers. Every icon name in the XAML exists in WPF-UI (an unknown one only fails when its window opens). Hover help: section resolution, and **coverage** (every XAML card header, code-built header, dashboard card and item, automation action and trigger has help text; reads the real sources and resources), and the real card header rebuilt several times with help on and off. OSD hotkey parsing/format and the "Toggle OSD" quick action. First-run tour: every step's text, page and Settings category exist (read from the real sources), shown once and not on a tray start. Safety report: what each state says, what is flagged, "couldn't read" instead of a guess, the copied text. OSD frame-time graph: the history keeps the last 5 s (clock supplied by the test), ignores values that aren't frame times, holds its capacity in time order, survives adding while reading, and is cleared when FPS measuring stops; the layout's 30 fps floor and 100 ms cap, stutters over twice the median, nothing drawn below two frames, oldest left to newest right. Game profiles (scripted processes and services): apply and put back exactly, the game's process never touched, off-limits apps (Windows' own shell apps by name, whatever their path), an app in both lists only closed, the app picker dropping names before looking up any path, protected and unstoppable services, a reused process id, two game instances, recovery after LLT was ended, a profile forcing game-safe mode. Game-safe mode: anti-cheat service detection, the check running only while the setting is on, a failed pause reported with the resume still running, what it pauses and resumes, driver-free OSD snapshot. Retro text glow: only Retro with the glow on and GPU effects defines it, and a real title follows it as the style changes. Power-mode details: profile building, "—" for missing values, diffs. Fan curves: going back to the firmware's curve (one mode or all, re-applied only when in use, never written back by a pending check), which curve applies, write/re-check/retry, two mode changes close together (the earlier curve is never written again), failed calls are not "rejected", only the user-started test records the firmware's answer, one Custom-preset fallback per mode. Fan-curve undo: the earlier curve saved and written on the active mode, saved but not written for another mode, a curve added to the active mode undone by re-applying the mode, a reset of every mode undone, the snapshot is a copy, a curve not kept and an unreadable mode reported. The undo slot: 30 s (clock supplied by the test), a newer change replaces the older, runs once, failures reported. Quick result with the re-check in the background (and a later "rejected" when the firmware undoes it twice), no overlap with the next apply, the test's single check and putting the earlier table back, Apply now without saving. Idle detection without hooks |

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
- [ ] Round 2: Retro (each phosphor) with Phosphor glow on: page, group and card titles and the console line glow; sensor values don't; turning the glow off removes it at once
- [ ] Round 2: dashboard open for 1 minute in Retro with glow on: CPU clearly below the 13% measured on 2026-11-14; also with glow off, and with Animations = Reduced (to see the CRT loops' share)
- [ ] Round 2: with game-safe mode off, the log shows no anti-cheat check; turning it on in Settings starts it, and game-safe still switches on within ~5 s of starting Valorant or an Easy Anti-Cheat game
- [ ] Round 2: the dGPU card on the dashboard keeps updating (active/inactive, processes) over a few minutes
- [ ] Round 2: with trace logging on, close the dashboard (tray only, OSD off): the log shows "GPU check stopped: nothing needs it" within ~15 s; opening the dashboard starts it again and the dGPU card and GPU sensors update
- [ ] Round 2: on battery with the dashboard closed, the dGPU goes to sleep (dGPU card shows powered off when reopened); GPU sensors show "—" for it rather than waking it
- [ ] Round 2: changing "GPU monitoring interval" in the dGPU window changes how often the card updates
- [ ] Round 2: a game-trigger automation with dGPU game detection still detects a game started on the dGPU
- [ ] Round 3: Fan curves window, **Use firmware curve** on Balance (with your curve set, laptop in Balance): the fans go back to the firmware's behaviour without a visible mode change, and the details card says "firmware"; **Reset all modes** removes every curve; the details card's **Use firmware curve** does the same for the shown mode
- [ ] Round 3: Power mode profiles (details card → Profiles…, Settings → Power): each tab shows the right fan status and, for your "Windows power plan" mapping, the plan cards; choosing a plan for the active mode switches the Windows plan at once; **Apply now** on the active mode; the Custom tab opens God Mode settings
- [ ] Round 3: the Windows power plans and Windows power modes windows still show and save every mode, the Custom tab(s) and (on ITS laptops) the ITS modes as before; their "Power mode profiles…" button opens the new window
- [ ] Round 3: Game profiles (Settings → App behaviour): add a game; add one app to ease, one to close and one allowed service (e.g. InventorySvc). The first enable asks. Spooler, GamingServices and the anti-cheat services show as protected and can't be added
- [ ] Round 3: start the game: notification says what changed; Task Manager shows the eased app in efficiency mode with below-normal priority; the closed app asked to close; the service is stopped. Quit the game: notification; the app's priority and efficiency mode are back; the service runs again
- [ ] Round 3: with game-safe mode off in Settings and "Game-safe mode" on in the game's profile: the GAME-SAFE label and notification appear while the game runs, and go when it ends
- [ ] Round 3: start the profiled game, end LLT in Task Manager, start LLT again: the service runs again and the app's priority is back (with the game still running, the profile then applies again)
- [ ] Round 4: with MSI Afterburner running and your undervolt applied: the GPU overclock card and its window say Afterburner is running; switching LLT's GPU overclock on and off leaves the undervolt untouched (check the curve in Afterburner); the dGPU window's P-state card shows the note
- [ ] Round 4: OSD settings → Game → turn on **Frame-time graph**, then play: a few seconds after the game starts the graph moves, stutters show in the critical colour, and turning the item off leaves the FPS header in place. Check it in both OSD styles (panel and bar). With the OSD hidden, LLT's CPU in Task Manager is as before
- [ ] Round 5: God Mode: change one value (e.g. CPU long-term limit), **Save**, then **Undo** within 30 s: the window shows the old value and the power-mode details card agrees. From Balance, **Save & close** (switches to Custom), then Undo on the main window: the laptop is back in Balance
- [ ] Round 5: Fan curves: change the active mode's curve, **Save**, **Undo**: RPM follows the old curve again. **Use firmware curve** on the dashboard card, then Undo: your curve is back and in use. Leave a change alone for 30 s: the bar goes and the change stays
- [ ] Round 5: after installing, the Quick tour opens once on a normal start (not when LLT starts in the tray); **Show me** on each step opens the right page; it doesn't open at the next start; Settings → App behaviour → Quick tour opens it again
- [ ] Round 5: Safety report (Settings → App behaviour, and About): with MSI Afterburner running it says so under GPU overclock; in Valorant, game-safe mode shows "On now" with `vgc`; the settings folder is the protected `UserData` one with nothing flagged; **Copy as text** pastes the report
- [ ] Round 5: Settings backup lists `fan_profiles.json` and `game_profiles.json`; a backup restored on a fresh settings folder brings your fan curves and game profiles back
- [ ] Round 4: Vanguard: quit Valorant; within a minute the `vgc` service stops and the OSD's GAME-SAFE label and the notification go
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
