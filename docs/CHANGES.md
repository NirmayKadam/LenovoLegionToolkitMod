# Changes from Lenovo Legion Toolkit v2.36.0.0

This file is the modification notice required by GPL-3.0 section 5(a).

- **Original work:** Lenovo Legion Toolkit v2.36.0.0, © Bartosz Cichecki, Kaguya, Dr. Skinner and contributors, licensed under GPL-3.0. Source: https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/releases/tag/v2.36.0.0
- **Modified by:** Nirmay Kadam
- **Date of modifications:** 2026-11-07 (bug and security fixes); 2026-11-08 (UI styles, animations, status strip)
- **License of this modified version:** GPL-3.0 (see [LICENSE](../LICENSE))

What changed and why: [BUG-FIXES.md](BUG-FIXES.md), [SECURITY.md](SECURITY.md) and [SKINS.md](SKINS.md).

## Modified files

### Build and CI
- `Directory.Build.props`: removed the Roslyn satellite cleanup target (the scripting package is gone)
- `LenovoLegionToolkit.sln`: added the test project
- `LenovoLegionToolkit.Lib/LenovoLegionToolkit.Lib.csproj`: pinned `LLT.*` packages; removed `Microsoft.CodeAnalysis.CSharp.Scripting`; `InternalsVisibleTo` for tests
- `LenovoLegionToolkit.Lib.Automation/LenovoLegionToolkit.Lib.Automation.csproj`, `LenovoLegionToolkit.CLI.Lib/LenovoLegionToolkit.CLI.Lib.csproj`: `InternalsVisibleTo` for tests
- `LenovoLegionToolkit.WPF/LenovoLegionToolkit.WPF.csproj`: removed `AvalonEdit`
- `.github/workflows/build.yml`: unsigned PR builds without secrets; actions pinned to SHAs
- `.github/workflows/release.yml`, `dev-release.yml`, `stale.yml`: actions pinned to SHAs
- `sign_installer.ps1`: skips signing when `LLT_UNSIGNED_BUILD=1`
- `.gitignore`: `docs/` is no longer ignored

### Library (`LenovoLegionToolkit.Lib`)
- `AutoListeners/TimeAutoListener.cs`: skipped-second catch-up
- `Controllers/GodMode/GodModeController.cs`: fan-table mapping cleanup, capability-error reset, inner exceptions, named constant
- `Controllers/Sensors/AbstractSensorsController.cs`: CPU count cap removed
- `Controllers/SmartFnLockController.cs`: left/right modifiers, safe dispose
- `Controllers/SpectrumKeyboardBacklightController.cs`: JSON type allow-list
- `Extensions/TimeExtensions.cs`: local-time helpers
- `HttpClientFactory.cs`: removed accept-any-certificate
- `IoCModule.cs`: removed script engine registration
- `Overclocking/Amd/AmdOverclockingController.cs`: protected settings folder
- `PackageDownloader/Detectors/Rules/ExternalDetectionRule.cs`: fresh, locked, Lenovo-signed helpers
- `Settings/AbstractSettings.cs`: atomic writes, protected folder, JSON type allow-list
- `Settings/ApplicationSettings.cs`: legacy plugin notice flag
- `System/Autorun.cs`: protected-location requirement
- `System/BootLogo.cs`: always re-encode static images, validate GIFs
- `System/CMD.cs`: output deadlock fix
- `Utils/AppFlags.cs`: protected folder; `--proxy-allow-all-certs` ignored
- `Utils/Compatibility.cs`: protected folder
- `Utils/Folders.cs`: protected per-user settings folder and migration
- `Utils/UpdateChecker.cs`: WinVerifyTrust, GitHub path verification, locked download

### Automation (`LenovoLegionToolkit.Lib.Automation`)
- `AutomationProcessor.cs`: working cancellation, exception safety, local-time events
- `IAutomationEvent.cs`: local-time event
- `Pipeline/AutomationPipeline.cs`: interruptions aren't failures
- `Pipeline/Triggers/PeriodicAutomationPipelineTrigger.cs`, `TimeAutomationPipelineTrigger.cs`, `TimeRangeAutomationPipelineTrigger.cs`: local-time matching; `Equals` fix
- `Steps/RunAutomationStep.cs`: exact process-name match

