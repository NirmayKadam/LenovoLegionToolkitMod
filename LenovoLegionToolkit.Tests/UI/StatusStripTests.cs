using System.Globalization;
using System.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Controls.Dashboard;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils.Skins;
using Xunit;
using static LenovoLegionToolkit.WPF.Controls.Dashboard.StatusStripFormat;

namespace LenovoLegionToolkit.Tests.UI;

public class StatusStripTests
{
    // ---- Missing readings: the sensor controller reports them as negative numbers (SensorData.Empty is all -1) ----

    [Fact]
    public void EmptySensorData_ShowsNothingMadeUp()
    {
        var empty = SensorData.Empty;
        Assert.False(IsAvailable(empty.Temperature));
        Assert.Null(Percent(empty.Utilization));
        Assert.Null(Gigahertz(empty.CoreClock));
        Assert.Null(Megahertz(empty.CoreClock));
        Assert.Null(Rpm(empty.FanSpeed));
        Assert.Null(Fraction(empty.Utilization, empty.MaxUtilization));
        Assert.Equal(Unavailable, JoinAvailable(Percent(empty.Utilization), Gigahertz(empty.CoreClock)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(55, true)]
    [InlineData(-1, false)]
    public void IsAvailable_TreatsNegativeAsMissing(int value, bool expected) => Assert.Equal(expected, IsAvailable(value));

    [Fact]
    public void ZeroClock_IsMissing_NotZeroGigahertz()
    {
        Assert.Null(Gigahertz(0));
        Assert.Null(Megahertz(0));
    }

    [Theory]
    [InlineData(78, false)]
    [InlineData(79, true)]
    [InlineData(-1, false)]
    public void IsHotCpu_AboveTheThreshold(int celsius, bool expected) => Assert.Equal(expected, IsHotCpu(celsius));

    // ---- Bars ----

    [Theory]
    [InlineData(50, 100, 0.5)]
    [InlineData(0, 100, 0.0)]
    [InlineData(120, 100, 1.0)]    // over the reported max is clamped, not drawn past the track
    [InlineData(3000, 6000, 0.5)]
    public void Fraction_IsValueOverMax_Clamped(int value, int max, double expected) => Assert.Equal(expected, Fraction(value, max)!.Value, 6);

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(50, 0)]
    [InlineData(50, -1)]
    public void Fraction_UnknownValueOrMax_IsNull(int value, int max) => Assert.Null(Fraction(value, max));

    // ---- Text ----

    [Fact]
    public void Temperature_ConvertsToFahrenheit()
    {
        Assert.Equal(62, ToDisplayTemperature(62, TemperatureUnit.C));
        Assert.Equal(143.6, ToDisplayTemperature(62, TemperatureUnit.F), 6);
        Assert.Equal(32, ToDisplayTemperature(0, TemperatureUnit.F));
        Assert.Equal(Resource.Celsius, TemperatureSuffix(TemperatureUnit.C));
        Assert.Equal(Resource.Fahrenheit, TemperatureSuffix(TemperatureUnit.F));
    }

    [Fact]
    public void Sublines_JoinOnlyTheReadingsThatExist()
    {
        WithCulture("en-US", () =>
        {
            Assert.Equal($"45{Resource.Percent} · 3.2 {Resource.GHz}", JoinAvailable(Percent(45), Gigahertz(3249)));
            Assert.Equal($"3.2 {Resource.GHz}", JoinAvailable(Percent(-1), Gigahertz(3249)));
            Assert.Equal($"12{Resource.Percent}", JoinAvailable(Percent(12), Megahertz(-1)));
            Assert.Equal($"1500 {Resource.MHz}", Megahertz(1500));
            Assert.Equal($"2100 {Resource.RPM}", Rpm(2100));
        });
    }

    [Theory]
    [InlineData(PowerAdapterStatus.Connected)]
    [InlineData(PowerAdapterStatus.ConnectedLowWattage)]
    [InlineData(PowerAdapterStatus.Disconnected)]
    public void AdapterText_HasDistinctWordingPerState(PowerAdapterStatus status)
    {
        var text = AdapterText(status);
        Assert.False(string.IsNullOrWhiteSpace(text));
        foreach (var other in new[] { PowerAdapterStatus.Connected, PowerAdapterStatus.ConnectedLowWattage, PowerAdapterStatus.Disconnected })
            if (other != status)
                Assert.NotEqual(text, AdapterText(other));
    }

    // ---- Mode colors ----

    [Theory]
    [InlineData(PowerModeState.Quiet, SkinKeys.ModeQuietBrush)]
    [InlineData(PowerModeState.Balance, SkinKeys.ModeBalanceBrush)]
    [InlineData(PowerModeState.Performance, SkinKeys.ModePerformanceBrush)]
    [InlineData(PowerModeState.GodMode, SkinKeys.ModeCustomBrush)]
    [InlineData(PowerModeState.Extreme, SkinKeys.ModeCustomBrush)]
    public void ModeBrushKey_MapsEveryMode(PowerModeState state, string key) => Assert.Equal(key, ModeBrushKey(state));

    // ---- Selector pill: modes depend on the model, so the count varies ----

    [Theory]
    [InlineData(0, 4, 400.0, 0.0, 100.0)]
    [InlineData(3, 4, 400.0, 300.0, 100.0)]
    [InlineData(1, 3, 300.0, 100.0, 100.0)]   // a model without Custom mode
    [InlineData(0, 1, 250.0, 0.0, 250.0)]
    public void PillGeometry_SitsUnderTheSegment(int index, int count, double width, double x, double w)
    {
        var geometry = PillGeometry(index, count, width);
        Assert.NotNull(geometry);
        Assert.Equal(x, geometry!.Value.X, 6);
        Assert.Equal(w, geometry.Value.Width, 6);
    }

    [Theory]
    [InlineData(-1, 4, 400.0)]   // current mode isn't one of the segments
    [InlineData(4, 4, 400.0)]
    [InlineData(0, 0, 400.0)]
    [InlineData(0, 4, 0.0)]      // not laid out yet
    [InlineData(0, 4, double.NaN)]
    public void PillGeometry_HidesWhenItCantBePlaced(int index, int count, double width) => Assert.Null(PillGeometry(index, count, width));

    // ---- Rules shared by the power-mode card and the selector ----

    [Theory]
    [InlineData(PowerModeState.Performance, PowerAdapterStatus.Disconnected, true)]
    [InlineData(PowerModeState.GodMode, PowerAdapterStatus.Disconnected, true)]
    [InlineData(PowerModeState.Extreme, PowerAdapterStatus.ConnectedLowWattage, true)]
    [InlineData(PowerModeState.Performance, PowerAdapterStatus.Connected, false)]
    [InlineData(PowerModeState.Quiet, PowerAdapterStatus.Disconnected, false)]
    [InlineData(PowerModeState.Balance, PowerAdapterStatus.Disconnected, false)]
    public void AdapterWarning_OnlyForHighPowerModesOffAFullAdapter(PowerModeState state, PowerAdapterStatus status, bool expected) =>
        Assert.Equal(expected, PowerModeActions.ShowsAdapterWarning(state, status));

    [Theory]
    [InlineData(PowerModeState.Balance, true, false, PowerAdapterStatus.Connected, true)]
    [InlineData(PowerModeState.Balance, false, true, PowerAdapterStatus.Connected, false)]
    [InlineData(PowerModeState.GodMode, false, true, PowerAdapterStatus.ConnectedLowWattage, true)]
    [InlineData(PowerModeState.GodMode, true, false, PowerAdapterStatus.Connected, false)]
    [InlineData(PowerModeState.GodMode, true, true, PowerAdapterStatus.Disconnected, false)]
    [InlineData(PowerModeState.Quiet, true, true, PowerAdapterStatus.Connected, false)]
    [InlineData(PowerModeState.Performance, true, true, PowerAdapterStatus.Connected, false)]
    public void HasSettings_MatchesTheCardsRules(PowerModeState state, bool aiMode, bool godMode, PowerAdapterStatus status, bool expected) =>
        Assert.Equal(expected, PowerModeActions.HasSettings(state, aiMode, godMode, status));

    private static void WithCulture(string name, global::System.Action action)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
