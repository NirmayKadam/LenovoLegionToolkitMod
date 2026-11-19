using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Station.Core;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Gathers what the safety report shows from settings, in-memory state, the process list, the registry, Task
/// Scheduler, the settings folder's permissions and file list (names, sizes, dates; no contents), and one
/// read-only Lenovo WMI query for the power mode. It calls no hardware driver and changes nothing. In particular
/// it avoids GPUOverclockController.GetState() (its first call in a run can write settings),
/// GodModeController.GetActivePresetAsync() (it can save a default preset) and the sensors' IsSupportedAsync() (it
/// starts the driver). Each part is read on its own; one that fails is logged and shown as unknown.
/// </summary>
public static class SafetyReportCollector
{
    public static async Task<SafetyInputs> CollectAsync()
    {
        var powerMode = await ReadPowerModeAsync().ConfigureAwait(false);

        return new(
        Read("game-safe mode", () => new GameSafeInfo(
            IoCContainer.Resolve<ApplicationSettings>().Store.GameSafeMode,
            IoCContainer.Resolve<GameSafeMode>().IsActive,
            [.. IoCContainer.Resolve<AntiCheatMonitor>().ActiveServices],
            IoCContainer.Resolve<GameProfileController>().ForceGameSafe)),

        Read("GPU overclock", () =>
        {
            var store = IoCContainer.Resolve<GPUOverclockSettings>().Store;
            return new GpuOverclockState(store.Enabled, store.Info, store.PendingInfo is not null, store.DisabledAfterInterruptedApply, AfterburnerDetector.IsRunning());
        }),

        Read("Custom mode", () =>
        {
            var store = IoCContainer.Resolve<GodModeSettings>().Store;
            return new GodModeInfo(store.Presets.TryGetValue(store.ActivePresetId, out var preset) ? preset : null);
        }),

        Read("fan curves", () =>
        {
            var store = IoCContainer.Resolve<FanProfileSettings>().Store;
            var controller = IoCContainer.Resolve<FanProfileController>();
            return new FanCurvesInfo(store.Enabled, store.FirmwareSupport,
                [.. store.Curves.Keys.Order().Select(mode => (mode, controller.CurveInUse(mode).Source))]);
        }),

        Read("game profile", () =>
        {
            var session = IoCContainer.Resolve<GameProfileSessionSettings>().Store;
            return new GameProfileInfo(
                IoCContainer.Resolve<GameProfileController>().ActiveGameName,
                [.. session.EasedApps.Select(app => app.ExeName).Distinct(StringComparer.OrdinalIgnoreCase)],
                [.. session.PausedServices]);
        }),

        Read("settings folder", () =>
        {
            var path = Folders.Settings;
            var isProtected = path.StartsWith(Path.Combine(Folders.Program, "UserData"), StringComparison.OrdinalIgnoreCase);
            bool? writable;
            try
            {
                writable = DirectoryProtection.IsWritableByNonAdmins(path);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Safety report: couldn't check the settings folder's permissions.", ex);
                writable = null;
            }
            return new SettingsFolderInfo(path, isProtected, writable);
        }),

        Read("plugins", () =>
        {
            var extensions = IoCContainer.Resolve<ExtensionManager>();
            return new PluginInfo(DirectoryProtection.IsProgramDirectoryProtected(), extensions.Providers.Count, extensions.LegacyPluginFiles.Count);
        }),

        ReadValue("autostart", () => Autorun.State),

        ReadValue("CLI", () => IoCContainer.Resolve<IntegrationsSettings>().Store.CLI),

        Read("hardware sensors", () => new SensorsInfo(IoCContainer.Resolve<ApplicationSettings>().Store.EnableHardwareSensors, PawnIOHelper.GetPawnIOState())),

        powerMode,

        Read("settings files", () =>
        {
            var files = new DirectoryInfo(Folders.Settings).GetFiles()
                .Select(f => new SettingsFile(f.Name, f.Length, f.LastWriteTimeUtc))
                .ToArray();
            return new SettingsFilesInfo(files, DateTime.UtcNow);
        }),

        Read("background services", () => new BackgroundServicesInfo(
            IoCContainer.Resolve<BackgroundServicesSettings>().Store.Enabled,
            [.. IoCContainer.Resolve<BackgroundServicesSessionSettings>().Store.StoppedServices])),

        Read("automation Run steps", () => new RunStepsInfo(
            [.. IoCContainer.Resolve<AutomationSettings>().Store.Pipelines
                .SelectMany(p => p.Steps)
                .OfType<RunAutomationStep>()
                .Where(s => RunTargetCheck.Check(s.ScriptPath).Problem is RunTargetProblem.Replaceable or RunTargetProblem.BareName)
                .Select(s => s.ScriptPath!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)])));
    }

    /// <summary>
    /// The current power mode: one read-only query of the Lenovo WMI interface, the same one the dashboard makes
    /// (game-safe mode allows it). Null when it couldn't be read.
    /// </summary>
    private static async Task<PowerModeInfo?> ReadPowerModeAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            if (!await feature.IsSupportedAsync().ConfigureAwait(false))
                return new PowerModeInfo(false, null);

            return new PowerModeInfo(true, await feature.GetStateAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Safety report: couldn't read the power mode.", ex);
            return null;
        }
    }

    private static T? Read<T>(string what, Func<T> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Safety report: couldn't read {what}.", ex);
            return null;
        }
    }

    private static T? ReadValue<T>(string what, Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Safety report: couldn't read {what}.", ex);
            return null;
        }
    }
}