### CLI
- `LenovoLegionToolkit.CLI.Lib/Constants.cs`: per-user pipe name
- `LenovoLegionToolkit.CLI.Lib/Extensions/PipeStreamExtensions.cs`: correct message reading
- `LenovoLegionToolkit.CLI/IpcClient.cs`: server identity check

### App (`LenovoLegionToolkit.WPF`)
- `App.xaml.cs`: legacy plugin notice; proxy setup
- `CLI/IpcServer.cs`: resilient server, current-user ACL, `FirstPipeInstance` with backoff
- `Controls/Settings/SettingsAppBehaviorControl.xaml(.cs)`: script console removed; autostart message; settings reset in the new folder
- `Flags.cs`, `Windows/Utils/ArgumentWindow.xaml.cs`: protected folder; allow-all-certs removed
- `Pages/AboutPage.xaml.cs`, `Utils/LocalizationHelper.cs`, `Station/Core/ExtensionContext.cs`: protected folder
- `Station/Core/ExtensionManager.cs`: plugins only from the install folder
- `Resources/Resource.resx`, `Resources/Resource.Designer.cs`: new English strings; script console strings removed
- `Windows/Settings/SettingsBackupWindow.xaml.cs`: protected folder; restore warning
- `Windows/Utils/UpdateWindow.xaml.cs`: locked installer launch

## Added files
- `LenovoLegionToolkit.Lib/Utils/DirectoryProtection.cs`, `SafeSerializationBinder.cs`, `AuthenticodeVerifier.cs`, `LockedFile.cs`
- `LenovoLegionToolkit.Lib/Station/Core/PluginDiscovery.cs`
- `LenovoLegionToolkit.Lib/System/GifValidator.cs`
- `LenovoLegionToolkit.Lib.Automation/Pipeline/Triggers/TimeTriggerMatching.cs`
- `LenovoLegionToolkit.Lib.Automation/Utils/BackupInspector.cs`
- `LenovoLegionToolkit.CLI.Lib/PipeServerIdentity.cs`
- `LenovoLegionToolkit.Tests/` (entire test project)
- `docs/SECURITY.md`, `docs/BUG-FIXES.md`, `docs/TESTING.md`, `docs/BUILDING.md`, `docs/CHANGES.md`

## Removed files
- `LenovoLegionToolkit.Lib/Scripting/ScriptEngine.cs`, `ScriptSecurityValidator.cs`
- `LenovoLegionToolkit.WPF/Windows/Utils/ScriptWindow.xaml`, `ScriptWindow.xaml.cs`

## Moved files
- `README.md`, `README_ja-JP.md`, `README_ru-RU.md`, `README_zh-hans.md` → `docs/upstream/` (relative links adjusted). The root `README.md` was replaced with this fork's README.

## UI styles, animations and status strip (2026-11-08)

### Modified files
- `LenovoLegionToolkit.Lib/Enums.cs`: `UiSkin`, `RetroPhosphor`, `AnimationLevel`
- `LenovoLegionToolkit.Lib/Settings/ApplicationSettings.cs`: `Skin`, `NeonGlow`, `RetroPhosphor`, `RetroScanlines`, `RetroGlow`, `Animations`, `ShowStatusStrip`
- `LenovoLegionToolkit.Lib/Resources/Resource.resx`, `Resource.Designer.cs`: display names for the new enums
- `LenovoLegionToolkit.WPF/App.xaml.cs`: motion level initialized at startup
- `LenovoLegionToolkit.WPF/Utils/ThemeManager.cs`: applies the selected style (resource overrides, accent, backdrop for open windows); High Contrast forces Fluent
- `LenovoLegionToolkit.WPF/Windows/BaseWindow.cs`: font from the style; backdrop off for solid styles
- `LenovoLegionToolkit.WPF/Windows/MainWindow.xaml(.cs)`: page transitions follow the motion level; Neon title-bar sweep; Retro CRT layer, phosphor glow and warm-up; solid background for solid styles
- `LenovoLegionToolkit.WPF/Styles/CardControl.xaml`, `CardAction.xaml`: hover lift
- `LenovoLegionToolkit.WPF/Styles/NavigationStore.xaml`: glow on the active item's marker
- `LenovoLegionToolkit.WPF/Pages/DashboardPage.xaml(.cs)`: status strip and selector; staggered entrance
- `LenovoLegionToolkit.WPF/Windows/Dashboard/EditDashboardWindow.xaml(.cs)`: status strip toggle
- `LenovoLegionToolkit.WPF/Controls/Dashboard/PowerModeControl.cs`: uses the shared `PowerModeActions` (behavior unchanged)
- `LenovoLegionToolkit.WPF/Controls/Settings/SettingsAppearanceControl.xaml(.cs)`: Style, Animations, Glow, Phosphor and CRT effects cards; notes on settings a style overrides
- `LenovoLegionToolkit.WPF/Extensions/PowerModeStateExtensions.cs`: notification and tray colors follow Neon and Retro; Fluent keeps the original colors
- `LenovoLegionToolkit.WPF/Resources/Resource.resx`, `Resource.Designer.cs`: new English strings
- `LenovoLegionToolkit.Tests/LenovoLegionToolkit.Tests.csproj`: references the WPF project for UI helper tests
- `README.md`: styles section and docs link

