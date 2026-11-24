using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Utils;

public enum SafetySection
{
    Protections,
    InEffect,
    AppSecurity,
    SettingsFiles,
}

/// <summary>One line of the report. <see cref="Attention"/> marks something worth a look (shown in the caution colour).</summary>
public sealed record SafetyRow(SafetySection Section, string Item, string State, bool Attention = false);

public sealed record GameSafeInfo(bool Setting, bool Active, IReadOnlyList<string> AntiCheatServices, bool ForcedByGameProfile);

public sealed record GpuOverclockState(bool Enabled, GPUOverclockInfo Values, bool ApplyPending, bool DisabledAfterInterruptedApply, bool AfterburnerRunning);

public sealed record GodModeInfo(GodModeSettings.GodModeSettingsStore.Preset? ActivePreset);

public sealed record FanCurvesInfo(bool Enabled, FanCurveFirmwareSupport FirmwareSupport, IReadOnlyList<(PowerModeState Mode, FanCurveSource Source)> Curves);

public sealed record GameProfileInfo(string? ActiveGame, IReadOnlyList<string> EasedApps, IReadOnlyList<string> PausedServices);

public sealed record SettingsFolderInfo(string Path, bool Protected, bool? WritableByNonAdmins);

public sealed record PluginInfo(bool ProgramFolderProtected, int Loaded, int LegacyFilesFound);

public sealed record SensorsInfo(bool Enabled, PawnIOState PawnIO);

/// <summary>The power mode now; <see cref="Supported"/> is false on laptops without Lenovo power modes.</summary>
public sealed record PowerModeInfo(bool Supported, PowerModeState? Current);

/// <summary>One file in the settings folder: name, size and when it last changed (no contents).</summary>
public sealed record SettingsFile(string Name, long Size, DateTime LastWriteUtc);

public sealed record SettingsFilesInfo(IReadOnlyList<SettingsFile> Files, DateTime NowUtc);

/// <summary>
/// What the report is built from. Each part is read on its own; null means it couldn't be read, and the report
/// says so instead of guessing.
/// </summary>
public sealed record SafetyInputs(
    GameSafeInfo? GameSafe,
    GpuOverclockState? Gpu,
    GodModeInfo? GodMode,
    FanCurvesInfo? FanCurves,
    GameProfileInfo? GameProfile,
    SettingsFolderInfo? SettingsFolder,
    PluginInfo? Plugins,
    AutorunState? Autostart,
    bool? CliEnabled,
    SensorsInfo? Sensors,
    PowerModeInfo? PowerMode,
    SettingsFilesInfo? SettingsFiles,
    BackgroundServicesInfo? BackgroundServices,
    RunStepsInfo? RunSteps = null,
    GpuDriverErrorsInfo? GpuDriverErrors = null);

/// <summary>GPU driver errors in Windows' System log over the last day, newest first.</summary>
public sealed record GpuDriverErrorsInfo(IReadOnlyList<GpuDriverError> Errors, DateTime NowUtc);

/// <summary>Programs that automation Run steps start (as administrator) and that other programs could replace, or that have no folder.</summary>
public sealed record RunStepsInfo(IReadOnlyList<string> AtRisk);

/// <summary>Whether services are kept stopped during normal use, and which ones LLT has stopped now.</summary>
public sealed record BackgroundServicesInfo(bool Enabled, IReadOnlyList<string> StoppedByLlt);

