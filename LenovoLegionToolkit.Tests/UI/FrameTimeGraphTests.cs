using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.WPF.Windows.Osd;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The OSD's frame-time graph: the history the FPS measurement feeds (clock supplied by the test) and where the graph
/// draws each frame. The frame times here are plain numbers; real ones come from PresentMon while a game runs.
/// </summary>
public class FrameTimeGraphTests
{
    private long _now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc).Ticks;

    private FrameTimeHistory History() => new(() => _now);

    private void Advance(double ms) => _now += TimeSpan.FromMilliseconds(ms).Ticks;

    // ---- History ----

    [Fact]
    public void History_KeepsOnlyTheLastFiveSeconds()
    {
        var history = History();
        history.Add(7);
        Advance(3000);
        history.Add(8);
        Advance(2500);   // the first frame is now 5.5 s old

        var (samples, _) = history.Snapshot();

        Assert.Equal([8.0], samples.Select(s => s.Milliseconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1500)]          // a pause, not a frame
    [InlineData(double.NaN)]
    public void History_IgnoresValuesThatArentFrameTimes(double ms)
    {
        var history = History();
        history.Add(ms);
        Assert.Empty(history.Snapshot().Samples);
    }

    [Fact]
    public void History_HoldsAtMostItsCapacity_DroppingTheOldest_InTimeOrder()
    {
        var history = History();
        for (var i = 1; i <= FrameTimeHistory.Capacity + 10; i++)
        {
            history.Add(i % 100 + 1);
            Advance(1);
        }

        var samples = history.Snapshot().Samples;

        Assert.True(samples.Count <= FrameTimeHistory.Capacity);
        Assert.True(samples.Zip(samples.Skip(1)).All(p => p.First.Ticks <= p.Second.Ticks));
    }

    [Fact]
    public void History_Clear_EmptiesIt()
    {
        var history = History();
        history.Add(7);
        history.Clear();
        Assert.Empty(history.Snapshot().Samples);
    }

    [Fact]
    public async Task History_AddingWhileReading_DoesNotFail()
    {
        var history = new FrameTimeHistory();
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 50_000; i++)
                history.Add(i % 30 + 1);
        });
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 2_000; i++)
                _ = history.Snapshot();
        });

        await Task.WhenAll(writer, reader);
    }

    /// <summary>Stopping the FPS measurement (game closed, OSD hidden) forgets the frames, so a new game starts clean.</summary>
    [Fact]
    public void FpsMeasurement_Stopping_ClearsTheFrames()
    {
        var controller = new FpsSensorController();
        controller.FrameTimes.Add(7);

        controller.StopMonitoring();

        Assert.Empty(controller.FrameTimes.Snapshot().Samples);
    }

    // ---- Layout ----

    private List<FrameTimeSample> Frames(params double[] ms)
    {
        var list = new List<FrameTimeSample>();
        foreach (var value in ms)
        {
            list.Add(new FrameTimeSample(_now, value));
            Advance(value);
        }
        return list;
    }

    [Fact]
    public void Layout_FewerThanTwoFrames_DrawsNothing()
    {
        Assert.Empty(FrameTimeGraphLayout.Compute([], _now, FrameTimeHistory.Window, 160, 40).Points);
        Assert.Empty(FrameTimeGraphLayout.Compute(Frames(7), _now, FrameTimeHistory.Window, 160, 40).Points);
    }

    [Fact]
    public void Layout_SmoothFrames_UseTheThirtyFpsFloor()
    {
        var layout = FrameTimeGraphLayout.Compute(Frames(6.9, 7.0, 6.8, 7.1), _now, FrameTimeHistory.Window, 160, 40);
        Assert.Equal(FrameTimeGraphLayout.MinScaleMs, layout.ScaleMs, 6);
        Assert.DoesNotContain(true, layout.Spikes);
    }

    [Fact]
    public void Layout_ScaleFollowsTheLongestFrame_UpToTheCap()
    {
        Assert.Equal(60, FrameTimeGraphLayout.Compute(Frames(7, 50, 7), _now, FrameTimeHistory.Window, 160, 40).ScaleMs, 6);

        var capped = FrameTimeGraphLayout.Compute(Frames(7, 400, 7), _now, FrameTimeHistory.Window, 160, 40);
        Assert.Equal(FrameTimeGraphLayout.MaxScaleMs, capped.ScaleMs, 6);
        Assert.Equal(0, capped.Points[1].Y, 6);   // a longer frame is drawn at the top, not outside
    }

    [Fact]
    public void Layout_FlagsFramesOverTwiceTheMedian_AsSpikes()
    {
        var layout = FrameTimeGraphLayout.Compute(Frames(7, 7, 7, 15, 7, 13.9), _now, FrameTimeHistory.Window, 160, 40);

        Assert.Equal(7, layout.MedianMs, 6);
        Assert.Equal([false, false, false, true, false, false], layout.Spikes);
    }

    [Fact]
    public void Layout_OldestLeft_NewestRight_InsideTheWidth()
    {
        var frames = Frames(10, 10, 10);
        var layout = FrameTimeGraphLayout.Compute(frames, _now, FrameTimeHistory.Window, 160, 40);

        Assert.True(layout.Points.Zip(layout.Points.Skip(1)).All(p => p.First.X < p.Second.X));
        Assert.All(layout.Points, p => Assert.InRange(p.X, 0, 160));
        Assert.All(layout.Points, p => Assert.InRange(p.Y, 0, 40));
    }
}
