using System;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
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
        new SensorsInfo(Enabled: false, PawnIOState.NotInstalled));

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
        Assert.Equal([SafetySection.Protections, SafetySection.InEffect, SafetySection.AppSecurity], sections);
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
}
