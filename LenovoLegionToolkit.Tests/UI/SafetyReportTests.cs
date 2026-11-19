using System;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The safety report built from given inputs: what each state says, what is flagged, and "couldn't read" when a
/// part wasn't readable. The inputs are plain values; on the laptop they come from LLT's settings and state.
/// </summary>
public class SafetyReportTests
{
    private static SafetyInputs Calm() => new(
        new GameSafeInfo(Setting: true, Active: false, AntiCheatServices: [], ForcedByGameProfile: false),
        new GpuOverclockState(Enabled: false, GPUOverclockInfo.Zero, ApplyPending: false, DisabledAfterInterruptedApply: false, AfterburnerRunning: false),
        new GodModeInfo(null),
        new FanCurvesInfo(Enabled: false, FanCurveFirmwareSupport.Unknown, []),
        new GameProfileInfo(null, [], []),
        new SettingsFolderInfo(@"C:\Program Files\LenovoLegionToolkit\UserData\S-1-5-21", Protected: true, WritableByNonAdmins: false),
        new PluginInfo(ProgramFolderProtected: true, Loaded: 0, LegacyFilesFound: 0),
        AutorunState.Enabled,
        CliEnabled: false,
        new SensorsInfo(Enabled: false, PawnIOState.NotInstalled),
        new PowerModeInfo(Supported: true, PowerModeState.Balance),
        new SettingsFilesInfo([new SettingsFile("settings.json", 2048, Now.AddDays(-3))], Now),
        new BackgroundServicesInfo(Enabled: false, StoppedByLlt: []),
        new RunStepsInfo([]),
        new GpuDriverErrorsInfo([], Now));

    private static readonly DateTime Now = new(2026, 11, 18, 18, 0, 0, DateTimeKind.Utc);

    private static SafetyRow Row(SafetyInputs inputs, string item) => SafetyReport.Build(inputs).Single(r => r.Item == item);

    [Fact]
    public void ACalmLaptop_HasNothingFlagged()
    {
        Assert.DoesNotContain(SafetyReport.Build(Calm()), r => r.Attention);
    }

    [Fact]
    public void EverySection_IsPresent_InTheOrderOfSafetyMd()
    {
        var sections = SafetyReport.Build(Calm()).Select(r => r.Section).Distinct();
        Assert.Equal([SafetySection.Protections, SafetySection.InEffect, SafetySection.AppSecurity, SafetySection.SettingsFiles], sections);
    }

    // ---- Background services ----

    [Fact]
    public void BackgroundServices_SayWhatLltStopped_OrThatNothingIs()
    {
        var stopped = Calm() with { BackgroundServices = new BackgroundServicesInfo(true, ["SysMain", "DiagTrack"]) };
        var state = Row(stopped, Resource.SafetyReport_BackgroundServices).State;
        Assert.Contains("SysMain", state);
        Assert.Contains("DiagTrack", state);

        Assert.Equal(Resource.SafetyReport_BackgroundServices_NoneStopped, Row(Calm() with { BackgroundServices = new BackgroundServicesInfo(true, []) }, Resource.SafetyReport_BackgroundServices).State);
        Assert.Equal(Resource.SafetyReport_BackgroundServices_Off, Row(Calm(), Resource.SafetyReport_BackgroundServices).State);
    }

    // ---- Power mode ----

    [Fact]
    public void ThePowerMode_IsTheFirstRowOfWhatIsInEffect()
    {
        var first = SafetyReport.Build(Calm()).First(r => r.Section == SafetySection.InEffect);

        Assert.Equal(Resource.SafetyReport_PowerMode, first.Item);
        Assert.Equal(PowerModeState.Balance.GetDisplayName(), first.State);
    }

    [Fact]
    public void InCustomMode_ThePresetSaysItIsInUse()
    {
        var preset = new GodModeSettings.GodModeSettingsStore.Preset { Name = "Gaming" };
        var custom = Calm() with { GodMode = new GodModeInfo(preset), PowerMode = new PowerModeInfo(true, PowerModeState.GodMode) };
        var balance = Calm() with { GodMode = new GodModeInfo(preset) };

        Assert.Equal(string.Format(Resource.SafetyReport_GodMode_InUse, "Gaming"), Row(custom, Resource.SafetyReport_GodMode).State);
        Assert.Equal(string.Format(Resource.SafetyReport_GodMode_Preset, "Gaming"), Row(balance, Resource.SafetyReport_GodMode).State);
    }

    [Fact]
    public void APowerModeThatCouldNotBeRead_OrIsNotAvailable_SaysWhich()
    {
        Assert.Equal(Resource.SafetyReport_Unknown, Row(Calm() with { PowerMode = null }, Resource.SafetyReport_PowerMode).State);
        Assert.Equal(Resource.SafetyReport_PowerMode_NotAvailable, Row(Calm() with { PowerMode = new PowerModeInfo(false, null) }, Resource.SafetyReport_PowerMode).State);
    }

    // ---- Settings files ----

