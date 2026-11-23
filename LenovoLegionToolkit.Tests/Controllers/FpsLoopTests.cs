using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// The FPS counter's once-a-second loop. Hiding and showing the OSD quickly (a style change, locking and unlocking
/// Windows) stops and starts it; a loop stopped mid-pass used to pick up the new loop's token and keep running, so two
/// loops fought over which game was measured. The real controller runs here; it may try to measure whatever window is
/// in front, which is harmless.
/// <para>
/// The old fault needed a stop to land in the few milliseconds of a pass, so this test guards the fix but may not catch
/// the old code every time.
/// </para>
/// </summary>
public class FpsLoopTests
{
    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > limit)
                return false;
            await Task.Delay(20);
        }
        return true;
    }

    [Fact]
    public async Task AfterQuickStopsAndStarts_OneLoopIsLeft_AndNoneAfterTheLastStop()
    {
        var fps = new FpsSensorController();
        try
        {
            for (var i = 0; i < 20; i++)
            {
                await fps.StartMonitoringAsync();
                await Task.Delay(i % 5 * 7);
                fps.StopMonitoring();
            }

            await fps.StartMonitoringAsync();
            Assert.True(await WaitForAsync(() => fps.RunningLoops == 1, TimeSpan.FromSeconds(3)), $"loops running: {fps.RunningLoops}");

            fps.StopMonitoring();
            Assert.True(await WaitForAsync(() => fps.RunningLoops == 0, TimeSpan.FromSeconds(3)), $"loops still running after stop: {fps.RunningLoops}");
        }
        finally
        {
            fps.Dispose();
        }
    }

    [Fact]
    public async Task StartingTwice_RunsOneLoop()
    {
        var fps = new FpsSensorController();
        try
        {
            await fps.StartMonitoringAsync();
            await fps.StartMonitoringAsync();

            Assert.True(await WaitForAsync(() => fps.RunningLoops == 1, TimeSpan.FromSeconds(3)));
            await Task.Delay(300);
            Assert.Equal(1, fps.RunningLoops);
        }
        finally
        {
            fps.Dispose();
        }
    }
}
