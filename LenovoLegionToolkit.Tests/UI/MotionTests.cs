using System.Globalization;
using System.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;
using TransitionType = Wpf.Ui.Animations.TransitionType;

namespace LenovoLegionToolkit.Tests.UI;

public class MotionTests
{
    private const AnimationLevel Full = AnimationLevel.Full;
    private const AnimationLevel Reduced = AnimationLevel.Reduced;
    private const AnimationLevel Off = AnimationLevel.Off;

    // ---- Level resolution: setting x Windows "Animation effects" x render tier x GPU rendering ----

    [Theory]
    // Everything allows it: the setting is used as is.
    [InlineData(Full, true, 2, true, Full, MotionLimit.None)]
    [InlineData(Reduced, true, 2, true, Reduced, MotionLimit.None)]
    [InlineData(Off, true, 2, true, Off, MotionLimit.None)]
    // Off always wins, whatever the system says.
    [InlineData(Off, false, 0, false, Off, MotionLimit.None)]
    [InlineData(Off, true, 0, true, Off, MotionLimit.None)]
    // Windows animation effects off: Full drops to Reduced and says why.
    [InlineData(Full, false, 2, true, Reduced, MotionLimit.WindowsAnimationsOff)]
    [InlineData(Full, false, 0, false, Reduced, MotionLimit.WindowsAnimationsOff)]
    [InlineData(Reduced, false, 2, true, Reduced, MotionLimit.None)]
    // App running in software rendering (hardware acceleration setting off).
    [InlineData(Full, true, 2, false, Reduced, MotionLimit.HardwareAccelerationOff)]
    [InlineData(Full, true, 0, false, Reduced, MotionLimit.HardwareAccelerationOff)]
    [InlineData(Reduced, true, 2, false, Reduced, MotionLimit.None)]
    // GPU can't do full hardware rendering (tier 0 = software, tier 1 = partial).
    [InlineData(Full, true, 1, true, Reduced, MotionLimit.NoHardwareRendering)]
    [InlineData(Full, true, 0, true, Reduced, MotionLimit.NoHardwareRendering)]
    [InlineData(Reduced, true, 1, true, Reduced, MotionLimit.None)]
    public void Resolve_FollowsTheRules(AnimationLevel setting, bool windowsAnimations, int tier, bool gpuRendering,
        AnimationLevel expectedLevel, MotionLimit expectedLimit)
    {
        var (level, limit) = Motion.Resolve(setting, windowsAnimations, tier, gpuRendering);
        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedLimit, limit);
    }

    [Fact]
    public void Resolve_NeverRaisesTheLevelAboveTheSetting()
    {
        foreach (var setting in new[] { Full, Reduced, Off })
        foreach (var windows in new[] { true, false })
        foreach (var tier in new[] { 0, 1, 2 })
        foreach (var gpu in new[] { true, false })
        {
            var (level, _) = Motion.Resolve(setting, windows, tier, gpu);
            // The enum runs Full(0) > Reduced(1) > Off(2) in amount of motion.
            Assert.True(level >= setting, $"{setting}/{windows}/{tier}/{gpu} gave {level}");
        }
    }

    // ---- Page transitions ----

    [Theory]
    [InlineData(Full, TransitionType.FadeInWithSlide, 220)]
    [InlineData(Reduced, TransitionType.FadeIn, 150)]
    [InlineData(Off, TransitionType.None, 0)]
    public void PageTransition_MatchesLevel(AnimationLevel level, TransitionType type, int duration)
    {
        Assert.Equal((type, duration), Motion.PageTransition(level));
    }

    // ---- Loops ----

    [Theory]
    [InlineData(Full, true, true)]
    [InlineData(Full, false, false)]
    [InlineData(Reduced, true, false)]
    [InlineData(Off, true, false)]
    public void ShouldRunLoops_OnlyAtFullAndOnScreen(AnimationLevel level, bool shown, bool expected) =>
        Assert.Equal(expected, Motion.ShouldRunLoops(level, shown));

    // ---- Number formatting used by the count-up ----

    [Theory]
    [InlineData(41.5, 0, "°C", "42°C")]   // half rounds away from zero, not to even
    [InlineData(42.5, 0, "°C", "43°C")]
    [InlineData(-2.5, 0, "", "-3")]
    [InlineData(0.4, 0, "", "0")]
    [InlineData(-0.4, 0, "", "0")]       // never "-0"
    [InlineData(-0.04, 1, "", "0.0")]
    [InlineData(3.14159, 2, " GHz", "3.14 GHz")]
    [InlineData(99.95, 1, "%", "100.0%")]
    [InlineData(1234.0, 0, " RPM", "1234 RPM")]
    public void FormatNumber_RoundsAndFormats(double value, int decimals, string suffix, string expected)
    {
        WithCulture("en-US", () => Assert.Equal(expected, Motion.FormatNumber(value, decimals, suffix)));
    }

    [Fact]
    public void FormatNumber_UsesTheCurrentCulture()
    {
        WithCulture("de-DE", () => Assert.Equal("3,5 GHz", Motion.FormatNumber(3.5, 1, " GHz")));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FormatNumber_NotANumber_ShowsDash(double value) => Assert.Equal("—", Motion.FormatNumber(value, 0, "°C"));

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(-0.2, 0)]
    [InlineData(1.7, 1)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    public void ClampFraction_StaysWithin0And1(double input, double expected) => Assert.Equal(expected, Motion.ClampFraction(input));

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