    private static SafetyInputs WithFiles(params SettingsFile[] files) => Calm() with { SettingsFiles = new SettingsFilesInfo(files, Now) };

    [Fact]
    public void EverySettingsFile_IsListed_WithItsSize()
    {
        var rows = SafetyReport.Build(WithFiles(new("godmode.json", 512, Now.AddHours(-1)), new("lang", 2, Now.AddDays(-30))))
            .Where(r => r.Section == SafetySection.SettingsFiles)
            .ToArray();

        Assert.Equal(["godmode.json", "lang"], rows.Select(r => r.Item));
        Assert.StartsWith("512 B", rows[0].State);
        Assert.DoesNotContain(rows, r => r.Attention);
    }

    [Theory]
    [InlineData("godmode_backup_20261118120000000.json", 0)]   // LLT found godmode.json unreadable and kept this copy
    [InlineData("fake_mi.json", 0)]                              // fake machine information in use
    [InlineData("settings.json.0a1b2c.tmp", 48)]                 // left over from an interrupted save, 2 days ago
    public void FilesWorthALook_AreFlagged(string name, int hoursOld)
    {
        var row = SafetyReport.Build(WithFiles(new SettingsFile(name, 100, Now.AddHours(-hoursOld - 1)))).Single(r => r.Item == name);
        Assert.True(row.Attention);
    }

    [Fact]
    public void AFreshTemporaryFile_IsNotFlagged_ItMayBeASaveInProgress()
    {
        var row = SafetyReport.Build(WithFiles(new SettingsFile("osd.json.0a1b2c.tmp", 100, Now.AddMinutes(-1)))).Single(r => r.Item == "osd.json.0a1b2c.tmp");
        Assert.False(row.Attention);
    }

    [Fact]
    public void SettingsFilesThatCouldNotBeListed_SaySo()
    {
        var row = SafetyReport.Build(Calm() with { SettingsFiles = null }).Single(r => r.Section == SafetySection.SettingsFiles);
        Assert.Equal(Resource.SafetyReport_Unknown, row.State);
    }

    [Fact]
    public void GameSafe_Active_NamesTheAntiCheat()
    {
        var inputs = Calm() with { GameSafe = new GameSafeInfo(true, true, ["vgc"], false) };
        Assert.Contains("vgc", Row(inputs, Resource.SafetyReport_GameSafe).State);
    }

    [Fact]
    public void GameSafe_ForcedByAProfileOnly_SaysSo()
    {
        var inputs = Calm() with { GameSafe = new GameSafeInfo(false, true, [], true) };
        Assert.Equal(Resource.SafetyReport_GameSafe_ActiveProfile, Row(inputs, Resource.SafetyReport_GameSafe).State);
    }

    [Fact]
    public void OverclockTurnedOffAfterACrash_IsFlagged()
    {
        var inputs = Calm() with { Gpu = new GpuOverclockState(false, GPUOverclockInfo.Zero, false, DisabledAfterInterruptedApply: true, false) };
        Assert.True(Row(inputs, Resource.SafetyReport_CrashGuard).Attention);
    }

    [Fact]
    public void Overclock_ShowsTheOffsets_AndAfterburnerWins()
    {
        var on = Calm() with { Gpu = new GpuOverclockState(true, new GPUOverclockInfo(150, -200), false, false, false) };
        var state = Row(on, Resource.SafetyReport_GpuOverclock).State;
        Assert.Contains("+150", state);
        Assert.Contains("-200", state);

        var withAfterburner = Calm() with { Gpu = new GpuOverclockState(true, new GPUOverclockInfo(150, 0), false, false, AfterburnerRunning: true) };
        Assert.Equal(Resource.SafetyReport_GpuOverclock_Afterburner, Row(withAfterburner, Resource.SafetyReport_GpuOverclock).State);
    }

    [Fact]
    public void CustomMode_ShowsThePresetAndItsSavedValues()
    {
        var preset = new GodModeSettings.GodModeSettingsStore.Preset
        {
            Name = "Gaming",
            CPULongTermPowerLimit = new StepperValue(55, 15, 80, 1, [], null),
            FanFullSpeed = true,
        };
        var inputs = Calm() with { GodMode = new GodModeInfo(preset) };

        Assert.Contains("Gaming", Row(inputs, Resource.SafetyReport_GodMode).State);
        var values = Row(inputs, Resource.SafetyReport_GodMode_Values).State;
        Assert.Contains("55", values);
        Assert.Contains(Resource.SafetyReport_GodMode_FanFullSpeed, values);
    }

    [Fact]
    public void AFanCurveTheFirmwareDidNotKeep_IsFlagged()
    {
        var inputs = Calm() with
        {
            FanCurves = new FanCurvesInfo(true, FanCurveFirmwareSupport.Unknown,
                [(PowerModeState.Balance, FanCurveSource.UserCurve), (PowerModeState.Quiet, FanCurveSource.UserCurveNotKept)]),
        };
        var rows = SafetyReport.Build(inputs);

        Assert.Single(rows, r => r.Attention);
        Assert.True(rows.Single(r => r.State == Resource.SafetyReport_FanCurve_NotKept).Attention);
    }

