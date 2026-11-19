# Changes from Lenovo Legion Toolkit v2.36.0.0

This file is the modification notice required by GPL-3.0 section 5(a).

- **Original work:** Lenovo Legion Toolkit v2.36.0.0, © Bartosz Cichecki, Kaguya, Dr. Skinner and contributors, licensed under GPL-3.0. Source: https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/releases/tag/v2.36.0.0
- **Modified by:** Nirmay Kadam
- **Date of modifications:** 2026-11-07 (bug and security fixes); 2026-11-08 (UI styles, animations, status strip); 2026-11-13 to 2026-11-19 (features, safety, performance and stability rounds; renamed to Indra)
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

**Measured on the LOQ after the performance fixes (2026-11-14):** in the tray 2.1% of one core (middle value; was 10.8%), private memory steady at 207–221 MB (was 316–357 MB swinging), UI thread idle. Dashboard open: Retro about 13% of one core (was about 36%); Fluent close to 0% (not confirmed that the Dashboard page was shown).

## Round 1: stability (2026-11-14)

1. **LLT crashed when a game closed** (4 times in the Windows log, on 2.34 and 2.36.1). `LenovoLegionToolkit.Lib/AutoListeners/GameAutoListener.cs` kept running games in sets hashed on `Process.Id`; a `Process` object that another exit event had already disposed throws when asked for its id, and that happened on a thread-pool thread, which ends the app. New `LenovoLegionToolkit.Lib/AutoListeners/ProcessSet.cs` keys each process by the id read once when it is added and finds disposed objects by identity; `Process_Exited` also catches anything left and writes an error report instead of crashing. A control test reproduces the logged exception with the old kind of set.
2. **Boot logo interrupted halfway.** `LenovoLegionToolkit.Lib/System/BootLogo.cs`: turning a logo on now clears the UEFI "enabled" flag before touching files (when a logo was on) and sets it only after the new image and checksum are written; turning it off clears the flag first, then the checksum, then the files (`EnableSteps`/`DisableSteps`). An interruption at any point leaves the stock logo. Same UEFI writes as before, plus one extra flag write when replacing an enabled logo.
3. **Crash reports with logging off.** `LenovoLegionToolkit.Lib/Utils/Log.cs`, `LenovoLegionToolkit.WPF/App.xaml.cs`: unhandled errors (background, UI, tasks; a closing error always) write `error_*.txt` to `%LOCALAPPDATA%\LenovoLegionToolkit\log` even with trace logging off. The same error is written once a minute at most; the newest 20 reports are kept. Nothing is sent anywhere. Tests showed the missing limit and repeat check failing first.

Tests: `Tests/AutoListeners/ProcessSetTests.cs`, `Tests/System/BootLogoStepTests.cs`, `Tests/Utils/ErrorReportTests.cs`. Not verified on the laptop yet.

## Round 2: performance (2026-11-17)

1. **Retro phosphor glow on static text only.** `LenovoLegionToolkit.WPF/Windows/MainWindow.xaml.cs`: the glow was one `DropShadowEffect` on the whole window content, re-rendered whenever anything under it changed (sensor values, the CRT flicker and roll band). Removed. `LenovoLegionToolkit.WPF/Utils/Skins/SkinPalette.cs`: Retro with the glow on and GPU effects defines `LltTextGlowEffect`, one frozen effect (phosphor color, blur 8, 0.35) shared by every title; other skins don't define it, so titles draw plainly. Used by page titles (`Pages/*.xaml`), dashboard group titles (`Controls/Dashboard/DashboardGroupControl.cs`), card titles (`Controls/CardHeaderControl.cs`) and the power-mode console line (`Controls/Dashboard/PowerModeSelectorControl.xaml`); never on live readings. `SkinOptions` carries the `RetroGlow` setting (`Utils/ThemeManager.cs`).
2. **Anti-cheat check only while game-safe mode is on.** `LenovoLegionToolkit.WPF/Utils/GameSafeMode.cs`: the 5 s service check starts with the setting and stops when it's turned off (it ran at every start before). A failure while pausing or resuming is logged and reported instead of escaping a background task; the switch back still runs. `LenovoLegionToolkit.Lib/Utils/AntiCheatMonitor.cs`: `IsRunning`; `Stop` forgets the last result; the service queries no longer run inside the lock, and an older overlapping check can't overwrite a newer result.
3. **GPU check loop no longer dies silently.** `LenovoLegionToolkit.Lib/Controllers/GPUController.cs`: one failed round ended the loop for good while `IsStarted` stayed true (and the dGPU card stopped updating). Now a failed round is logged (an error report once per run) and the loop carries on; a failed start-up (no NVIDIA driver) ends the loop and clears `IsStarted`, so a later start can try again.
4. **0 compiler warnings** (was 14). `CS1998` in `SensorsControllerV0`, `PrecisionTouchpadLockFeature`, `ITSModeFeature.GetITSModeEx`, `ITSModeListener`, `App.InitAutomationLocalization`, `LampArrayRGBKeyboardPage.IsSupportedAsync`, `GodModeSettingsWindow.FanFullSpeedToggle_Click`, `RGBKeyboardBacklightControl.SynchroniseZonesMenuItem_Click`: no `async` without an `await`; errors still arrive through the returned task where they did before. `ITSModeListener`'s Fn+Q handler now catches a failing subscriber. `CS8602` in `SkinPaletteTests`: an explicit `Assert.NotNull`.

