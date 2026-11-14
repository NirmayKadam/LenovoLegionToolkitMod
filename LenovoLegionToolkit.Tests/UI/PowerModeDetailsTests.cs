using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls.Dashboard;
using LenovoLegionToolkit.WPF.Resources;
using Xunit;
using Preset = LenovoLegionToolkit.Lib.Settings.GodModeSettings.GodModeSettingsStore.Preset;

namespace LenovoLegionToolkit.Tests.UI;

// Inputs are hand-made fixtures shaped like what the firmware and God Mode presets return.
public class PowerModeDetailsTests
{
    private static StepperValue Step(int value) => new(value, 0, 200, 1, [], null);

    private static readonly GodModeDefaults Balance = new()
    {
        CPULongTermPowerLimit = 55,
        CPUShortTermPowerLimit = 90,
        CPUTemperatureLimit = 90,
        GPUConfigurableTGP = 100,
        GPUPowerBoost = 15,
        GPUTemperatureLimit = 87,
        CPUPeakPowerLimit = 0,          // firmware: not provided
        FanTable = new FanTable([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
    };

    private static readonly GodModeDefaults Performance = new()
    {
        CPULongTermPowerLimit = 80,
        CPUShortTermPowerLimit = 115,
        CPUTemperatureLimit = 95,
        GPUConfigurableTGP = 115,
        GPUPowerBoost = 25,
        GPUTemperatureLimit = 87,
    };

    // ---- Reading a mode's values ----

    [Fact]
    public void FirmwareMode_ListsProvidedValues_AndSkipsZeroOrMissing()
    {
        var p = PowerModeProfileReader.FromFirmwareDefaults(PowerModeState.Balance, Balance, null, null);

        Assert.Equal(55, p.Values[PowerModeValueKind.CpuLongTermPowerLimit]);
        Assert.Equal(100, p.Values[PowerModeValueKind.GpuConfigurableTgp]);
        Assert.False(p.Values.ContainsKey(PowerModeValueKind.CpuPeakPowerLimit));   // 0 = not provided
        Assert.False(p.Values.ContainsKey(PowerModeValueKind.ApuSpptPowerLimit));   // null
        Assert.Equal(6, p.Values.Count);
    }

    [Fact]
    public void FirmwareMode_DoesNotPassOffThePlaceholderTableAsTheFirmwareCurve()
    {
        var p = PowerModeProfileReader.FromFirmwareDefaults(PowerModeState.Balance, Balance, null, null);

        Assert.Equal(FanCurveSource.Firmware, p.FanSource);
        Assert.Null(p.FanTable);
    }

    [Fact]
    public void FirmwareMode_WithAUserCurve_ShowsIt()
    {
        ushort[] curve = [2, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var p = PowerModeProfileReader.FromFirmwareDefaults(PowerModeState.Quiet, null, curve, null);

        Assert.Equal(FanCurveSource.UserCurve, p.FanSource);
        Assert.Equal(curve, p.FanTable);
        Assert.Empty(p.Values); // laptop reported nothing for this mode
    }

    [Fact]
    public void CustomMode_ReadsThePreset_AndItsFanCurve()
    {
        var preset = new Preset
        {
            Name = "Gaming",
            CPULongTermPowerLimit = Step(70),
            GPUConfigurableTGP = Step(110),
            CPUTemperatureLimit = Step(92),
            FanTable = new FanTable([3, 3, 4, 5, 6, 7, 8, 9, 10, 10]),
        };

        // As sent: the same values, since they are inside the firmware's range.
        var sent = new GodModePreset { Name = "Gaming", CPULongTermPowerLimit = Step(70), GPUConfigurableTGP = Step(110), CPUTemperatureLimit = Step(92) };

        var p = PowerModeProfileReader.FromPreset(preset, sent, new GPUOverclockInfo(100, 500, 0, 0));

        Assert.Equal(PowerModeState.GodMode, p.Mode);
        Assert.Equal(70, p.Values[PowerModeValueKind.CpuLongTermPowerLimit]);
        Assert.Equal(110, p.Values[PowerModeValueKind.GpuConfigurableTgp]);
        Assert.Equal(FanCurveSource.CustomPreset, p.FanSource);
        Assert.Equal(new ushort[] { 3, 3, 4, 5, 6, 7, 8, 9, 10, 10 }, p.FanTable);
        Assert.Equal(100, p.GpuClockOffsets!.Value.CoreDeltaMhz);
    }

    [Fact]
    public void CustomMode_ShowsWhatIsSent_NotWhatIsSaved()
    {
        // Saved 140 W from an old backup; the firmware allows up to 100 W, so the default (65 W) is what's sent.
        var saved = new Preset { Name = "Old backup", CPULongTermPowerLimit = Step(140) };
        var sent = new GodModePreset { Name = "Old backup", CPULongTermPowerLimit = Step(65), GPUConfigurableTGP = Step(100) };

        var p = PowerModeProfileReader.FromPreset(saved, sent, null);

        Assert.Equal(65, p.Values[PowerModeValueKind.CpuLongTermPowerLimit]);
        Assert.False(p.Values.ContainsKey(PowerModeValueKind.GpuConfigurableTgp));   // not saved, so not sent or shown
    }

    [Fact]
    public void CustomMode_RangesUnreadable_ListsNoLimits()
    {
        // Nothing is sent when the firmware's ranges can't be read, so nothing is shown as set.
        var saved = new Preset { Name = "Gaming", CPULongTermPowerLimit = Step(70), FanTable = new FanTable([3, 3, 4, 5, 6, 7, 8, 9, 10, 10]) };

        var p = PowerModeProfileReader.FromPreset(saved, null, null);

        Assert.Empty(p.Values);
        Assert.Equal(FanCurveSource.CustomPreset, p.FanSource);   // the fan step still runs
    }

    // ---- What changed (Fn+Q Balance -> Performance) ----

    [Fact]
    public void Compare_MarksExactlyTheValuesThatChanged()
    {
        var before = PowerModeProfileReader.FromFirmwareDefaults(PowerModeState.Balance, Balance, null, null);
        var after = PowerModeProfileReader.FromFirmwareDefaults(PowerModeState.Performance, Performance, null, null);

        var rows = PowerModeProfileReader.Compare(before, after).ToDictionary(r => r.Kind);

        Assert.True(rows[PowerModeValueKind.CpuLongTermPowerLimit].Changed);
        Assert.Equal(55, rows[PowerModeValueKind.CpuLongTermPowerLimit].Previous);
        Assert.Equal(80, rows[PowerModeValueKind.CpuLongTermPowerLimit].Current);
        Assert.False(rows[PowerModeValueKind.GpuTemperatureLimit].Changed); // 87 in both
    }

    [Fact]
    public void Compare_ValuesThatAppearOrDisappear_AreChanges()
    {
        var withSppt = new PowerModeProfile(PowerModeState.Balance, new global::System.Collections.Generic.Dictionary<PowerModeValueKind, int> { [PowerModeValueKind.ApuSpptPowerLimit] = 30 }, FanCurveSource.Firmware, null, null);
        var without = new PowerModeProfile(PowerModeState.Quiet, new global::System.Collections.Generic.Dictionary<PowerModeValueKind, int>(), FanCurveSource.Firmware, null, null);

        var gone = Assert.Single(PowerModeProfileReader.Compare(withSppt, without));
        Assert.True(gone.Changed);
        Assert.Equal(30, gone.Previous);
        Assert.Null(gone.Current);
    }

    [Fact]
    public void Compare_FirstTime_HasNoChanges_AndKeepsAFixedOrder()
    {
        var p = PowerModeProfileReader.FromFirmwareDefaults(PowerModeState.Performance, Performance, null, null);
        var rows = PowerModeProfileReader.Compare(null, p);

        Assert.All(rows, r => Assert.False(r.Changed));
        Assert.Equal(rows.Select(r => r.Kind).Order(), rows.Select(r => r.Kind));
    }

    // ---- Text ----

    [Fact]
    public void Value_ShowsOldToNewWhenChanged()
    {
        Assert.Equal($"55 {Resource.Watt} → 80 {Resource.Watt}", PowerModeDetailsFormat.Value(new(PowerModeValueKind.CpuLongTermPowerLimit, 55, 80), TemperatureUnit.C));
        Assert.Equal($"87 {Resource.Celsius}", PowerModeDetailsFormat.Value(new(PowerModeValueKind.GpuTemperatureLimit, 87, 87), TemperatureUnit.C));
        Assert.Equal($"— → 30 {Resource.Watt}", PowerModeDetailsFormat.Value(new(PowerModeValueKind.ApuSpptPowerLimit, null, 30), TemperatureUnit.C));
    }

    [Fact]
    public void Single_UsesTheRightUnit()
    {
        Assert.Equal("28 s", PowerModeDetailsFormat.Single(PowerModeValueKind.CpuPl2Tau, 28, TemperatureUnit.C));
        Assert.Equal($"203 {Resource.Fahrenheit}", PowerModeDetailsFormat.Single(PowerModeValueKind.CpuTemperatureLimit, 95, TemperatureUnit.F));
        Assert.Equal($"115 {Resource.Watt}", PowerModeDetailsFormat.Single(PowerModeValueKind.GpuConfigurableTgp, 115, TemperatureUnit.F)); // watts unaffected
    }

    [Theory]
    [InlineData(100, "+100")]
    [InlineData(-50, "-50")]
    [InlineData(0, "0")]
    public void Signed_ShowsTheSign(int value, string expected) => Assert.Equal(expected, PowerModeDetailsFormat.Signed(value));

    [Fact]
    public void BarHeights_RelativeToTheHighestStep_WithAVisibleMinimum()
    {
        var heights = PowerModeDetailsFormat.BarHeights([0, 5, 10], 34);
        Assert.Equal([3, 17, 34], heights);
        Assert.Empty(PowerModeDetailsFormat.BarHeights([], 34));
    }

    [Fact]
    public void EveryValueHasAName()
    {
        foreach (var kind in global::System.Enum.GetValues<PowerModeValueKind>())
            Assert.NotEqual(kind.ToString(), PowerModeDetailsFormat.Label(kind));
    }
}