### Added files
- `LenovoLegionToolkit.WPF/Utils/Skins/SkinPalette.cs`, `SkinGlow.cs`, `RetroText.cs`, `ColorContrast.cs`
- `LenovoLegionToolkit.WPF/Utils/Motion.cs`
- `LenovoLegionToolkit.WPF/Controls/Dashboard/StatusStripControl.xaml(.cs)`, `StatusStripFormat.cs`, `PowerModeSelectorControl.xaml(.cs)`, `PowerModeActions.cs`
- `LenovoLegionToolkit.Tests/UI/` (`SkinPaletteTests`, `MotionTests`, `StatusStripTests`, `RetroTextTests`, `StaRunner`)
- `docs/SKINS.md`

## Installer fix (2026-11-08)
- `make_installer.iss`: when the user chooses to delete settings at uninstall, also delete the protected settings folder `<install folder>\UserData` (added by the security fixes, previously left behind), and the install folder if it's then empty

## Documentation (2026-11-08)
- Added `docs/README.md` (index), `docs/USER-GUIDE.md`, `docs/ARCHITECTURE.md`, `docs/CODEBASE-GUIDE.md`
- `README.md`: documentation table points to the new guides

## OSD quick toggle, game-safe mode, hover help, power-mode details, fan curves (2026-11-13)

### Modified files
- **Library**
  - `LenovoLegionToolkit.Lib/Settings/ApplicationSettings.cs`: `ShowHelpTooltips`, `HelpTooltipDelayMs`, `GameSafeMode`, `PowerModeDetailsExpanded`
  - `LenovoLegionToolkit.Lib/Settings/OsdSettings.cs`: `ToggleHotkey`
  - `LenovoLegionToolkit.Lib/Settings/NotificationSettings.cs`, `Enums.cs`: OSD shown/hidden and game-safe on/off notifications (new enum values added at the end)
  - `LenovoLegionToolkit.Lib/Settings/AbstractSettings.cs`: optional folder constructor, so tests never write the real settings folder
  - `LenovoLegionToolkit.Lib/Controllers/Sensors/SensorsGroupController.cs`: `IsPaused`/`SetPaused` (no driver reads while paused)
  - `LenovoLegionToolkit.Lib/Controllers/Sensors/FpsSensorController.cs`: finds the game in a `Process.GetProcesses()` snapshot instead of opening it with `GetProcessById`
  - `LenovoLegionToolkit.Lib/Features/HardwareSensorsFeature.cs`: turning sensors off no longer turns the OSD off
  - `LenovoLegionToolkit.Lib/Listeners/NativeWindowsMessageListener.cs`: `SetKeyboardHookSuspendedAsync`
  - `LenovoLegionToolkit.Lib/AutoListeners/UserInactivityAutoListener.cs`: low-level keyboard and mouse hooks replaced by `GetLastInputInfo` polling; idle steps still come from a 10 s timer
  - `LenovoLegionToolkit.Lib/NativeMethods.txt`: `GetLastInputInfo`
  - `LenovoLegionToolkit.Lib/Controllers/GodMode/GodModeController.cs`: `IsValidFanTableAsync` made public
  - `LenovoLegionToolkit.Lib/IoCModule.cs`: registrations for the new services
  - `LenovoLegionToolkit.Lib.Macro/MacroController.cs`, `Utils/MacroPlayer.cs`: `Suspend`/`Resume`, `Stop`
