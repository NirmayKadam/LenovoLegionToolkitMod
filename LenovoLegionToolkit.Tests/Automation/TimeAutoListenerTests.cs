using System;
using LenovoLegionToolkit.Lib.AutoListeners;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

public class TimeAutoListenerTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxCatchUp = TimeSpan.FromSeconds(5);

    [Fact]
    public void FirstTick_EmitsOnlyNow()
    {
        Assert.Equal([T0], TimeAutoListener.GetSecondsToEmit(null, T0, MaxCatchUp));
    }

    [Fact]
    public void NormalTick_EmitsNextSecond()
    {
        Assert.Equal([T0.AddSeconds(1)], TimeAutoListener.GetSecondsToEmit(T0, T0.AddSeconds(1), MaxCatchUp));
    }

    [Fact]
    public void SkippedSecond_IsCaughtUp()
    {
        var result = TimeAutoListener.GetSecondsToEmit(T0, T0.AddSeconds(2), MaxCatchUp);
        Assert.Equal([T0.AddSeconds(1), T0.AddSeconds(2)], result);
    }

    [Fact]
    public void SameSecondTwice_EmitsNothing()
    {
        Assert.Empty(TimeAutoListener.GetSecondsToEmit(T0, T0, MaxCatchUp));
    }

    [Fact]
    public void ClockGoesBackwards_EmitsNothing()
    {
        Assert.Empty(TimeAutoListener.GetSecondsToEmit(T0, T0.AddSeconds(-30), MaxCatchUp));
    }

    [Fact]
    public void GapAtLimit_IsCaughtUp()
    {
        Assert.Equal(5, TimeAutoListener.GetSecondsToEmit(T0, T0.AddSeconds(5), MaxCatchUp).Count);
    }

    [Fact]
    public void LargeGap_SleepResume_EmitsOnlyNow()
    {
        var now = T0.AddHours(3);
        Assert.Equal([now], TimeAutoListener.GetSecondsToEmit(T0, now, MaxCatchUp));
    }

    [Fact]
    public void TruncateToSecond_DropsSubSecondPart_KeepsKind()
    {
        var truncated = TimeAutoListener.TruncateToSecond(T0.AddMilliseconds(987));
        Assert.Equal(T0, truncated);
        Assert.Equal(DateTimeKind.Utc, truncated.Kind);
    }
}