/// <summary>
/// The in-app safety report: what LLT is changing on this laptop now and which protections are on, in the order
/// of docs/SAFETY.md. Pure: everything comes in through <see cref="SafetyInputs"/>.
/// </summary>
public static class SafetyReport
{
    public static IReadOnlyList<SafetyRow> Build(SafetyInputs inputs)
    {
        var rows = new List<SafetyRow>();

        // ---- Protections ----
        rows.Add(GameSafeRow(inputs.GameSafe));
        rows.Add(CrashGuardRow(inputs.Gpu));

        // ---- In effect now ----
        rows.Add(PowerModeRow(inputs.PowerMode));
        rows.Add(GpuOverclockRow(inputs.Gpu));
        rows.AddRange(GodModeRows(inputs.GodMode, inputs.PowerMode?.Current == PowerModeState.GodMode));
        rows.AddRange(FanCurveRows(inputs.FanCurves));
        rows.Add(GameProfileRow(inputs.GameProfile));
        rows.Add(BackgroundServicesRow(inputs.BackgroundServices));
        rows.Add(GpuDriverErrorsRow(inputs.GpuDriverErrors));

        // ---- App security ----
        rows.Add(SettingsFolderRow(inputs.SettingsFolder));
        rows.AddRange(PluginRows(inputs.Plugins));
        rows.Add(new(SafetySection.AppSecurity, Resource.SafetyReport_Autostart, inputs.Autostart is { } autostart ? autostart.GetDisplayName() : Resource.SafetyReport_Unknown));
        rows.Add(new(SafetySection.AppSecurity, Resource.SafetyReport_Cli, inputs.CliEnabled switch
        {
            true => Resource.SafetyReport_On,
            false => Resource.SafetyReport_Off,
            null => Resource.SafetyReport_Unknown,
        }));
        rows.Add(SensorsRow(inputs.Sensors));
        rows.Add(RunStepsRow(inputs.RunSteps));

        // ---- Settings files ----
        rows.AddRange(SettingsFileRows(inputs.SettingsFiles));

        return rows;
    }

    /// <summary>The report as plain text, for pasting into a bug report.</summary>
    public static string ToText(IReadOnlyList<SafetyRow> rows, DateTime generatedLocal)
    {
        var text = new StringBuilder();
        text.AppendLine($"{Resource.SafetyReport_Title} ({generatedLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)})");   // the same in every language, for copying

        foreach (var section in rows.GroupBy(r => r.Section))
        {
            text.AppendLine();
            text.AppendLine(SectionTitle(section.Key));
            foreach (var row in section)
                text.AppendLine($"{(row.Attention ? "! " : "- ")}{row.Item}: {row.State}");
        }

        return text.ToString();
    }

    public static string SectionTitle(SafetySection section) => section switch
    {
        SafetySection.Protections => Resource.SafetyReport_Section_Protections,
        SafetySection.InEffect => Resource.SafetyReport_Section_InEffect,
        SafetySection.SettingsFiles => Resource.SafetyReport_Section_SettingsFiles,
        _ => Resource.SafetyReport_Section_AppSecurity,
    };

    private static SafetyRow GameSafeRow(GameSafeInfo? info)
    {
        const SafetySection section = SafetySection.Protections;
        if (info is null)
            return new(section, Resource.SafetyReport_GameSafe, Resource.SafetyReport_Unknown);

        string state;
        if (info.Active && info.ForcedByGameProfile && info.AntiCheatServices.Count == 0)
            state = Resource.SafetyReport_GameSafe_ActiveProfile;
        else if (info.Active)
            state = string.Format(Resource.SafetyReport_GameSafe_Active, string.Join(", ", info.AntiCheatServices));
        else if (info.Setting)
            state = Resource.SafetyReport_GameSafe_Ready;
        else
            state = Resource.SafetyReport_GameSafe_Off;

        return new(section, Resource.SafetyReport_GameSafe, state);
    }

    private static SafetyRow CrashGuardRow(GpuOverclockState? info)
    {
        const SafetySection section = SafetySection.Protections;
        if (info is null)
            return new(section, Resource.SafetyReport_CrashGuard, Resource.SafetyReport_Unknown);

        if (info.DisabledAfterInterruptedApply)
            return new(section, Resource.SafetyReport_CrashGuard, Resource.SafetyReport_CrashGuard_Disabled, Attention: true);

        return new(section, Resource.SafetyReport_CrashGuard, info.ApplyPending ? Resource.SafetyReport_CrashGuard_Pending : Resource.SafetyReport_CrashGuard_Clear);
    }