Tests: `Tests/Controllers/GPURefreshLoopTests.cs` (new), `Tests/UI/GameSafeModeTests.cs` and `Tests/UI/SkinPaletteTests.cs` (new cases). The 7 new game-safe and GPU tests were run against the old behaviour first and failed for the expected reasons. 583 tests, all passing. Not verified on the laptop yet.

### Tests changed on purpose
- `GameSafeModeTests.Switching_FromSeveralPlacesAtOnce_…`: passes the new error-report argument and disposes the monitor (whose check now starts with the setting); same assertions.
- `SkinPaletteTests.Neon_GlowFollowsTheOptions`: `SkinOptions` has a fourth argument (`RetroGlow`); same assertions.
- `SkinPaletteTests.Retro_EveryPhosphor_IsReadable`: `Assert.NotNull` on the colors before use (was a null-forgiving `!`); stricter, not weaker.

### Round 2 follow-up: the GPU check runs only while something needs it (2026-11-17)

The discrete-GPU check (every 5 s: NVIDIA's API and the list of processes on the dGPU) kept running in the tray once anything had started it.
- Every sensor read started it, and nothing stopped it.
- The dashboard card stopped it only when "use dGPU for game detection" was off.
- Game detection never stopped it.

`LenovoLegionToolkit.Lib/Controllers/GPUController.cs` now tracks who needs it:
- **Holders:** `Hold`/`Release`, used by the dGPU card while visible, the dGPU window while open (it relied on others before) and game detection while it runs.
- **Readers:** `GetFreshStateAsync`, used by the two sensor paths. Each read keeps the check running for three rounds (at least 15 s). If the check wasn't running, the read refreshes first, so the sensors never act on an old state and query a sleeping dGPU.
- **Stopping:** the loop stops when the last holder lets go or the last reader's time runs out. It starts again when needed, and a hold arriving during the stop decision is never lost.
- **Interval setting:** the "GPU monitoring interval" setting is read every round. Before, starting the check overwrote it with 5 s.
- **Removed:** `StartAsync`/`StopAsync`/`GetLastKnownStateAsync`.

Callers:
- `WPF/Controls/Dashboard/DiscreteGPUControl.xaml.cs`;
- `WPF/Windows/Dashboard/DiscreteGPUManagementWindow.xaml.cs`;
- `Lib/AutoListeners/GameAutoListener.cs`;
- `Lib/Controllers/Sensors/AbstractSensorsController.cs`;
- `Lib/Controllers/Sensors/SensorsGroupController.cs`: only for GPU updates of an NVIDIA dGPU.

**Behaviour change:** a locked GPU P-state is re-applied when the dGPU wakes only while the check runs. `DgpuAwakeManager` still applies it when it keeps the dGPU awake, and no P-state is locked on the test laptop.

Tests: `Tests/Controllers/GPURefreshLoopTests.cs`, 6 new.
- All 6 were run against a stand-in with the old behaviour (release does nothing; a read starts the check and returns the last state), and all 6 failed.
- The read-refreshes-first test needed a start-up delay above 0 to fail: with 0, the old loop's first round happened to run synchronously.
- 589 tests, all passing. Not verified on the laptop yet.

#### Tests changed on purpose
- `GPURefreshLoopTests`: the three Round 2 loop tests call `Hold`/`Release` instead of `StartAsync`/`StopAsync`; same assertions.

## Round 3: hardware features (2026-11-17)

### 1. Use the firmware's fan curve
- `LenovoLegionToolkit.Lib/Controllers/FanProfileController.cs`: `ResetToFirmwareAsync(mode or all)` removes your curve(s). If one of them was in use (feature on, firmware keeping curves, laptop in that mode), it stops the pending re-check, so the removed curve is never written again, and applies the same power mode again so the firmware loads its own curve. It writes no fan table itself, and says whether the mode was re-applied.
- `LenovoLegionToolkit.WPF/Windows/Dashboard/FanCurvesWindow.xaml(.cs)`: a "Firmware fan curve" card with **Use firmware curve** (selected mode) and **Reset all modes**. Both ask first and lock the window while running. Custom mode is pointed to God Mode → Load defaults, which already existed.
- `LenovoLegionToolkit.WPF/Controls/Dashboard/PowerModeDetailsControl.xaml(.cs)`: **Use firmware curve** next to "Fan curves…" while a curve of yours is set for the shown mode.
- Tests (`Tests/UI/FanProfileTests.cs`, 8 new):
  - only that mode is removed;
  - the mode is re-applied once, and only when the curve was in use;
  - "all" re-applies once;
  - a pending re-check never writes the removed curve back;
  - a failed re-apply is reported;
  - nothing to reset changes nothing.
- Not verified on the laptop yet.

### 2. Power mode profiles
- `LenovoLegionToolkit.WPF/Windows/Dashboard/PowerModeProfilesWindow.xaml(.cs)` (new): one tab per power mode.
  - **Fan curve:** whose curve the mode uses, with **Edit…** (opens the Fan curves window on that mode) and **Use firmware curve**.
  - **Windows:** the plan cards (plan mapping) or the power-mode cards (power-mode mapping), or a note when the mapping is off.
  - **Apply now:** for the mode the laptop is in, it applies its fan curve and Windows setting again, the way a mode change does.
  - **Custom tab:** points to God Mode settings.
  - It edits the same settings through the same paths; there are no new firmware writes.
- `LenovoLegionToolkit.WPF/Controls/Settings/WindowsPowerCards.cs` (new): the plan, Balanced-overlay and power-mode cards, plus saving a built-in mode's choices (applied at once when the laptop is in that mode). `WindowsPowerPlansWindow` and `WindowsPowerModesWindow` now build their per-mode and ITS tabs from it instead of four copies of the same code. Choice handlers no longer let an error escape into WPF.
- **Opens from:**
  - the power-mode details card (**Profiles…**);
  - Settings → Power;
  - the two Windows power windows.
- `FanCurvesWindow` can open on a given mode.
- Tests: `Tests/UI/WindowsPowerCardsTests.cs` (which Windows section each mapping shows). The refactored windows have no automated tests; they are on the laptop checklist.
- Not verified on the laptop yet.

### 3. Game profiles
- `LenovoLegionToolkit.Lib/GameProfiles/GameProfileController.cs` (new): while a chosen game runs, it can:
  - force game-safe mode;
  - ease listed apps (efficiency mode and below-normal priority);
  - ask listed apps to close;
  - stop listed services.

  When the game ends, everything it changed is put back in reverse order. It listens to process start/stop events only while an enabled profile exists.
- **Recognising the game:** from the names Windows reports, including the process trace's 15-character names, and from the process list. The game's process is never opened.
- **Session note:** every change is noted in `game_profile_session.json` before it is made. It is put back when the game ends, when LLT exits (at most 30 s, otherwise at the next start), or at the next start after LLT was ended mid-game.
- **Errors:** one failed step doesn't stop the others, and failures are counted in the report.
- `LenovoLegionToolkit.Lib/GameProfiles/ServiceRules.cs` (new): the fixed deny list, plus no drivers, no services that can't stop, and no services other running services depend on (stops use `stopDependentServices: false`).
- `LenovoLegionToolkit.Lib/GameProfiles/WindowsGameProfileSystem.cs`, `GameProfileCatalog.cs`, `IGameProfileSystem.cs` (new): the Windows calls. Running apps are listed from the process and window lists. Efficiency mode is read and set through `GetProcessInformation`/`SetProcessInformation` (`NativeMethods.txt`: `GetProcessInformation`).
- `LenovoLegionToolkit.Lib/Settings/GameProfileSettings.cs` (new): `game_profiles.json` and the session note.
- `LenovoLegionToolkit.WPF/Utils/GameSafeMode.cs`: a profile that asks for game-safe mode wins, even with the setting off; the anti-cheat check still follows the setting only.
- `LenovoLegionToolkit.WPF/Windows/Settings/GameProfilesWindow.xaml(.cs)` (new), opened from Settings → App behaviour:
  - games on the left;
  - on the right, per game: on/off, game-safe, and the three lists. Apps are picked from running apps or by browsing; services from running ones, with the protected or refused ones greyed out with the reason.
  - It asks before a profile first changes other software, saying that closed apps aren't reopened.
- **Notifications** `GameProfileOn`/`GameProfileOff`: what was changed or put back, with a setting in the notification options. Settings → App behaviour says when game-safe mode is on because of a profile.
- `App.xaml.cs`: the profile is started after start-up and put back on exit.
- Tests: `Tests/GameProfiles/GameProfileControllerTests.cs` (new, scripted Windows), plus forced game-safe cases in `GameSafeModeTests`. They cover:
  - apply and put back exactly;
  - the game's process is never touched, even when listed;
  - off-limits apps;
  - an app already in efficiency mode;
  - unknown efficiency setting;
  - a reused process id;
  - protected and unstoppable services;
  - a service running again;
  - a failed stop;
  - two game instances;
  - recovery after LLT was ended;
  - turning a running profile off;
  - name matching.

  These are new features, so no old bug was shown failing first.
- Not verified on the laptop yet.

### Fix after the first laptop check (2026-11-18)
- **Game profiles → Open… crashed** with "Requested value 'Leaf24' was not found": WPF-UI 2.1.0 has no `Leaf24` icon. `GameProfilesWindow.xaml` now uses `LeafOne24`.
- XAML icon names are only looked up when a window opens, so the build and tests didn't catch it. The new `Tests/UI/XamlIconTests.cs` checks every literal `Icon`/`Symbol` name in the XAML against WPF-UI's list; it failed on exactly this name before the fix.
- **The app picker offered Windows' own apps.** Your profile showed `explorer` and `StartMenuExperienceHost`; LLT would have refused them at game start, but they shouldn't be offered.
  - New `LenovoLegionToolkit.Lib/GameProfiles/AppRules.cs`: Windows' shell and system process names, plus LLT itself, are never eased or closed, whatever their path; nothing in the Windows folder is either.
  - `GameProfileCatalog.RunningApps` drops those names, profiled games and apps already listed **before** opening anything, then drops Windows-folder apps.
  - "Browse…" refuses them with a message.
  - Entries already saved show in the caution colour with "LLT won't touch this".
- **An app listed both to ease and to close** is now only closed, not eased first.
- Tests:
  - two new `GameProfileControllerTests` (shell apps by name, an app in both lists), both failing before the fix;
  - `Tests/GameProfiles/AppRulesTests.cs` (names, Windows-folder paths, and that a game's name is never looked up).
- **ITS mode not on the dashboard:** expected.

## Round 4: games and OSD (2026-11-18)

### 1. LLT steps aside while MSI Afterburner runs
- `LenovoLegionToolkit.Lib/Utils/AfterburnerDetector.cs` (new): whether `MSIAfterburner` is in the process list. No process is opened. If the check itself fails, it counts as running, which is the safer answer.
- `GPUOverclockController.ApplyStateAsync`: returns the new `SkippedForAfterburner` before anything reaches the GPU driver.
  - **The bug it fixes:** switching LLT's overclock **off** (dashboard toggle, automation step) used to write zero offsets, which would undo Afterburner's undervolt or curve. That also stops now.
  - The saved values and the crash guard are untouched.
- `GPUController.ApplyPStateAsync`: LLT's P-state lock (and its reset) is skipped the same way. The choice stays saved.
- **Where it's shown:** a note on the GPU overclock card, its settings window (plus a "Saved. Not applied while MSI Afterburner is running." result) and the dGPU window's P-state card.
- Tests: `Tests/Controllers/GPUOverclockAfterburnerTests.cs`.
  - The overclock tests failed before the fix by going on towards the driver; the P-state tests were written with the fix, since running them on the old code would have changed the real GPU's P-state.
- Not verified on the laptop yet. The ITS card is only for laptops with Lenovo's Intelligent Thermal Solution (IdeaPad, ThinkBook, Yoga, Slim and similar). The LOQ 15IRH8 uses power modes and has no ITS service. The Customize Dashboard list shows every card for every laptop.

### 2. OSD frame-time graph
- A new OSD item, **Frame-time graph** (Game group, off by default, with hover help). It shows each frame of the last 5 seconds, oldest on the left; frames over twice the median (stutters) are drawn in the OSD's critical colour, with a faint line at the median. The scale is 33.3 ms (30 fps) up to 1.2 × the longest frame, capped at 100 ms.
- **Where the frames come from:** the FPS measurement already in use (PresentMon over ETW, no driver), whose callback fires once per frame after a warm-up of about 500 frames. Nothing is drawn before real frames arrive, and nothing is invented.
- `LenovoLegionToolkit.Lib/Controllers/Sensors/FrameTimeHistory.cs` (new): the last 5 s of frame times (at most 3000), thread-safe, ignoring values ≤ 0 or > 1000 ms (pauses). `FpsSensorController` adds each frame and clears it when the measured game changes or measuring stops.
- `LenovoLegionToolkit.WPF/Windows/Osd/FrameTimeGraphLayout.cs` (new, pure): where each frame is drawn, the scale, the median and the stutter flags.
- `LenovoLegionToolkit.WPF/Windows/Osd/FrameTimeGraph.cs` (new): draws the layout with one frozen geometry. It reads the history at most 30 times a second, **only while it is visible** (OSD shown and the item on); errors are logged and the OSD keeps running.
- Panel style: a 40 px high row at the bottom of the FPS group. Bar style: an 80 × 16 sparkline after the frame time. Turning the graph on also starts FPS measuring, like the other Game items.
- `OsdItem.FrameTimeGraph` is appended at the end of the enum, so saved item lists keep their meaning.
- Tests: `Tests/UI/FrameTimeGraphTests.cs` (14; written for the new code, so there was no old behaviour to fail).
- Not verified on the laptop yet.

## Round 5: usability (2026-11-18)

New strings are in English only (`Resource.resx`). Other languages show them in English, the standard fallback, like the rest of this fork's strings.

### 1. Undo within 30 seconds (God Mode and fan curves)
- `LenovoLegionToolkit.Lib/Utils/UndoSlot.cs` (new): the one change that can be undone right now. A newer change replaces it; it expires after 30 s; an undo runs once; a failed or partial undo is reported, never thrown. It is in memory only.
- `AbstractSettings.CloneStore()` / `RestoreStore()`: an exact deep copy of a settings store, through the same JSON it is saved with, and putting it back.
- `LenovoLegionToolkit.Lib/Controllers/GodMode/GodModeUndo.cs` (new): taken before a Save. Undo puts the earlier presets back and, only while the laptop is still in Custom mode, sends them again, or goes back to the earlier mode if the Save had switched to Custom. `GodModeSettings` got a test folder constructor.
- `FanProfileController.Snapshot()` / `RestoreAsync()`: the on/off switch and every curve (copied). Undo touches only the current mode, and only if its curve changed. It writes the earlier curve, or re-applies the mode to bring the firmware's curve back.
- `LenovoLegionToolkit.WPF/Controls/UndoBarControl.xaml(.cs)` (new): "… · Undo (23 s)", then the result. It is in the main window, the God Mode, Fan curves and Power mode profiles windows, so the window in front always has one (God Mode and Fan curves are modal). The timer runs only while there is something to show.
- **Offered after:** God Mode Save and Save & close; fan-curve Save; Use firmware curve (Fan curves window, Power mode profiles, dashboard details card); Reset all modes. Test now and Apply now already put the earlier table back, so they get none. The Fn+Q-loop choice saved by the God Mode window isn't part of the undo.
- On the dashboard card, the reset result now shows on the undo bar instead of a snackbar, so the two don't cover each other.
- Tests: `Tests/UI/UndoSlotTests.cs`, `Tests/Controllers/GodModeUndoTests.cs` (over a real `godmode.json`), and the undo section of `Tests/UI/FanProfileTests.cs`. These are new features, so there was no old behaviour to fail.
- Not verified on the laptop yet.

### 2. First-run tour
- `LenovoLegionToolkit.WPF/Windows/Utils/TourWindow.xaml(.cs)` (new): 7 steps covering power modes and Fn+Q, the dashboard, Custom mode, fan curves, the OSD, game-safe mode and game profiles, and Settings with the safety report. It has Back / Next / Skip, and each step has **Show me**, which opens the page. It doesn't block the main window, and only one can be open.
- `LenovoLegionToolkit.WPF/Utils/TourSteps.cs` (new): the steps, and the rule for when the tour opens by itself.
- `MainWindow.NavigateTo(pageTag, settingsTab)`: opens a page (and a Settings category). It returns false when this laptop doesn't have the page, and **Show me** is then hidden.
- **When it opens:** once, on a normal start (not a tray or autostart start), right after the plugin notice. This includes after this update. `ApplicationSettings.TourShown` is saved when the tour opens, so a crash can't make it come back at every start.
- **Settings → App behaviour → Quick tour** opens it again (with hover help).
- Tests: `Tests/UI/TourTests.cs`. They check that every step has its text, that every step's page and Settings category exist in the real `MainWindow.xaml` and `SettingsPage` sources, and the show-once rule.

### 3. Safety report
- `LenovoLegionToolkit.WPF/Utils/SafetyReport.cs` (new, pure): rows in three sections, in the order of `docs/SAFETY.md`.
  - **Protections:** game-safe mode and which anti-cheat, the GPU crash guard.
  - **In effect now:** GPU overclock (or "MSI Afterburner is running"), the Custom mode preset and its saved values, per-mode fan curves and whether each is in use, the active game profile and what it eased or paused.
  - **App security:** settings folder protection, plugins and old plugin files, autostart, CLI, the hardware-sensor driver and PawnIO.
  - Rows that need a look are shown in the caution colour: the overclock turned off after a crash, a settings folder others can write to, old plugin files, a curve the firmware didn't keep.
  - `ToText()` gives the plain text for Copy.
  - It lives in WPF rather than Lib, because its text uses the app's strings.
- `LenovoLegionToolkit.WPF/Utils/SafetyReportCollector.cs` (new): reads only settings, in-memory state, the process list, the registry, Task Scheduler and the settings folder's permissions.
  - It calls no hardware driver and changes nothing.
  - It deliberately avoids three calls that have side effects: `GPUOverclockController.GetState()` (can write settings), `GodModeController.GetActivePresetAsync()` (can save) and the sensors' `IsSupportedAsync()` (starts the driver).
  - Each part is read on its own, and one that fails shows "Couldn't read this".
- `LenovoLegionToolkit.WPF/Windows/Settings/SafetyReportWindow.xaml(.cs)` (new): it is read off the UI thread, and has Refresh and Copy as text. It opens from Settings → App behaviour (with hover help) and from the About page.
- Tests: `Tests/UI/SafetyReportTests.cs` checks what each state says, what is flagged, "couldn't read" instead of a guess, and the text format.

### 4. Fix: settings backup missed three files
- `SettingsBackupWindow` didn't list `fan_profiles.json` (per-mode fan curves), `game_profiles.json` or `amd_oc.json` (AMD overclocking), so a backup lost them. They are now included.
- `game_profile_session.json` is left out on purpose: restoring an old session note could put back services or app priorities from another time.
- Test: `Tests/Settings/SettingsBackupCoverageTests.cs` checks every settings file named in the settings classes' sources. It **failed before the fix**, naming exactly those three files.

## Round 6: CI (2026-11-18)
- `.github/workflows/ci.yml` (new): on every push and pull request to `main`, and on demand.
  - It runs on `windows-latest` with .NET 9.
  - It restores each project (not the `.sln`, because of its `.wapproj`), builds the app and `llt` with warnings as errors, runs every test, and uploads the results.
  - It has read-only permissions and no secrets or signing. The actions are pinned to commit SHAs like the upstream workflows.
  - The upstream `build.yml` / release workflows run on `master` and are untouched.
- `Tests/Performance/StartupBudgetTests.cs` (new): a budget for building the app's container from its four real modules, with start-up components skipped. A marker component proves nothing starts. The budgets are 10 ms and 2 MB, against 1.0 ms and 0.62 MB measured locally. It is not a measure of real app start-up; the test says so.
- `MacroSettings` got a test-folder constructor. The two game-safe macro tests now use a temporary folder instead of the real settings folder. Before, on an elevated CI machine they could create `UserData\<SID>` next to the test files, and locally they read your real `macro.json`. The assertions are unchanged.
- Not run on GitHub yet when committed; the first run's result is reported separately.

### Fix found by the first CI run
- **The repository couldn't be built from a fresh clone.** `.gitignore`'s NuGet rule `**/[Pp]ackages/*` also matched the app's own `LenovoLegionToolkit.WPF/Controls/Packages/PackageControl.xaml(.cs)` (the Packages page's list item). Those two files were never committed, so every clone failed with "the namespace 'Packages' does not exist".
- An exception for that folder was added to `.gitignore`, and the two files are now committed. No other source file was being ignored (checked across the whole tree, outside build output).
- With the fix, CI is green: the build and all 708 tests pass on GitHub's Windows runner.

## Round 7: Custom mode (God Mode) test seam (2026-11-18)
- `LenovoLegionToolkit.Lib/Controllers/GodMode/IGodModeFirmware.cs` (new, internal): everything `GodModeController` reads from or sends to the laptop on the data-driven path (current Legion/LOQ and non-gaming models), the fan steps and AMD overclocking.
  - That covers machine info, the Vantage/Legion Zone/Legion Space checks, capability and fan-table data, feature values, the NVIDIA power-target offset, fan full speed and fan table, BIOS OC mode and CPU OC data, the Windows power-settings step, and the delays.
  - `WmiGodModeFirmware` forwards each call unchanged to the same WMI / NVAPI / Windows calls as before.
- `GodModeController`:
  - The public constructor (used by the app) builds `WmiGodModeFirmware`, and an internal constructor takes the interface.
  - The order of the writes, the error handling and the exception filters are unchanged.
  - The legacy path for older models isn't routed through it; it is untouched apart from the Legion Zone check.
- **Wording fixed, behaviour kept (your choice):** when the firmware's ranges can't be read, the saved values aren't sent, and the firmware's own Performance (or Extreme) default for each setting is sent instead. The code comment and log said "no limits will be sent", which was wrong. They and `docs/SAFETY.md` now say what happens.
- Tests: `Tests/Controllers/GodModeApplyTests.cs` (19), against a scripted laptop and real `godmode.json` files in a temporary folder.
  - **Blocking:** Vantage, Legion Zone or Legion Space (Smart Fan ≥ 8) running blocks the apply.
  - **Values:**
    - in-range values are sent as-is, masked, in order;
    - out-of-range values get the firmware default;
    - offsets only narrow the range;
    - an unsaved value gets the Performance default;
    - the fallback when ranges can't be read.
  - **Non-gaming models:** an unsaved value isn't sent, cTGP is sent as an offset, and no fan table is written.
  - **Failures:** a CPU limit failure stops the later limits but the fan step still runs, then the error is raised. A GPU failure is skipped.
  - **Fan:** a curve the firmware doesn't keep is tried 3 times. A kept curve is written once.
  - **AMD:** the overclock values are clamped, and nothing is sent without BIOS OC.
  - **Ranges cache:** read once until forgotten.
- **Honesty check:** this refactor had no old bug to fail first. Instead, three lines were broken one at a time, and each time exactly the matching test failed:
  - removing the Vantage check failed the Vantage test;
  - removing the AMD clamp failed the clamp test;
  - removing the cTGP offset failed the non-gaming test.

  The lines were then put back.
- Not verified on the laptop yet. Custom mode should behave exactly as before.

## Round 8: safety report extras, backup laptop model (2026-11-18)
- **Power mode in the safety report:**
  - It is the first row of "In effect now", read with one read-only Lenovo WMI query, the same one the dashboard makes; game-safe mode allows it.
  - Laptops without Lenovo power modes show "Not available"; a failed read shows "Couldn't read this".
  - The Custom mode preset says "in use now" while Custom mode is on.
  - The report's message and hover help now describe that query.
- **Settings files in the safety report:**
  - A new section lists every file in the settings folder with its size and when it changed. Only names, sizes and dates are read, never contents.
  - **Flagged:**
    - `*_backup_*.json`: LLT found that settings file unreadable and kept a copy;
    - a `.tmp` file more than a day old: left over from an interrupted save;
    - `fake_mi.json`: fake machine information in use.
  - `SafetyReportCollector.CollectAsync` replaces `Collect`.
- **Backup laptop model:**
  - `LenovoLegionToolkit.Lib/Utils/BackupManifest.cs` (new): `backup_info.json` holds the LLT version, machine type, model name and time made. There is no serial number.
  - **Saving:** the Settings backup writes it next to the copied files. If it can't be written, that is logged and the backup still succeeds.
  - **Restoring:** a backup from another model warns first, naming both models, with Continue or Cancel. Only then does the existing "what's inside" check run.
    - The machine type decides; the model name is used only when a type is missing.
    - An old backup without the note restores as before.
    - The note is never copied into the settings folder.
- Tests:
  - `Tests/Settings/BackupManifestTests.cs` (new): write/read, no serial number, missing or unreadable note, the model comparison, never restored.
  - `Tests/UI/SafetyReportTests.cs` additions: power mode row, "in use now", not available or unreadable, file list and sizes, flags, a fresh `.tmp` not flagged, an unreadable file list.
- **Test changed on purpose:** `SafetyReportTests.EverySection_IsPresent_InTheOrderOfSafetyMd` now expects four sections, because the "Settings files" section is new.
- Not verified on the laptop yet.

## Round 9: Game mode, better game picking, background services (2026-11-19)
The earlier Round 9 (translations) was dropped; English stays the only language.

### 1. Game mode (any game)
- `GameProfileSettings.Store.GameMode` (new): a profile with the same choices as a per-game one, applied to any game without its own profile. It is off by default, and older settings files load with it off.
- **What counts as a game:**
  - a game Windows has recognised (`HKCU\System\GameConfigStore`, re-read when it changes);
  - the program in front when Windows Game Mode turns on;
  - a game with its own profile.
- **What doesn't count:** "uses the NVIDIA GPU" isn't a signal here, so a browser or Discord isn't taken for a game. Windows' own apps never count.
- `Lib/GameProfiles/WindowsGameSignals.cs` (new): the known-games list and Windows Game Mode. It runs only while Game mode is on, and the foreground program is found in the process list without being opened.
- **`GameProfileController`:**
  - A game with its own profile uses that profile only.
  - Every running game's processes are off-limits, by process id and by name. This covers a second process of the game that LLT didn't see start.
  - A program eased before it was recognised as a game is put back at once.
  - Put-back, the session note and crash recovery are unchanged.
- **UI:** "Game mode" is the first entry in the Game profiles list, with its own explanation. It can't be removed.

### 2. Better game picking
- "Add game" is now a menu:
  - **Games Windows knows:** `GameProfileCatalog.KnownGames`, which leaves out LLT, Windows' own apps and games already profiled;
  - **Running now;**
  - **Browse….**
- The first two give the game's own program, not its launcher (for Valorant, `VALORANT-Win64-Shipping`).

### 3. Fix: long game names missed by the automation game trigger
- `GameAutoListener` compared a started process's name exactly with the known games and your included programs. Windows cuts names to 15 characters with ".exe", so `VALORANT-Win64-Shipping` arrived as `VALORANT-Win64-` and was missed.
- It now uses the same rule as game profiles; the full path is still checked afterwards.
- Test `Tests/Automation/GameStartNameTests.cs` **failed first** on the cut names.

### 4. Service presets
- `Lib/GameProfiles/ServicePresets.cs` (new) has five presets:

| Preset | Services | On by default |
|---|---|---|
| Telemetry and error reports | DiagTrack, dmwappushservice, WerSvc | yes |
| SysMain | SysMain | yes |
| Windows Search indexing | WSearch | no, with a warning |
| Unused extras | MapsBroker, Fax, RetailDemo, RemoteRegistry | yes |
| Xbox services | XblAuthManager, XblGameSave, XboxNetApiSvc, XboxGipSvc | no, with a warning |

- Each preset says what it does and the honest effect: usually little or no FPS change.
- None contains a protected service (tested), and every service still goes through `ServiceRules.Check`.
- Game profiles and Game mode get "Add preset" in the Pause card. It adds the preset's services that are installed and may be paused.

### 5. Background services (normal use)
- **`Lib/GameProfiles/BackgroundServicesController.cs` (new):** while LLT runs, it keeps the services of the chosen presets stopped.
  - Settings are in `background_services.json`, which is backed up.
  - The note is `background_services_session.json`, written before each stop. It is not backed up, on purpose.
  - It only stops and starts services; Windows' service settings are never changed.
- **When services start again:**
  - when LLT exits;
  - when a preset is unticked or the feature is turned off ("Start them all again");
  - at the next start after LLT was ended.

  A service that won't start stays noted and is tried again later.
- **Never touched:** a service something else stopped, or a game profile paused.
- **After a game ends,** the chosen services are stopped again, in case the game session started one.
- **UI:**
  - Settings → App behaviour → **Background services**, with hover help, opens a window with the presets and their notes, each service's state ("running again" when Windows restarted one), Apply and "Start them all again".
  - The safety report gets a "Background services" row.

### Tests
**New:**
- `GameStartNameTests` (fail-first);
- `ServicePresetsTests`;
- `BackgroundServicesControllerTests`;
- `KnownGamesTests`;
- 12 Game mode tests in `GameProfileControllerTests`;
- the safety report row.

**Honesty check:** five Game mode guards were broken one at a time:
- Windows' own apps excluded;
- only known games count;
- the game's other processes protected by name;
- the put-back of a game eased too early;
- the 15-character rule.

Each failed exactly its test. Breaking the third one first showed a test that couldn't tell. Fixing that test exposed the real gap of a game eased before it was recognised, which is now put back.

**Test changed on purpose:** `SettingsBackupCoverageTests` lists `background_services_session.json` as left out of backups, with the reason.

Not verified on the laptop yet.

## Round 10: safe automations, a fork-only updater, code-review fixes (2026-11-19)

### 1. Automations: only steps that can work
- `Lib.Automation/Pipeline/AutomationRules.cs` (new, pure).
  - **Inputs:** the trigger and the steps (quick actions expanded) as `StepInfo` descriptors.
  - **It marks:**
    - AI Engine On/Toggle where the power mode is known and isn't Balance;
    - steps after Close app;
    - a setting set again later (not across a delay or program, not when the later step toggles or cycles);
    - a power mode equal to or different from the trigger's;
    - presets outside Custom mode;
    - unsupported steps.
  - `WhyNotAddable` tells Add step what to grey out and why. `FindLoops` finds automations that can start each other.
- `AutomationPipeline.cs`:
  - quick actions are expanded with a set of those being expanded. Two quick actions that ran each other used to recurse until the stack overflowed, which ended LLT;
  - a step this laptop doesn't support is skipped when run;
  - `GetValidationWarnings` lists every issue by step number, plus Run step warnings and loops;
  - `WhyStepCannotBeAdded` (new).
- **Fixed:** the hybrid mode, GPU overclock, HDR and resolution order warnings only appeared when the automation also had a Power mode step. The test failed first.
- **UI:**
  - the automation page checks each automation against all the others, and again after any change;
  - Add step disables a step that can't work and shows the reason;
  - Run steps show their program warning.
- **Fixed:** the Run step's "Check instance" box wasn't reloaded, and ticking it didn't mark the automation as changed.

### 2. Loops
`Lib.Automation/Utils/AutomationLoopGuard.cs` (new):
- A power mode or Custom preset event that arrives while an automation that can change those is running, or within 3 s after it, counts as caused by an automation.
- An automation started that way more than 3 times within 60 s is paused, with a notification, until the automations are saved again or LLT restarts. The plan said 10 s; 60 s also catches slower loops, since each mode change takes a moment.
- `AutomationProcessor` decides "caused by an automation" when the event arrives.

### 3. Run steps
- `Lib.Automation/Utils/RunTargetCheck.cs` (new) reads permissions only. It flags:
  - a program whose file or folder can be changed by programs that aren't elevated (Users, Authenticated Users, Everyone, INTERACTIVE or the user; also the owner);
  - a bare name;
  - a missing program.
- It warns only: the step still runs, and a log line is written.
- The safety report gets an "Automation Run steps" row.

### 4. Updater: this fork only, nothing downloaded
- `UpdateChecker` checks `NirmayKadam/LenovoLegionToolkitMod` releases. Stable and Beta come from `main` (or `master`). It only opens a release page of this fork (anything else falls back to the latest release page).
- **Removed:**
  - the "Server" update method and its scrambled `http://` address;
  - the zh-Hans switch to it;
  - the download, signature-pin and silent-install path;
  - `UpdateMethod`, `StatusCode`, `ProjectEntry`, `ProjectInfo`, `UpdateFromServer`;
  - the Settings "Update method" card;
  - their strings.
- An old `"UpdateMethod": "Server"` in `update_settings.json` still loads.
- `UpdateWindow`: release notes, then **Open release page**.
- **Tests removed with the feature:** `VerifiedDownloadTests` (they tested the removed download path; nothing failed). `LockedFile` and `AuthenticodeVerifier` stay, as the Lenovo package helpers use them, and so do their tests.

### 5. Code-review fixes
- `Compatibility`:
  - `fake_mi.json` is read only in Debug builds; the safety report says a release build ignores it;
  - the first machine-information scan runs once (a lock);
  - `GetMachineInformation()` (new) replaces `.Result` at five places that can't await (constructors and a static property). It returns the value loaded at start-up, and otherwise loads it on a worker thread, so it can't deadlock the caller. The plan said `await`, but these places can't await.
- `KeyboardBacklightPage`: turning off Dynamic Lighting writes the registry directly instead of starting `reg.exe` and waiting for it.
- `PawnIOHelper.IsPawnIOServiceRunning`: `ServiceController` instead of starting `sc.exe` and parsing its text.
- **14 silent `catch { }` blocks:**
  - most now log what failed;
  - the two per-process ones in the exclude-processes window catch only the expected exceptions, with a comment;
  - the status window doesn't log a cancelled read.
- `SunriseSunset`: coordinates are parsed culture-independently and must be in range. The lookup is plain HTTP; the service has no free HTTPS.

### Tests
**New:**
- `AutomationRulesTests`;
- `PipelineExpansionTests` (the quick action cycle, unsupported steps skipped, step numbers);
- `AutomationLoopGuardTests`;
- `RunTargetCheckTests` (real files and permissions);
- `UpdateCheckerTests`;
- `SunriseSunsetTests`;
- safety report Run step rows.

**871 tests**, all passing.

**Honesty check:**
- The quick action cycle can't fail first, because a stack overflow ends the test run. The cycle that recursed (A runs B; B and C run each other) was traced through the old code instead.
- Breaking five guards one at a time made a test fail each time:
  - the loop limit;
  - the owner rule;
  - the AI Engine rule;
  - the unsupported-step skip;
  - HTTPS-only release pages.

Not verified on the laptop yet.

## Round 11: the app becomes Indra (2026-11-19)
The fork is now presented as **Indra – Legion Toolkit Mod**, developed and maintained by Nirmay Kadam.

**What GPL-3.0 requires, and how it is kept:**
- The original copyright notice and the licence stay. About keeps them reachable under **Original project and licence**: the original copyright, GPL-3.0, no warranty, a link to the licence and one to the original project.
- The build's file properties (`Directory.Build.props`) and the installer's copyright carry both notices.

**What deliberately stays the same:**
- the program file (`Lenovo Legion Toolkit.exe`) and the assembly name;
- the install folder and the settings in it;
- the installer AppId, so it upgrades in place;
- the autostart task and the CLI pipe;
- the process name LLT uses to recognise itself;
- the HWiNFO sensor group name.

### App
- `Resource.resx`:
  - `AppName` and `AboutPage_AppName` are "Indra – Legion Toolkit Mod" (in every resx, because a name isn't translated);
  - sentences that name the app say "Indra";
  - new About strings;
  - `AboutPage_TranslationCredit` removed.
- `AboutPage.xaml(.cs)`:
  - maintainer and © 2026 Nirmay Kadam;
  - links to this repository and its latest release (`UpdateChecker.LatestReleasePage`);
  - the "Original project and licence" expander. **View the licence** opens the installed `LICENSE` in Notepad, or this repository's LICENSE when that file isn't there.
- `LanguageSelectorWindow.xaml` uses `Resource.AppName`.
- `UnsupportedWindow.xaml`: its GitHub link points to this repository. A test found this one.
- **Removed:**
  - the Donate page (`DonatePage.xaml(.cs)`) and its menu entry;
  - its three photos and their csproj entries;
  - `Constants.cs` (only donation addresses were left);
  - the `DonatePage_*` and `MainWindow_NavigationItem_Donate` strings in every resx.

### Build and installer
- `Directory.Build.props` `Copyright`: "© 2026 Nirmay Kadam. Based on Lenovo Legion Toolkit © 2026 Bartosz Cichecki, Kaguya, and Dr. Skinner."
- `make_installer.iss`, `make_installer_action.iss`:
  - **Changed:** name, publisher (Nirmay Kadam), URLs (this repository) and copyright.
  - **Removed:** the three Discord links, their handlers and messages.
  - **Added:** `[InstallDelete]` removes the old "Lenovo Legion Toolkit" shortcuts on upgrade.
  - **Encoding:** saved as UTF-8 with a byte-order mark, so the en dash and © are read correctly. Both compile with Inno Setup 6.

### Tests
- **`AppIdentityTests` (new):**
  - the name;
  - About's maintainer and GPL notice;
  - only the expander links to the original repository;
  - no Donate page;
  - both copyright lines in the build;
  - in both installer scripts: unchanged AppId, folder and program file, the new name, publisher and URL, no Discord, a byte-order mark, and the old shortcuts removed.
- **878 tests**, all passing.

Not verified on the laptop yet.