    [Fact]
    public void ActiveGameProfile_ListsWhatWasChanged()
    {
        var inputs = Calm() with { GameProfile = new GameProfileInfo("VALORANT", ["Discord.exe"], ["InventorySvc"]) };
        var state = Row(inputs, Resource.SafetyReport_GameProfile).State;

        Assert.Contains("VALORANT", state);
        Assert.Contains("Discord.exe", state);
        Assert.Contains("InventorySvc", state);
    }

    [Theory]
    [InlineData(false, false)]   // not the protected folder (portable / not elevated)
    [InlineData(true, true)]     // protected folder, but someone opened its permissions
    public void AnUnprotectedSettingsFolder_IsFlagged(bool isProtected, bool writable)
    {
        var inputs = Calm() with { SettingsFolder = new SettingsFolderInfo(@"C:\x", isProtected, writable) };
        Assert.True(Row(inputs, Resource.SafetyReport_SettingsFolder).Attention);
    }

    [Fact]
    public void OldPluginFiles_AreFlagged()
    {
        var inputs = Calm() with { Plugins = new PluginInfo(true, 0, LegacyFilesFound: 2) };
        Assert.True(Row(inputs, Resource.SafetyReport_Plugins_Legacy).Attention);
    }

    [Fact]
    public void APartThatCouldNotBeRead_SaysSo_InsteadOfGuessing()
    {
        var inputs = Calm() with { Gpu = null, Autostart = null, SettingsFolder = null };
        var rows = SafetyReport.Build(inputs);

        Assert.Equal(Resource.SafetyReport_Unknown, Row(inputs, Resource.SafetyReport_GpuOverclock).State);
        Assert.Equal(Resource.SafetyReport_Unknown, Row(inputs, Resource.SafetyReport_CrashGuard).State);
        Assert.Equal(Resource.SafetyReport_Unknown, Row(inputs, Resource.SafetyReport_Autostart).State);
        Assert.Equal(Resource.SafetyReport_Unknown, Row(inputs, Resource.SafetyReport_SettingsFolder).State);
        Assert.DoesNotContain(rows, r => r.Attention);
    }

    [Fact]
    public void Text_HasEverySectionAndRow_AndMarksFlaggedOnes()
    {
        var inputs = Calm() with { Plugins = new PluginInfo(true, 0, 1) };
        var rows = SafetyReport.Build(inputs);

        var text = SafetyReport.ToText(rows, new DateTime(2026, 11, 18, 21, 30, 0));

        Assert.Contains("2026-11-18 21:30", text);
        Assert.Contains(Resource.SafetyReport_Section_AppSecurity, text);
        Assert.All(rows, r => Assert.Contains($"{r.Item}: {r.State}", text));
        Assert.Contains($"! {Resource.SafetyReport_Plugins_Legacy}:", text);
    }

    // ---- Automation Run steps ----

    [Fact]
    public void RunSteps_WithReplaceablePrograms_AreFlagged_WithTheirPaths()
    {
        var inputs = Calm() with { RunSteps = new RunStepsInfo([@"C:\MSI Afterburner\MSIAfterburner.exe", "tool.exe"]) };
        var row = Row(inputs, Resource.SafetyReport_RunSteps);

        Assert.True(row.Attention);
        Assert.Contains(@"C:\MSI Afterburner\MSIAfterburner.exe", row.State);
        Assert.Contains("tool.exe", row.State);
    }

    [Fact]
    public void RunSteps_NoneAtRisk_OrUnreadable()
    {
        Assert.Equal(Resource.SafetyReport_RunSteps_None, Row(Calm(), Resource.SafetyReport_RunSteps).State);
        Assert.Equal(Resource.SafetyReport_Unknown, Row(Calm() with { RunSteps = null }, Resource.SafetyReport_RunSteps).State);
    }

    // ---- GPU driver errors ----

    [Fact]
    public void GpuDriverErrors_AreFlagged_WithWhatWasKnown()
    {
        var error = new LenovoLegionToolkit.Lib.Utils.GpuDriverError(Now.AddHours(-2), "nvlddmkm", 153, "2 s after switching to Performance");
        var row = Row(Calm() with { GpuDriverErrors = new GpuDriverErrorsInfo([error], Now) }, Resource.SafetyReport_GpuDriverErrors);

        Assert.True(row.Attention);
        Assert.Contains("nvlddmkm 153", row.State);
        Assert.Contains("2 s after switching to Performance", row.State);
    }

    [Fact]
    public void GpuDriverErrors_NoneOrUnreadable()
    {
        Assert.Equal(Resource.SafetyReport_GpuDriverErrors_None, Row(Calm(), Resource.SafetyReport_GpuDriverErrors).State);
        Assert.Equal(Resource.SafetyReport_Unknown, Row(Calm() with { GpuDriverErrors = null }, Resource.SafetyReport_GpuDriverErrors).State);
    }
}