    private static SafetyRow GpuOverclockRow(GpuOverclockState? info)
    {
        const SafetySection section = SafetySection.InEffect;
        if (info is null)
            return new(section, Resource.SafetyReport_GpuOverclock, Resource.SafetyReport_Unknown);

        if (info.AfterburnerRunning)
            return new(section, Resource.SafetyReport_GpuOverclock, Resource.SafetyReport_GpuOverclock_Afterburner);

        if (!info.Enabled)
            return new(section, Resource.SafetyReport_GpuOverclock, Resource.SafetyReport_Off);

        var parts = new List<string>
        {
            string.Format(Resource.SafetyReport_GpuOverclock_Core, Signed(info.Values.CoreDeltaMhz)),
            string.Format(Resource.SafetyReport_GpuOverclock_Memory, Signed(info.Values.MemoryDeltaMhz)),
        };
        if (info.Values.VoltageLockMv > 0)
            parts.Add(string.Format(Resource.SafetyReport_GpuOverclock_VoltageLock, info.Values.VoltageLockMv));
        if (info.Values.VoltageCapMv > 0)
            parts.Add(string.Format(Resource.SafetyReport_GpuOverclock_VoltageCap, info.Values.VoltageCapMv));

        return new(section, Resource.SafetyReport_GpuOverclock, string.Join(", ", parts));
    }

    private static SafetyRow BackgroundServicesRow(BackgroundServicesInfo? info)
    {
        const SafetySection section = SafetySection.InEffect;
        return info switch
        {
            null => new(section, Resource.SafetyReport_BackgroundServices, Resource.SafetyReport_Unknown),
            { StoppedByLlt.Count: > 0 } => new(section, Resource.SafetyReport_BackgroundServices,
                string.Format(Resource.SafetyReport_BackgroundServices_Stopped, info.StoppedByLlt.Count, string.Join(", ", info.StoppedByLlt))),
            { Enabled: true } => new(section, Resource.SafetyReport_BackgroundServices, Resource.SafetyReport_BackgroundServices_NoneStopped),
            _ => new(section, Resource.SafetyReport_BackgroundServices, Resource.SafetyReport_BackgroundServices_Off),
        };
    }

    private static SafetyRow PowerModeRow(PowerModeInfo? info)
    {
        const SafetySection section = SafetySection.InEffect;
        return info switch
        {
            null or { Supported: true, Current: null } => new(section, Resource.SafetyReport_PowerMode, Resource.SafetyReport_Unknown),
            { Supported: false } => new(section, Resource.SafetyReport_PowerMode, Resource.SafetyReport_PowerMode_NotAvailable),
            { Current: { } mode } => new(section, Resource.SafetyReport_PowerMode, mode.GetDisplayName()),
        };
    }

    private static IEnumerable<SafetyRow> SettingsFileRows(SettingsFilesInfo? info)
    {
        const SafetySection section = SafetySection.SettingsFiles;
        if (info is null)
        {
            yield return new(section, Resource.SafetyReport_Section_SettingsFiles, Resource.SafetyReport_Unknown);
            yield break;
        }

        if (info.Files.Count == 0)
        {
            yield return new(section, Resource.SafetyReport_Section_SettingsFiles, Resource.SafetyReport_Files_None);
            yield break;
        }

        foreach (var file in info.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var size = FormatSize(file.Size);
            var changed = file.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var name = file.Name;

            if (name.Equals("fake_mi.json", StringComparison.OrdinalIgnoreCase))
                yield return new(section, name, string.Format(FakeMachineFileMessage, size, changed), Attention: true);
            else if (name.Contains("_backup_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                yield return new(section, name, string.Format(Resource.SafetyReport_File_Backup, size, changed), Attention: true);
            else if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && info.NowUtc - file.LastWriteUtc > StaleTempAge)
                yield return new(section, name, string.Format(Resource.SafetyReport_File_StaleTemp, size, changed), Attention: true);
            else
                yield return new(section, name, string.Format(Resource.SafetyReport_File_Size, size, changed));
        }
    }

#if DEBUG
    private static string FakeMachineFileMessage => Resource.SafetyReport_File_FakeMachine;
#else
    /// <summary>Release builds never read fake_mi.json (see Compatibility).</summary>
    private static string FakeMachineFileMessage => Resource.SafetyReport_File_FakeMachineIgnored;
#endif

    /// <summary>A temporary file this old is left over from an interrupted save (newer ones may be a save in progress).</summary>
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromDays(1);

    private static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} KB";