- **App**
  - `LenovoLegionToolkit.WPF/Controls/CardHeaderControl.cs`: `HelpKey` and the ⓘ help tooltip
  - Feature-card, automation-step, God Mode field, RGB keyboard and edit-dashboard base controls: default or forwarded `HelpKey`
  - About 45 XAML pages, windows and controls: `HelpKey` on every card header; OSD settings headers became `CardHeaderControl`s
  - Automation, dashboard-item, special-key, ITS-loop and power-mapping windows: help keys for cards built in code; `AutomationPipelineTriggerConfigurationWindow` drops an unused header
  - `LenovoLegionToolkit.WPF/Controls/Settings/SettingsAppBehaviorControl.xaml(.cs)`: Help on hover, game-safe mode; the OSD card is always available
  - `LenovoLegionToolkit.WPF/Controls/Settings/SettingsPowerControl.xaml(.cs)`: explainer for power mode / Windows power plan / Windows power mode, clearer wording, fan curves card
  - `LenovoLegionToolkit.WPF/Windows/Osd/OsdSettingsWindow.xaml(.cs)`: hotkey and Fn-key cards
  - `LenovoLegionToolkit.WPF/Windows/Osd/OsdWindowBase.cs`, `OsdPanelWindow.xaml`, `OsdBarWindow.xaml`: driver-free snapshot while paused or game-safe; GAME-SAFE label
  - `LenovoLegionToolkit.WPF/Pages/DashboardPage.xaml(.cs)`: power mode details card
  - `LenovoLegionToolkit.WPF/Utils/NotificationsManager.cs`, `Windows/Settings/NotificationsSettingsWindow.xaml(.cs)`: the new notifications
  - `LenovoLegionToolkit.WPF/App.xaml.cs`: help settings, hotkey service, game-safe mode and fan profiles started at launch
  - `LenovoLegionToolkit.WPF/Resources/Resource.resx`, `Resource.Designer.cs`: new strings and about 430 help texts (English)

### Added files
- `LenovoLegionToolkit.Lib/Utils/AntiCheatMonitor.cs`, `LenovoLegionToolkit.WPF/Utils/GameSafeMode.cs`
- `LenovoLegionToolkit.Lib/Settings/HotkeyBinding.cs`, `LenovoLegionToolkit.WPF/Utils/GlobalHotkeyService.cs`, `HotkeyGesture.cs`
- `LenovoLegionToolkit.Lib.Automation/QuickActionTemplates.cs`
- `LenovoLegionToolkit.Lib/System/SystemMemory.cs`, `LenovoLegionToolkit.WPF/Windows/Osd/OsdSnapshotBuilder.cs`
- `LenovoLegionToolkit.Lib/Controllers/PowerModeProfileReader.cs`, `LenovoLegionToolkit.WPF/Controls/Dashboard/PowerModeDetailsControl.xaml(.cs)`, `PowerModeDetailsFormat.cs`
- `LenovoLegionToolkit.Lib/Settings/FanProfileSettings.cs`, `LenovoLegionToolkit.Lib/Controllers/FanProfileController.cs`, `LenovoLegionToolkit.WPF/Windows/Dashboard/FanCurvesWindow.xaml(.cs)`
- `LenovoLegionToolkit.WPF/Controls/Help/HelpContent.cs`, `HelpPresenter.cs`
- Tests in `LenovoLegionToolkit.Tests/UI/`: `HelpContentTests`, `HelpCoverageTests`, `OsdToggleTests`, `GameSafeModeTests`, `PowerModeDetailsTests`, `FanProfileTests`, `UserInactivityListenerTests`, `CardHeaderControlTests`

### Fixes found on the first installed build
- `LenovoLegionToolkit.WPF/Controls/CardHeaderControl.cs`: the ⓘ was wrapped in a new container on every title rebuild, which threw "Specified element is already the logical child of another element" the second time. The container is now made once and reused.
- `LenovoLegionToolkit.WPF/Controls/Dashboard/DashboardGroupControl.cs`: a group now always reports that it has finished, even when one of its cards fails to build. The error is still shown and logged; before, the dashboard stayed on the loader forever.

## Safety fixes (2026-11-13)

A review of everything that writes to the firmware and the GPU. Found by reading the code; **not yet checked on a laptop**. `docs/SAFETY.md` lists every write and how to undo it.

