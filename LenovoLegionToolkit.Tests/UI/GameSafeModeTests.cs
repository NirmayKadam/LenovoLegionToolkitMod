using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Macro;
using LenovoLegionToolkit.Lib.Macro.Utils;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Osd;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

public class GameSafeModeTests
{
    // ---- Detecting a protected game (from service status only) ----

    [Fact]
    public void WatchesVanguardEasyAntiCheatAndBattlEye() =>
        Assert.Equal(["vgc", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService"], AntiCheatMonitor.WatchedServices);

    [Fact]
    public void Poll_RaisesOnlyOnChanges_AndReportsWhichServiceIsRunning()
    {
        var running = new HashSet<string>();
        var monitor = new AntiCheatMonitor(running.Contains);
        var events = new List<bool>();
        monitor.ProtectedGameChanged += (_, active) => events.Add(active);

        Assert.False(monitor.Poll());          // nothing running: no event
        running.Add("vgc");
        Assert.True(monitor.Poll());           // Valorant starts
        Assert.True(monitor.Poll());           // still running: no second event
        Assert.Equal(["vgc"], monitor.ActiveServices);
        running.Clear();
        Assert.False(monitor.Poll());          // game closed

        Assert.Equal([true, false], events);
        Assert.Empty(monitor.ActiveServices);
    }

    [Fact]
    public void Poll_ServiceQueryErrors_CountAsNotRunning()
    {
        var monitor = new AntiCheatMonitor(_ => throw new global::System.InvalidOperationException("no access"));
        Assert.False(monitor.Poll());
    }

    [Fact]
    public void Poll_AnyOneServiceIsEnough()
    {
        var monitor = new AntiCheatMonitor(name => name == "EasyAntiCheat_EOS");
        Assert.True(monitor.Poll());
        Assert.Equal(["EasyAntiCheat_EOS"], monitor.ActiveServices);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]   // user turned it off
    [InlineData(false, false, false)]
    public void ShouldBeActive_NeedsTheSettingAndAProtectedGame(bool setting, bool game, bool expected) =>
        Assert.Equal(expected, GameSafeMode.ShouldBeActive(setting, game));

    // ---- Switching on and off ----

    /// <summary>
    /// The 5-second service check and the Settings toggle can both ask game-safe mode to switch at the same time.
    /// The pausing/resuming is supplied by the test and records what it is asked to do; it takes a moment, like
    /// the real one (it waits for the UI thread). Switches must never overlap, must alternate on/off, and must end
    /// in the state the setting and the game call for.
    /// </summary>
    [Fact]
    public async Task Switching_FromSeveralPlacesAtOnce_NeverOverlaps_AndEndsInTheRightState()
    {
        var gameRunning = false;
        var settingOn = true;
        var monitor = new AntiCheatMonitor(_ => gameRunning);

        var running = 0;
        var maxRunning = 0;
        var switches = new List<bool>();
        var mode = new GameSafeMode(monitor, () => settingOn, async active =>
        {
            var now = Interlocked.Increment(ref running);
            lock (switches)
            {
                maxRunning = Math.Max(maxRunning, now);
                switches.Add(active);
            }
            await Task.Delay(5);
            Interlocked.Decrement(ref running);
        });

        // Each call sets its own inputs first, so the calls really disagree and race each other.
        var calls = new List<Task>();
        for (var i = 0; i < 40; i++)
        {
            var game = i % 2 == 0;
            var setting = i % 3 != 0;
            calls.Add(Task.Run(() => { gameRunning = game; monitor.Poll(); }));
            calls.Add(Task.Run(() => { settingOn = setting; return mode.RefreshAsync(); }));
        }
        await Task.WhenAll(calls);

        // Then two known phases, each hammered from several places at once: game on, then game off.
        foreach (var game in new[] { true, false })
        {
            gameRunning = game;
            settingOn = true;
            var phase = new List<Task>();
            for (var i = 0; i < 20; i++)
            {
                phase.Add(Task.Run(() => monitor.Poll()));
                phase.Add(Task.Run(mode.RefreshAsync));
            }
            await Task.WhenAll(phase);
            await mode.RefreshAsync();
            Assert.Equal(game, mode.IsActive);
        }

        Assert.True(switches.Count >= 2, $"only {switches.Count} switch(es): the test didn't exercise switching");
        Assert.Equal(1, maxRunning);
        for (var i = 1; i < switches.Count; i++)
            Assert.NotEqual(switches[i - 1], switches[i]);
        Assert.Equal(GameSafeMode.ShouldBeActive(settingOn, monitor.IsProtectedGameRunning), mode.IsActive);
    }

    // ---- Macros pause ----

    [Fact]
    public void Macros_SuspendAndResume_TrackTheirState()
    {
        var controller = new MacroController(new MacroSettings());
        var changes = 0;
        controller.StateChanged += (_, _) => changes++;

        controller.Suspend();
        controller.Suspend(); // idempotent
        Assert.True(controller.IsSuspended);

        controller.Resume();
        Assert.False(controller.IsSuspended);
        Assert.Equal(2, changes);
    }

    /// <summary>
    /// Windows delivers a low-level keyboard hook's events only to the thread that installed it, and only while
    /// that thread runs a message loop. Game-safe mode resumes macros from a background timer thread, so the hook
    /// must still be installed on the thread that started macros (the UI thread), not on the timer's thread.
    /// This installs a real hook for a moment; macros are off in a fresh settings object, so key presses pass straight through.
    /// </summary>
    [Fact]
    public void Macros_ResumedFromABackgroundThread_HookIsInstalledOnTheStartingThread() => StaRunner.Run(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var uiThread = Environment.CurrentManagedThreadId;
        var installedOn = new List<int>();

        var controller = new MacroController(new MacroSettings()) { HookInstalledOnThread = installedOn.Add };
        try
        {
            controller.Start();
            controller.Suspend();

            // Resume from a thread-pool thread, the way game-safe mode's timer does, while this thread pumps messages.
            var resume = Task.Run(controller.Resume);
            var frame = new DispatcherFrame();
            resume.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            resume.GetAwaiter().GetResult();

            Assert.Equal([uiThread, uiThread], installedOn);
        }
        finally
        {
            controller.Suspend();
        }
    });

    // ---- OSD readings without the driver ----

    private static SensorsData Lenovo(int cpuTemp = 71, int cpuUse = 40, int cpuClock = 3800, int cpuFan = 3100, int gpuTemp = 65, int gpuUse = 90, int gpuClock = 2100, int gpuFan = 3300) =>
        new(new SensorData(cpuUse, 100, cpuClock, 5000, -1, -1, cpuTemp, 100, cpuFan, 5000),
            new SensorData(gpuUse, 100, gpuClock, 2500, 8000, 9000, gpuTemp, 87, gpuFan, 5000),
            new SensorData(-1, -1, -1, -1, -1, -1, 50, 100, 2000, 4000));

    [Fact]
    public void DriverFree_UsesLenovoSensorsAndWindowsMemory()
    {
        var s = OsdSnapshotBuilder.Build(null, Lenovo(), (55, 8.8, 16), showAverageFrequency: false);

        Assert.Equal(71, s.CpuTemp);
        Assert.Equal(40, s.CpuUsage);
        Assert.Equal(3800, s.CpuFrequency);
        Assert.Equal(3100, s.CpuFanSpeed);
        Assert.Equal(65, s.GpuTemp);
        Assert.Equal(90, s.GpuUsage);
        Assert.Equal(2100, s.GpuFrequency);
        Assert.Equal(55, s.MemUsage);
        Assert.Equal(8.8, s.MemUsed);
        Assert.Equal(16, s.MemTotal);
        Assert.Equal(50, s.PchTemp);
    }

    [Fact]
    public void DriverFree_DriverOnlyReadings_AreMissingNotGuessed()
    {
        var s = OsdSnapshotBuilder.Build(null, Lenovo(), (55, 8.8, 16), false);

        foreach (var value in new[] { s.CpuPower, s.CpuVoltage, s.CpuPClock, s.CpuEClock, s.GpuPower, s.GpuVramUsage, s.GpuVramUsed, s.GpuVramTotal, s.GpuVramTemp, s.MemTemp, s.Disk1Temp, s.Disk2Temp })
            Assert.Equal(-1, value);
    }

    [Fact]
    public void DriverFree_ZeroClockIsMissing_AndNoSourcesMeansAllMissing()
    {
        Assert.Equal(-1, OsdSnapshotBuilder.Build(null, Lenovo(cpuClock: 0, gpuClock: 0), null, false).CpuFrequency);

        var empty = OsdSnapshotBuilder.Build(null, null, null, false);
        Assert.Equal(-1, empty.CpuTemp);
        Assert.Equal(-1, empty.GpuUsage);
        Assert.Equal(-1, empty.MemUsage);
        Assert.Equal(-1, empty.CpuFanSpeed);
    }

    [Fact]
    public void Detailed_UsesLibreHardwareMonitor_WithFansFromLenovo()
    {
        var detailed = new HardwareSensorSnapshot
        {
            [SensorItem.CpuTemperature] = 80,
            [SensorItem.CpuPower] = 45.5f,
            [SensorItem.CpuMaxFrequency] = 4900,
            [SensorItem.CpuAverageFrequency] = 4100,
            [SensorItem.GpuVramUsed] = 5.5f,
        };

        var max = OsdSnapshotBuilder.Build(detailed, Lenovo(cpuTemp: 71), null, showAverageFrequency: false);
        Assert.Equal(80, max.CpuTemp);                // LHM reading, not Lenovo's 71
        Assert.Equal(45.5, max.CpuPower, 3);
        Assert.Equal(4900, max.CpuFrequency);
        Assert.Equal(5.5, max.GpuVramUsed, 3);
        Assert.Equal(3100, max.CpuFanSpeed);          // fans always come from Lenovo's sensors

        Assert.Equal(4100, OsdSnapshotBuilder.Build(detailed, Lenovo(), null, showAverageFrequency: true).CpuFrequency);
    }

    [Fact]
    public void Memory_FromWindowsByteCounts()
    {
        var gib = 1024UL * 1024 * 1024;
        var (load, used, total) = SystemMemory.FromBytes(75, 16 * gib, 4 * gib);
        Assert.Equal(75, load);
        Assert.Equal(12, used, 6);
        Assert.Equal(16, total, 6);

        Assert.Equal(0, SystemMemory.FromBytes(10, 4 * gib, 8 * gib).UsedGb); // never negative
    }

    [Fact]
    public void Memory_ThisPcAnswers()
    {
        var status = SystemMemory.GetStatus();
        Assert.NotNull(status);
        Assert.True(status!.Value.TotalGb > 0.5);
        Assert.InRange(status.Value.LoadPercent, 0, 100);
    }
}