    private static IEnumerable<SafetyRow> GodModeRows(GodModeInfo? info, bool inUseNow)
    {
        const SafetySection section = SafetySection.InEffect;
        if (info is null)
        {
            yield return new(section, Resource.SafetyReport_GodMode, Resource.SafetyReport_Unknown);
            yield break;
        }

        if (info.ActivePreset is not { } preset)
        {
            yield return new(section, Resource.SafetyReport_GodMode, Resource.SafetyReport_GodMode_None);
            yield break;
        }

        yield return new(section, Resource.SafetyReport_GodMode, inUseNow
            ? string.Format(Resource.SafetyReport_GodMode_InUse, preset.Name)
            : string.Format(Resource.SafetyReport_GodMode_Preset, preset.Name));

        var values = new List<string>();
        void Add(StepperValue? value, string format)
        {
            if (value is { } v)
                values.Add(string.Format(format, v.Value));
        }

        Add(preset.CPULongTermPowerLimit, Resource.SafetyReport_GodMode_CpuLongTerm);
        Add(preset.CPUShortTermPowerLimit, Resource.SafetyReport_GodMode_CpuShortTerm);
        Add(preset.CPUTemperatureLimit, Resource.SafetyReport_GodMode_CpuTemperature);
        Add(preset.GPUConfigurableTGP, Resource.SafetyReport_GodMode_GpuTgp);
        Add(preset.GPUPowerBoost, Resource.SafetyReport_GodMode_GpuBoost);
        Add(preset.GPUTemperatureLimit, Resource.SafetyReport_GodMode_GpuTemperature);
        if (preset.FanTable is not null)
            values.Add(Resource.SafetyReport_GodMode_FanCurve);
        if (preset.FanFullSpeed == true)
            values.Add(Resource.SafetyReport_GodMode_FanFullSpeed);

        if (values.Count > 0)
            yield return new(section, Resource.SafetyReport_GodMode_Values, string.Join(", ", values));
    }

    private static IEnumerable<SafetyRow> FanCurveRows(FanCurvesInfo? info)
    {
        const SafetySection section = SafetySection.InEffect;
        if (info is null)
        {
            yield return new(section, Resource.SafetyReport_FanCurves, Resource.SafetyReport_Unknown);
            yield break;
        }

        if (!info.Enabled || info.Curves.Count == 0)
        {
            yield return new(section, Resource.SafetyReport_FanCurves, Resource.SafetyReport_FanCurves_Firmware);
            yield break;
        }

        yield return new(section, Resource.SafetyReport_FanCurves, info.FirmwareSupport switch
        {
            FanCurveFirmwareSupport.Accepts => Resource.SafetyReport_FanCurves_Accepts,
            FanCurveFirmwareSupport.Rejects => Resource.SafetyReport_FanCurves_Rejects,
            _ => Resource.SafetyReport_FanCurves_NotTested,
        });

        foreach (var (mode, source) in info.Curves)
        {
            var (state, attention) = source switch
            {
                FanCurveSource.UserCurve => (Resource.SafetyReport_FanCurve_InUse, false),
                FanCurveSource.UserCurvePending => (Resource.SafetyReport_FanCurve_Pending, false),
                FanCurveSource.UserCurveNotKept => (Resource.SafetyReport_FanCurve_NotKept, true),
                _ => (Resource.SafetyReport_FanCurve_FirmwareInstead, false),
            };
            yield return new(section, string.Format(Resource.SafetyReport_FanCurve_Mode, mode.GetDisplayName()), state, attention);
        }
    }

    private static SafetyRow GameProfileRow(GameProfileInfo? info)
    {
        const SafetySection section = SafetySection.InEffect;
        if (info is null)
            return new(section, Resource.SafetyReport_GameProfile, Resource.SafetyReport_Unknown);

        if (info.ActiveGame is null)
            return new(section, Resource.SafetyReport_GameProfile, Resource.SafetyReport_GameProfile_None);

        var parts = new List<string> { info.ActiveGame };
        if (info.EasedApps.Count > 0)
            parts.Add(string.Format(Resource.SafetyReport_GameProfile_Eased, string.Join(", ", info.EasedApps)));
        if (info.PausedServices.Count > 0)
            parts.Add(string.Format(Resource.SafetyReport_GameProfile_Paused, string.Join(", ", info.PausedServices)));

        return new(section, Resource.SafetyReport_GameProfile, string.Join("; ", parts));
    }