### Per-mode fan curves
- `LenovoLegionToolkit.Lib/Controllers/FanProfileController.cs`
  - Only one apply runs at a time; a newer mode change cancels the running one. Before every write and every verdict the current power mode is re-read, and the apply stops (`Superseded`) if the laptop has left the curve's mode. Before, pressing Fn+Q twice quickly could write the first mode's curve after the laptop was already in the second.
  - `IFanTableAccess.WriteVerifiedAsync` returns `Kept` / `NotKept` / `Error`. A failed call is reported as `Failed`, no longer as "the firmware rejects curves".
  - Whether the firmware supports curves is saved only by **Test now** (`ProbeAsync`). Before, any failed apply (a mode change still settling, the laptop going to sleep) could save "rejects" for good and silently switch the feature off.
  - The current mode's curve is also applied at start and after resume.
  - `UpsertFallbackPreset`: the Custom preset made for a mode replaces the one made earlier. Before, every click added another preset.
  - `BuildCustomPreset`: a reported 0 is used where 0 is allowed; settings with a fixed list of steps land on a step (they were all turned into 0 before); the template's Windows power plan/mode choices are not copied.
- `LenovoLegionToolkit.Lib/Controllers/GodMode/GodModeController.cs`: `FanTableNotKeptException` for a read-back mismatch.
- `LenovoLegionToolkit.Lib/Settings/FanProfileSettings.cs`: `FallbackPresetIds`.
- `LenovoLegionToolkit.WPF/Windows/Dashboard/FanCurvesWindow.xaml.cs`: the on/off switch takes effect at once (on: applies the curve; off: brings the firmware's curve back); messages for the two new results.

### GPU overclock
- `LenovoLegionToolkit.Lib/Controllers/OverclockApplyGuard.cs` (new), `GPUOverclockController.cs`, `LenovoLegionToolkit.Lib/Settings/GPUOverclockSettings.cs`
  - Crash guard: values that haven't proven stable are noted in `gpu_oc.json` before they are applied, and cleared after 60 s or on a normal exit. A start that finds the note turns the overclock off and keeps the values. Before, values that froze the laptop were applied again at every start, resume and power-mode change.
  - Clock offsets are clamped to the range the NVIDIA driver reports, inside LLT's fixed limits.
  - The voltage lock is capped at the top of the GPU's own voltage/frequency curve and not sent when that can't be read. Before, any value up to 1200 mV was sent.
  - `ApplyStateAsync` returns what happened. Saved values are no longer wiped when an apply fails.
- `LenovoLegionToolkit.WPF/Windows/Dashboard/OverclockDiscreteGPUSettingsWindow.xaml.cs`: shows the real result; the voltage lock slider stops at the GPU's limit.
- `LenovoLegionToolkit.WPF/Controls/Dashboard/OverclockDiscreteGPUControl.cs`: warning when the overclock was turned off for safety.
- `LenovoLegionToolkit.WPF/App.xaml.cs`: tells the guard about a normal exit or Windows shutdown.

### God Mode
- `LenovoLegionToolkit.Lib/Controllers/GodMode/GodModeController.cs`
  - Values sent to the firmware come from the range-checked preset (`GetRangeCheckedPresetAsync`), not raw from `godmode.json`. A value whose range can't be read is not sent.
  - Min/max value offsets can only narrow a range. Before, they could push a limit past the firmware's own minimum or maximum.
  - A limit that fails to set no longer skips the fan step; the error is raised afterwards.
  - AMD overclocking values are held to their fixed ranges when applied.
- `LenovoLegionToolkit.WPF/Windows/Dashboard/GodModeSettingsWindow.xaml`: the offset boxes accept only narrowing values; English texts updated (other languages still show the old wording).

### Settings files
- `LenovoLegionToolkit.Lib/Settings/AbstractSettings.cs`: an unreadable settings file is backed up once per distinct content (before: again at every start), at most 5 backups are kept, backups go next to the file, and leftover `.tmp` files older than a day are removed on save.

### Added files
- `docs/SAFETY.md`
- Tests: `Tests/Controllers/OverclockApplyGuardTests.cs`, `Tests/Controllers/GodModeRangeTests.cs`, `Tests/Settings/SettingsBackupTests.cs`; `Tests/UI/FanProfileTests.cs` extended

### Tests changed on purpose
- `FanProfileTests`: `Apply_CurveStays_…` and `Apply_FirmwarePutsItsCurveBackTwice_…` asserted that a normal apply remembers the firmware's answer. That behavior was the bug, so they now assert the answer stays unknown, and the "remembers" checks moved to the `Probe_…` tests.

## Faster fan-curve Save and Test, Apply now (2026-11-13)

Reported on the LOQ: Save and Test now were slow and showed nothing while working.

- `LenovoLegionToolkit.Lib/Controllers/FanProfileController.cs`
  - An apply reports as soon as the immediate read-back matches (about 0.5 s instead of 3.5–7 s). The 3 s re-check, the one rewrite and the "firmware put its own curve back" report now happen in the background (`WatchAsync`), still holding the one-at-a-time lock, so applies never overlap.
  - Test now (`ProbeAsync`) checks once instead of retrying (no mode change happens during a test), and returns `FanCurveProbeResult`. It puts back the table that was there before the test instead of re-applying the whole power mode; it falls back to a power-mode re-apply only when that table can't be read or written back or is below LLT's minimum.
  - `PreviewAsync` for Apply now.
- `LenovoLegionToolkit.WPF/Windows/Dashboard/FanCurvesWindow.xaml(.cs)`
  - Controls are locked while an action runs, with a progress ring and a status line for the result (and for a later revert found by the background check).
  - Test now always tests the active mode's curve, switching the list to it first and warning about unsaved edits to the other mode.
  - New **Apply now** button: tries the editor's curve on the active mode without saving; closing the window without saving puts the saved curve (or the firmware's) back.
