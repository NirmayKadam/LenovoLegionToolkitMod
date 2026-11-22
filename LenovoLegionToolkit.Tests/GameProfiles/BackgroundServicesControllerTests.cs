using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameProfiles;

/// <summary>
/// Keeping chosen services stopped during normal use, against a scripted set of Windows services (every start and
/// stop is recorded) and real settings files in a temporary folder. What real Windows does with a stop is on the
/// laptop checklist.
/// </summary>
public sealed class BackgroundServicesControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-background-services-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>Scripted Windows services. Only services are used here; the app calls fail the test if reached.</summary>
    private sealed class ScriptedServices : IGameProfileSystem
    {
        public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Calls { get; } = [];
        public HashSet<string> WontStart { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string name, bool running = true, bool canStop = true, bool driver = false, bool dependents = false) =>
            Services[name] = new ServiceInfo(name, name + " service", driver, running, canStop, dependents);

        public bool IsRunning(string name) => Services[name].Running;

        public ServiceInfo? ReadService(string name) => Services.TryGetValue(name, out var s) ? s : null;

        public Task<bool> StopServiceAsync(string name, TimeSpan timeout)
        {
            Calls.Add($"stop {name}");
            Services[name] = Services[name] with { Running = false };
            return Task.FromResult(true);
        }

        public Task<bool> StartServiceAsync(string name, TimeSpan timeout)
        {
            Calls.Add($"start {name}");
            if (WontStart.Contains(name))
                return Task.FromResult(false);
            Services[name] = Services[name] with { Running = true };
            return Task.FromResult(true);
        }

        // Background services never change the power mode; a call here would be a bug.
        public Task<PowerModeState?> GetPowerModeAsync() => throw new NotSupportedException("Background services don't read the power mode.");

        public Task<bool> SetPowerModeAsync(PowerModeState mode) => throw new NotSupportedException("Background services don't change the power mode.");

        public IReadOnlyList<int> FindByName(string exeName) => throw new InvalidOperationException("Apps are not touched here.");
        public bool IsRunning(int processId, string reportedName) => throw new InvalidOperationException("Apps are not touched here.");
        public AppProcess? ReadApp(int processId) => throw new InvalidOperationException("Apps are not touched here.");
        public bool SetApp(int processId, DateTime startTimeUtc, ProcessPriorityClass priority, ThrottleState? throttle) => throw new InvalidOperationException("Apps are not touched here.");
        public Task<bool> CloseAppAsync(int processId, DateTime startTimeUtc, TimeSpan timeout) => throw new InvalidOperationException("Apps are not touched here.");
    }

    private (BackgroundServicesController Controller, BackgroundServicesSettings Settings, List<Exception> Reports) Create(ScriptedServices system, bool enabled, params string[] presets)
    {
        var settings = new BackgroundServicesSettings(_dir);
        settings.Store.Enabled = enabled;
        settings.Store.Presets = [.. presets];
        var reports = new List<Exception>();
        return (new BackgroundServicesController(settings, new BackgroundServicesSessionSettings(_dir), system, (_, ex) => reports.Add(ex)), settings, reports);
    }

    private List<string> Noted() => new BackgroundServicesSessionSettings(_dir).Store.StoppedServices;

    [Fact]
    public async Task ChosenServices_AreStopped_EachNotedOnDiskFirst()
    {
        var system = new ScriptedServices();
        system.Add("SysMain");
        var (controller, _, _) = Create(system, true, "SysMain");

        await controller.ApplyAsync();

        Assert.False(system.IsRunning("SysMain"));
        Assert.Equal(["SysMain"], Noted());
    }

    [Fact]
    public async Task WithTheSettingOff_NothingIsStopped()
    {
        var system = new ScriptedServices();
        system.Add("SysMain");

        await Create(system, false, "SysMain").Controller.ApplyAsync();

        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task ServicesThatMayNotStop_AreLeftRunning()
    {
        var system = new ScriptedServices();
        system.Add("DiagTrack", canStop: false);
        system.Add("dmwappushservice", dependents: true);
        system.Add("WerSvc", running: false);   // already stopped by Windows: not ours
        var (controller, _, _) = Create(system, true, "Telemetry");

        await controller.ApplyAsync();

        Assert.Empty(system.Calls);
        Assert.Empty(Noted());
    }

    [Fact]
    public async Task OnExit_OnlyWhatLltStopped_IsStartedAgain()
    {
        var system = new ScriptedServices();
        system.Add("SysMain");
        system.Add("WSearch", running: false);   // stopped by something else
        var (controller, settings, _) = Create(system, true, "SysMain", "WindowsSearch");

        await controller.ApplyAsync();
        system.Calls.Clear();
        await controller.RestoreAllAsync();

        Assert.Equal(["start SysMain"], system.Calls);
        Assert.False(system.IsRunning("WSearch"));
        Assert.Empty(Noted());
        Assert.True(settings.Store.Enabled);   // the choice stays for the next start
    }

    [Fact]
    public async Task UntickingAPreset_StartsItsServicesAgain()
    {
        var system = new ScriptedServices();
        system.Add("SysMain");
        system.Add("MapsBroker");
        var (controller, settings, _) = Create(system, true, "SysMain", "UnusedExtras");
        await controller.ApplyAsync();

        settings.Store.Presets = ["UnusedExtras"];
        await controller.ApplyAsync();

        Assert.True(system.IsRunning("SysMain"));
        Assert.False(system.IsRunning("MapsBroker"));
        Assert.Equal(["MapsBroker"], Noted());
    }

    [Fact]
    public async Task AServiceWindowsStartedAgain_IsShown_AndStoppedAgainOnApply()
    {
        var system = new ScriptedServices();
        system.Add("SysMain");
        var (controller, _, _) = Create(system, true, "SysMain");
        await controller.ApplyAsync();

        system.Services["SysMain"] = system.Services["SysMain"] with { Running = true };   // Windows started it
        Assert.Equal(BackgroundServiceState.RunningAgain, controller.GetStatus().Single().State);

        await controller.ApplyAsync();
        Assert.Equal(BackgroundServiceState.StoppedByLlt, controller.GetStatus().Single().State);
    }

    [Fact]
    public async Task AfterLltWasEnded_TheNextStartPutsBackWhatIsNoLongerWanted()
    {
        var system = new ScriptedServices();
        system.Add("SysMain");
        var (first, settings, _) = Create(system, true, "SysMain");
        await first.ApplyAsync();   // LLT is then ended without putting anything back

        settings.Store.Enabled = false;
        settings.SynchronizeStore();
        var (next, _, _) = Create(system, false, "SysMain");
        await next.ApplyAsync();

        Assert.True(system.IsRunning("SysMain"));
        Assert.Empty(Noted());
    }

    [Fact]
    public async Task AServiceThatWontStartAgain_StaysNoted_AndIsReported()
    {
        var system = new ScriptedServices();
        system.Add("SysMain");
        system.WontStart.Add("SysMain");
        var (controller, _, reports) = Create(system, true, "SysMain");
        await controller.ApplyAsync();

        await controller.RestoreAllAsync();

        Assert.Equal(["SysMain"], Noted());   // tried again at the next apply or start
        Assert.Single(reports);
    }

    [Fact]
    public async Task AServiceAGameProfilePaused_IsNotTakenOver()
    {
        // A game session stopped SysMain first; this controller sees it stopped and leaves it to the game session.
        var system = new ScriptedServices();
        system.Add("SysMain", running: false);
        var (controller, _, _) = Create(system, true, "SysMain");

        await controller.ApplyAsync();
        await controller.RestoreAllAsync();

        Assert.Empty(system.Calls);
        Assert.Equal(BackgroundServiceState.StoppedAlready, controller.GetStatus().Single().State);
    }

    [Fact]
    public void Status_ShowsRefusals_AndMissingServices()
    {
        var system = new ScriptedServices();
        system.Add("DiagTrack", canStop: false);   // dmwappushservice and WerSvc not installed
        var (controller, _, _) = Create(system, true, "Telemetry");

        var status = controller.GetStatus().ToDictionary(s => s.Name);

        Assert.Equal(BackgroundServiceState.Refused, status["DiagTrack"].State);
        Assert.Equal(ServiceRefusal.CannotStop, status["DiagTrack"].Refusal);
        Assert.Equal(BackgroundServiceState.NotInstalled, status["WerSvc"].State);
    }
}