    private static SafetyRow SettingsFolderRow(SettingsFolderInfo? info)
    {
        const SafetySection section = SafetySection.AppSecurity;
        if (info is null)
            return new(section, Resource.SafetyReport_SettingsFolder, Resource.SafetyReport_Unknown);

        if (info.WritableByNonAdmins == true || !info.Protected)
            return new(section, Resource.SafetyReport_SettingsFolder, string.Format(Resource.SafetyReport_SettingsFolder_Open, info.Path), Attention: true);

        var state = info.WritableByNonAdmins is null
            ? string.Format(Resource.SafetyReport_SettingsFolder_ProtectedUnchecked, info.Path)
            : string.Format(Resource.SafetyReport_SettingsFolder_Protected, info.Path);
        return new(section, Resource.SafetyReport_SettingsFolder, state);
    }

    private static IEnumerable<SafetyRow> PluginRows(PluginInfo? info)
    {
        const SafetySection section = SafetySection.AppSecurity;
        if (info is null)
        {
            yield return new(section, Resource.SafetyReport_Plugins, Resource.SafetyReport_Unknown);
            yield break;
        }

        yield return new(section, Resource.SafetyReport_Plugins, info.ProgramFolderProtected
            ? string.Format(Resource.SafetyReport_Plugins_Loaded, info.Loaded)
            : Resource.SafetyReport_Plugins_Blocked);

        if (info.LegacyFilesFound > 0)
            yield return new(section, Resource.SafetyReport_Plugins_Legacy, string.Format(Resource.SafetyReport_Plugins_LegacyFound, info.LegacyFilesFound), Attention: true);
    }

    private static SafetyRow SensorsRow(SensorsInfo? info)
    {
        const SafetySection section = SafetySection.AppSecurity;
        if (info is null)
            return new(section, Resource.SafetyReport_Sensors, Resource.SafetyReport_Unknown);

        if (!info.Enabled)
            return new(section, Resource.SafetyReport_Sensors, Resource.SafetyReport_Off);

        var pawnIO = info.PawnIO switch
        {
            PawnIOState.Installed => Resource.SafetyReport_PawnIO_Installed,
            PawnIOState.UpdateRequired => Resource.SafetyReport_PawnIO_Update,
            PawnIOState.ServiceNotRunning => Resource.SafetyReport_PawnIO_NotRunning,
            _ => Resource.SafetyReport_PawnIO_NotInstalled,
        };
        return new(section, Resource.SafetyReport_Sensors, string.Format(Resource.SafetyReport_Sensors_On, pawnIO));
    }

    private static SafetyRow GpuDriverErrorsRow(GpuDriverErrorsInfo? info)
    {
        const SafetySection section = SafetySection.InEffect;
        if (info is null)
            return new(section, Resource.SafetyReport_GpuDriverErrors, Resource.SafetyReport_Unknown);

        if (info.Errors.Count == 0)
            return new(section, Resource.SafetyReport_GpuDriverErrors, Resource.SafetyReport_GpuDriverErrors_None);

        var listed = info.Errors.Take(5).Select(e =>
        {
            var when = e.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(e.Context) ? $"{when} ({e.Source} {e.EventId})" : $"{when} ({e.Source} {e.EventId}; {e.Context})";
        });
        return new(section, Resource.SafetyReport_GpuDriverErrors,
            string.Format(Resource.SafetyReport_GpuDriverErrors_Some, info.Errors.Count, string.Join("; ", listed)), Attention: true);
    }

    private static SafetyRow RunStepsRow(RunStepsInfo? info)
    {
        const SafetySection section = SafetySection.AppSecurity;
        return info switch
        {
            null => new(section, Resource.SafetyReport_RunSteps, Resource.SafetyReport_Unknown),
            { AtRisk.Count: 0 } => new(section, Resource.SafetyReport_RunSteps, Resource.SafetyReport_RunSteps_None),
            _ => new(section, Resource.SafetyReport_RunSteps,
                string.Format(Resource.SafetyReport_RunSteps_AtRisk, info.AtRisk.Count, string.Join(", ", info.AtRisk)), Attention: true),
        };
    }

    private static string Signed(int value) => value > 0 ? $"+{value}" : $"{value}";
}