- WPF `Resource.resx`: new status and result texts; the Test now confirmation names the mode.

### Tests changed on purpose
- `FanProfileTests`: `Apply_ForAnEarlierMode_…`, `Apply_ModeChangesBetweenWriteAndRecheck_…`, `Apply_FirmwarePutsItsCurveBackTwice_…`, `Apply_RevertedOnceThenKept_…` and `Apply_DoesNotOverwriteAnEarlierAnswer` took their verdict from the call's return value. That verdict now comes from the background check, so they await `PendingCheck` and assert the same outcome there (`Rejected` event, no second write of the earlier curve, no rejection on a mode change). The earlier-mode test now expects that apply to report `Applied` (its write did go through) with its check stopped.
- `Probe_ModeChangesDuringTheTest_…`: the mode now changes at the check (read 1) rather than at the first read, because the test reads the table before writing.
- `ProbeAsync` callers read `.Result`.

## Review fixes (2026-11-13)

A fresh read of the changed code found 11 issues. Found by reading; **not checked on a laptop**.

1. **Macros after a protected game (high).** `LenovoLegionToolkit.Lib.Macro/MacroController.cs`: game-safe mode resumed macros from a background timer thread, so the keyboard hook was installed on a thread with no message loop: macros stopped working and key presses could stall. The hook is now always installed and removed on the thread that started macros (the UI thread). A test reproduced it first (hook on thread 13 instead of 17).
2. **Game-safe on/off overlapping.** `LenovoLegionToolkit.WPF/Utils/GameSafeMode.cs`: the service check and the Settings toggle could switch at the same time (up to 25 switches at once in the test). Now one at a time, deciding on the newest inputs.
3. **Stuck keys.** `LenovoLegionToolkit.Lib.Macro/Utils/MacroPlayer.cs`: a macro stopped half-way (another key, or game-safe mode starting) now releases the keys and mouse buttons it pressed (`HeldInputs`). These few "key up" events are sent even when game-safe mode is what stopped the macro: a held key in the game is worse.
4. **God Mode apply.** `LenovoLegionToolkit.Lib/Controllers/GodMode/GodModeController.cs`, new `LenovoLegionToolkit.Lib/Utils/ReadOnceCache.cs`: the firmware's ranges are read once per run instead of on every Custom apply, re-read after resume from sleep (`PowerStateListener`). If they can't be read, no limits are sent but the fan settings still are. Also found while doing this: since the range check, a setting the preset doesn't save was sent with the firmware's read-back value; it now goes the "default" way again, as before (`OnlyIfSaved`).
5. **FPS counter.** `LenovoLegionToolkit.Lib/Controllers/Sensors/FpsSensorController.cs`: one process snapshot per second instead of two, and the process objects are disposed (`Decide`).
6. **Custom-mode details.** `PowerModeProfileReader.FromPreset` shows the values actually sent (range-checked, only saved settings), via `IGodModeController.GetRangeCheckedActivePresetAsync`.
7. **GPU clock range.** `GPUOverclockController`: the driver's range is picked by clock domain, not by position; a range of a single value (0 to 0 when overclocking is locked) now means "no offset" instead of falling back to LLT's ±500/±3000 MHz (`OverclockApplyGuard.ClampDeltaMhz`).
8. **Fan curve on the details card.** `FanProfileController.CurveInUse`/`CurveState`: the card says "yours" only when the last write of the saved curve was kept, "not applied yet" before the first, and "the laptop's built-in curve, yours wasn't kept" after a refusal or failure (no bars then). Tests and Apply now don't count. The card refreshes when a fan result arrives.
9. **Stale reading after pause.** `SensorsGroupController`: a read that was running when game-safe mode started is dropped instead of published; `IsPaused` is volatile.
10. **False safety warning.** `GPUOverclockController.IsSupportedAsync`: when it clears unsupported settings it also removes the "apply in progress" note.
11. **Idle detection.** `UserInactivityAutoListener`: events are raised outside the lock; an event overtaken by a newer one is dropped.

