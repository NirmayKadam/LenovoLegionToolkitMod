using System;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// When the window counts as not answering (the freeze watchdog ticks every 2 s). Times in milliseconds of a steady
/// clock, stepped by the test.
/// </summary>
public class FreezeDetectorTests
{
    private static FreezeDetector Create() => new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(10));

    [Fact]
    public void AWindowThatAnswers_IsNeverFrozen()
    {
        var detector = Create();
        for (long t = 0; t <= 120_000; t += 2_000)
        {
            Assert.Null(detector.OnTick(t));
            Assert.Null(detector.OnAnswer(t + 5));
        }
        Assert.False(detector.IsFrozen);
    }

    [Fact]
    public void FifteenSecondsOfSilence_IsReportedOnce()
    {
        var detector = Create();
        detector.OnTick(0);
        detector.OnAnswer(10);

        var reports = Enumerable.Range(1, 20).Select(i => detector.OnTick(i * 2_000L)).Where(r => r is not null).ToList();

        var silent = Assert.Single(reports);
        Assert.Equal(TimeSpan.FromMilliseconds(15_990), silent);   // first tick at least 15 s after the last answer (16 s - 10 ms)
        Assert.True(detector.IsFrozen);
    }

    [Fact]
    public void TheNextAnswer_EndsTheFreeze_WithHowLongItLasted()
    {
        var detector = Create();
        detector.OnTick(0);
        detector.OnAnswer(0);
        for (long t = 2_000; t <= 20_000; t += 2_000)
            detector.OnTick(t);

        Assert.Equal(TimeSpan.FromSeconds(21), detector.OnAnswer(21_000));
        Assert.False(detector.IsFrozen);
        Assert.Null(detector.OnAnswer(22_000));
    }

    [Fact]
    public void SleepOrAPausedProcess_IsNotAFreeze()
    {
        var detector = Create();
        detector.OnTick(0);
        detector.OnAnswer(0);
        detector.OnTick(2_000);

        // The laptop slept for an hour between two ticks: no answer could come, but the window isn't frozen.
        Assert.Null(detector.OnTick(3_602_000));
        Assert.Null(detector.OnTick(3_604_000));
        Assert.False(detector.IsFrozen);
    }
}
