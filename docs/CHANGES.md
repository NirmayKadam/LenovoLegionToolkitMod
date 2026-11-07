# Changes from Lenovo Legion Toolkit v2.36.0.0

This file is the modification notice required by GPL-3.0 section 5(a).

- **Original work:** Lenovo Legion Toolkit v2.36.0.0, © Bartosz Cichecki, Kaguya, Dr. Skinner and contributors, licensed under GPL-3.0. Source: https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/releases/tag/v2.36.0.0
- **Modified by:** Nirmay Kadam
- **Date of modifications:** 2026-11-07
- **License of this modified version:** GPL-3.0 (see [LICENSE](../LICENSE))

What changed and why: [BUG-FIXES.md](BUG-FIXES.md) and [SECURITY.md](SECURITY.md).

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