`LenovoLegionToolkit.Lib.Macro` and `LenovoLegionToolkit.WPF` now expose internals to the test project, like the other libraries.

### Performance fixes (2026-11-14)

Measured on the LOQ before the fixes: in the tray ~7–17% of one core (peaks 34%), with the dashboard open 32–48%, private memory 320–380 MB swinging. A `dotnet-trace` of both states showed the causes. **The after-measurement is still to be done on the laptop.**

1. **Hidden spinners kept animating.** `LenovoLegionToolkit.WPF/Controls/LoadableControl.cs`: the page loading ring was only hidden after loading, and WPF keeps an endless animation running even when hidden or in the tray. It now stops and leaves the layout when not loading; a caller's progress mode is kept. Same for the download ring in `Controls/Packages/PackageControl.xaml(.cs)` and the busy ring in `Windows/Dashboard/FanCurvesWindow`. Tests failed first (2 of 5).
2. **WMI objects never released.** `LenovoLegionToolkit.Lib/System/Management/WMI.cs`: every query left its searcher, results and method parameters to the garbage collector; the finalizer thread spent about a busy thread's time releasing them. New `QueryAsync`/`InvokeAsync` release each object right after reading it; results are a list, not a lazy sequence.
3. **The same sensors read three times a second.** New `LenovoLegionToolkit.Lib/Utils/SharedReading.cs`; `SensorsController.GetDataAsync`/`GetFanSpeedsAsync` and `Battery.GetBatteryInformation`/battery temperature share one real read among callers within 750 ms (below the 1 s minimum refresh interval).
4. **Battery driver slot.** `Battery`: the slot that answered is tried first instead of searching up to three slots on every read (`BatterySlotsToTry`).
5. **HWiNFO integration.** `LenovoLegionToolkit.Lib/Integrations/HWiNFOIntegration.cs`: updates every 2 s (HWiNFO's own default poll) instead of 1 s, through the shared reads.
6. **Animations capped at 30 fps.** `Motion.CapAnimationFrameRate()` at the very start of `App.Application_Startup` sets the default of `Timeline.DesiredFrameRate` to 30 for every animation (LLT's and WPF-UI's). A redraw caused directly by input still happens at once.

Still open: the rendering thread with the dashboard open in the Retro style (23–35% of a core before); to be measured after these fixes, Retro vs Fluent, before deciding on any change.

### Tests changed on purpose
- `OverclockApplyGuardTests.ClampDelta_WithoutADriverRange_UsesLltsOwnLimits`: its `(0, 0)` case asserted the fallback that fix 7 removes; it moved to `ClampDelta_DriverAllowsOnlyZero_SendsZero`.
- `PowerModeDetailsTests.CustomMode_ReadsThePreset_AndItsFanCurve`: passes the range-checked preset too (new `FromPreset` signature); same assertions.
- `GameSafeModeTests.Switching_FromSeveralPlacesAtOnce_…`: written for fix 2; its first version read the final inputs in every task and never switched, so it was corrected before use. Shown failing without the lock (7–25 overlapping switches) and passing with it.
