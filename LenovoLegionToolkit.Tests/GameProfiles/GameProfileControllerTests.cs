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
/// Game profiles act on other software, so these tests check the controller's decisions against a scripted Windows
/// (<see cref="ScriptedSystem"/>): processes and services are plain records, and every call is recorded. What the
/// real Windows does with a stop, a close or efficiency mode is on the laptop checklist.
/// </summary>
public sealed class GameProfileControllerTests : IDisposable
{
    private const int GamePid = 4000;
    private static readonly DateTime Started = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string UserApp = @"C:\Users\me\AppData\Local\App\app.exe";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-gameprofile-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>A scripted Windows: running processes and services, and a record of every call.</summary>
    private sealed class ScriptedSystem : IGameProfileSystem
    {
        public Dictionary<int, (string Name, AppProcess App)> Processes { get; } = [];
        public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every process id LLT asked about or changed (the game's must never appear).</summary>
        public List<int> TouchedPids { get; } = [];

        public List<string> Calls { get; } = [];

        public bool StopFails { get; set; }
        public HashSet<int> WontClose { get; } = [];

        public void AddApp(int pid, string name, ProcessPriorityClass priority = ProcessPriorityClass.Normal, ThrottleState? throttle = null, string? path = null, int session = 1) =>
            Processes[pid] = (name, new AppProcess(pid, Started, path ?? UserApp, session, priority, throttle ?? new ThrottleState(0, 0)));

        public void AddService(string name, bool running = true, bool canStop = true, bool driver = false, bool dependents = false) =>
            Services[name] = new ServiceInfo(name, name, driver, running, canStop, dependents);

        public IReadOnlyList<int> FindByName(string exeName) =>
            Processes.Where(p => string.Equals(p.Value.Name, exeName, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList();

        /// <summary>Game processes that have ended, whether or not their end was reported to the controller.</summary>
        public HashSet<int> Ended { get; } = [];

        public bool IsRunning(int processId, string reportedName) => !Ended.Contains(processId);

        public AppProcess? ReadApp(int processId)
        {
            TouchedPids.Add(processId);
            return Processes.TryGetValue(processId, out var p) ? p.App : null;
        }

        public bool SetApp(int processId, DateTime startTimeUtc, ProcessPriorityClass priority, ThrottleState? throttle)
        {
            TouchedPids.Add(processId);
            if (!Processes.TryGetValue(processId, out var p) || p.App.StartTimeUtc != startTimeUtc)
                return false;

            Calls.Add($"set {p.Name} {priority} {(throttle is { } t ? t.IsEfficient ? "eco" : $"throttle {t.ControlMask}/{t.StateMask}" : "-")}");
            Processes[processId] = (p.Name, p.App with { Priority = priority, Throttle = throttle ?? p.App.Throttle });
            return true;
        }

        /// <summary>Background helpers of an app: they have no window, so nothing can be asked to close.</summary>
        public HashSet<int> Windowless { get; } = [];

        public Task<CloseOutcome> CloseAppAsync(int processId, DateTime startTimeUtc, TimeSpan timeout)
        {
            TouchedPids.Add(processId);
            if (!Processes.TryGetValue(processId, out var p) || Windowless.Contains(processId))
                return Task.FromResult(CloseOutcome.NoWindow);

            if (WontClose.Contains(processId))
                return Task.FromResult(CloseOutcome.LeftOpen);

            Calls.Add($"close {p.Name}");
            Processes.Remove(processId);
            return Task.FromResult(CloseOutcome.Closed);
        }

        public ServiceInfo? ReadService(string name) => Services.TryGetValue(name, out var s) ? s : null;

        public Task<bool> StopServiceAsync(string name, TimeSpan timeout)
        {
            Calls.Add($"stop {name}");
            if (StopFails)
                return Task.FromResult(false);
            Services[name] = Services[name] with { Running = false };
            return Task.FromResult(true);
        }

        public Task<bool> StartServiceAsync(string name, TimeSpan timeout)
        {
            Calls.Add($"start {name}");
            Services[name] = Services[name] with { Running = true };
            return Task.FromResult(true);
        }

        /// <summary>The laptop's power mode; null when it can't be read.</summary>
        public PowerModeState? PowerMode { get; set; } = PowerModeState.Balance;

        /// <summary>Power modes the laptop refuses (for example the high-power ones on battery).</summary>
        public HashSet<PowerModeState> RefusedModes { get; } = [];

        public Task<PowerModeState?> GetPowerModeAsync() => Task.FromResult(PowerMode);

        public Task<bool> SetPowerModeAsync(PowerModeState mode)
        {
            Calls.Add($"power {mode}");
            if (RefusedModes.Contains(mode))
                return Task.FromResult(false);
            PowerMode = mode;
            return Task.FromResult(true);
        }
    }

    private GameProfile Profile(Action<GameProfile>? configure = null)
    {
        var profile = new GameProfile { Name = "My Game", ExeName = "game", Enabled = true, ChangesConfirmed = true };
        configure?.Invoke(profile);
        return profile;
    }

    private (GameProfileController Controller, GameProfileSessionSettings Session, List<Exception> Reports) Create(ScriptedSystem system, params GameProfile[] profiles)
    {
        var settings = new GameProfileSettings(_dir);
        settings.Store.Profiles = [.. profiles];
        var session = new GameProfileSessionSettings(_dir);
        var reports = new List<Exception>();
        return (new GameProfileController(settings, session, system, (_, ex) => reports.Add(ex)), session, reports);
    }

    /// <summary>A controller with Game mode set up by <paramref name="gameMode"/> and Windows' known games scripted.</summary>
    private (GameProfileController Controller, GameProfileSessionSettings Session) CreateWithGameMode(ScriptedSystem system, Action<GameProfile> gameMode, string[] knownGames, params GameProfile[] profiles)
    {
        var settings = new GameProfileSettings(_dir);
        settings.Store.Profiles = [.. profiles];
        var mode = GameProfile.NewGameMode();
        mode.Enabled = true;
        mode.ChangesConfirmed = true;
        gameMode(mode);
        settings.Store.GameMode = mode;
        var session = new GameProfileSessionSettings(_dir);
        var controller = new GameProfileController(settings, session, system, (_, ex) => throw ex, () => knownGames);
        return (controller, session);
    }

    // ---- Game mode (any game) ----

    private const string Valorant = "VALORANT-Win64-Shipping";
    private const string ValorantAsReported = "VALORANT-Win64-";   // Windows' 15-character name

    [Fact]
    public async Task GameMode_AppliesToAGameWindowsKnows_ThenPutsEverythingBack()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "browser");
        system.AddService("SysMain");
        var (controller, _) = CreateWithGameMode(system, m => { m.EfficiencyApps = ["browser"]; m.PauseServices = ["SysMain"]; }, [Valorant]);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync(ValorantAsReported, GamePid);

        Assert.Equal(["set browser BelowNormal eco", "stop SysMain"], system.Calls);
        Assert.Equal($"Game mode: {ValorantAsReported}", controller.ActiveGameName);

        system.Calls.Clear();
        await controller.ProcessStoppedAsync(ValorantAsReported, GamePid);

        Assert.Equal(["start SysMain", "set browser Normal throttle 0/0"], system.Calls);
        Assert.Null(controller.ActiveGameName);
    }

    [Fact]
    public async Task GameMode_IgnoresProgramsThatArentKnownGames_LikeABrowserOnTheGpu()
    {
        var system = new ScriptedSystem();
        system.AddService("SysMain");
        var (controller, _) = CreateWithGameMode(system, m => m.PauseServices = ["SysMain"], [Valorant]);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync("chrome", 6000);
        await controller.ProcessStartedAsync("Discord", 6001);

        Assert.Empty(system.Calls);
        Assert.Null(controller.ActiveGameName);
    }

    [Fact]
    public async Task GameMode_CountsTheProgramThatTurnedWindowsGameModeOn_ButNeverWindowsOwnApps()
    {
        var system = new ScriptedSystem();
        system.AddService("SysMain");
        var (controller, _) = CreateWithGameMode(system, m => m.PauseServices = ["SysMain"], []);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync("explorer", 50, fromWindowsGameMode: true);
        Assert.Empty(system.Calls);

        await controller.ProcessStartedAsync("IndieGame", 7000, fromWindowsGameMode: true);
        Assert.Equal(["stop SysMain"], system.Calls);
    }

    [Fact]
    public async Task AGameWithItsOwnProfile_UsesThatProfile_NotGameMode()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "browser");
        system.AddApp(101, "chat");
        var (controller, _) = CreateWithGameMode(system, m => m.EfficiencyApps = ["chat"], ["game"], Profile(p => p.EfficiencyApps = ["browser"]));
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Equal(["set browser BelowNormal eco"], system.Calls);
        Assert.Equal("My Game", controller.ActiveGameName);
    }

    [Fact]
    public async Task GameMode_NeverTouchesTheGame_EvenWhenItIsListed()
    {
        var system = new ScriptedSystem();
        system.AddApp(GamePid, Valorant);
        var (controller, _) = CreateWithGameMode(system, m => { m.EfficiencyApps = [Valorant]; m.CloseApps = [Valorant]; }, [Valorant]);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync(ValorantAsReported, GamePid);
        await controller.ProcessStoppedAsync(ValorantAsReported, GamePid);

        Assert.DoesNotContain(GamePid, system.TouchedPids);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task GameMode_NeverTouchesAnotherProcessOfTheGame_ThatWasntSeenStarting()
    {
        // The game runs a second process with the same name; only the first one's start was seen.
        var system = new ScriptedSystem();
        system.AddApp(GamePid, Valorant);
        system.AddApp(GamePid + 50, Valorant);
        var (controller, _) = CreateWithGameMode(system, m => m.EfficiencyApps = [Valorant], []);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync(ValorantAsReported, GamePid, fromWindowsGameMode: true);

        Assert.DoesNotContain(GamePid, system.TouchedPids);
        Assert.DoesNotContain(GamePid + 50, system.TouchedPids);
    }

    [Fact]
    public async Task AProgramEasedBeforeItWasRecognisedAsAGame_IsPutBackAtOnce()
    {
        // Another game started Game mode first; Valorant was running but not yet recognised, so it was eased.
        var system = new ScriptedSystem();
        system.AddApp(GamePid, Valorant);
        var (controller, session) = CreateWithGameMode(system, m => m.EfficiencyApps = [Valorant], []);
        await controller.RefreshAsync();
        await controller.ProcessStartedAsync("IndieGame", 7000, fromWindowsGameMode: true);
        Assert.Equal(ProcessPriorityClass.BelowNormal, system.Processes[GamePid].App.Priority);

        await controller.ProcessStartedAsync(ValorantAsReported, GamePid, fromWindowsGameMode: true);

        Assert.Equal(ProcessPriorityClass.Normal, system.Processes[GamePid].App.Priority);
        Assert.Empty(session.Store.EasedApps);   // nothing left to put back at the end
    }

    [Fact]
    public async Task GameMode_WithTwoGames_PutsBackOnlyAfterTheLastEnds()
    {
        var system = new ScriptedSystem();
        system.AddService("SysMain");
        var (controller, _) = CreateWithGameMode(system, m => m.PauseServices = ["SysMain"], [Valorant, "cs2"]);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync(ValorantAsReported, GamePid);
        await controller.ProcessStartedAsync("cs2", GamePid + 1);
        await controller.ProcessStoppedAsync(ValorantAsReported, GamePid);

        Assert.DoesNotContain("start SysMain", system.Calls);   // cs2 still runs; Game mode stays

        await controller.ProcessStoppedAsync("cs2", GamePid + 1);
        Assert.Contains("start SysMain", system.Calls);
    }

    [Fact]
    public async Task AGameAlreadyRunning_IsFoundWhenGameModeTurnsOn()
    {
        var system = new ScriptedSystem();
        system.AddApp(GamePid, Valorant);
        system.AddService("SysMain");
        var (controller, _) = CreateWithGameMode(system, m => m.PauseServices = ["SysMain"], [Valorant]);

        await controller.RefreshAsync();

        Assert.Equal(["stop SysMain"], system.Calls);
    }

    [Fact]
    public async Task AfterLltWasEndedDuringGameMode_TheNextStartPutsEverythingBack()
    {
        var system = new ScriptedSystem();
        system.AddService("SysMain");
        var (first, _) = CreateWithGameMode(system, m => m.PauseServices = ["SysMain"], [Valorant]);
        await first.RefreshAsync();
        await first.ProcessStartedAsync(ValorantAsReported, GamePid);   // then LLT is ended mid-game

        var (next, session) = CreateWithGameMode(system, m => m.PauseServices = ["SysMain"], [Valorant]);
        await next.RecoverAsync();

        Assert.Contains("start SysMain", system.Calls);
        Assert.Null(session.Store.ProfileId);
    }

    [Fact]
    public async Task GameModeOff_DoesNothing()
    {
        var system = new ScriptedSystem();
        system.AddService("SysMain");
        var (controller, _) = CreateWithGameMode(system, m => { m.Enabled = false; m.PauseServices = ["SysMain"]; }, [Valorant]);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync(ValorantAsReported, GamePid);
        await controller.ProcessStartedAsync("IndieGame", 7000, fromWindowsGameMode: true);

        Assert.Empty(system.Calls);
    }

    [Fact]
    public void GameMode_IsOffInSettingsFromBeforeItExisted()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "game_profiles.json"), """{ "Profiles": [] }""");

        var gameMode = new GameProfileSettings(_dir).Store.GameMode;

        Assert.False(gameMode.Enabled);
        Assert.True(gameMode.IsGameMode);
    }

    // ---- Recognising the game ----

    [Theory]
    [InlineData("game", "game", true)]
    [InlineData("GAME", "game", true)]
    [InlineData("game2", "game", false)]
    [InlineData("VALORANT-Win64-", "VALORANT-Win64-Shipping", true)]   // the process trace's 15-character name
    [InlineData("VALORANT-Win64", "VALORANT-Win64-Shipping", false)]
    [InlineData("cs2", "cs2", true)]
    public void IsGame_MatchesFullAndCutNames(string reported, string exeName, bool expected) =>
        Assert.Equal(expected, GameProfileController.IsGame(reported, exeName));

    // ---- Applying and putting back ----

    [Fact]
    public async Task GameStartAndEnd_EasesClosesPauses_ThenPutsBackExactlyWhatChanged()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "browser");
        system.AddApp(101, "chat");
        system.AddService("InventorySvc");
        var (controller, session, reports) = Create(system, Profile(p =>
        {
            p.EfficiencyApps = ["browser"];
            p.CloseApps = ["chat"];
            p.PauseServices = ["InventorySvc"];
        }));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Equal(["set browser BelowNormal eco", "close chat", "stop InventorySvc"], system.Calls);
        Assert.Equal(new GameProfileReport("My Game", true, 1, 1, 0, 1, 0), events.Single());
        Assert.Equal("My Game", controller.ActiveGameName);
        Assert.Equal(ProcessPriorityClass.BelowNormal, system.Processes[100].App.Priority);

        system.Calls.Clear();
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.Equal(["start InventorySvc", "set browser Normal throttle 0/0"], system.Calls);
        Assert.Equal(ProcessPriorityClass.Normal, system.Processes[100].App.Priority);
        Assert.False(system.Processes.ContainsKey(101));   // closed apps aren't reopened
        Assert.Null(controller.ActiveGameName);
        Assert.Null(new GameProfileSessionSettings(_dir).Store.ProfileId);
        Assert.Empty(reports);
    }

    /// <summary>
    /// A game run whose end Windows never reported used to count as "still running" forever, so the next run's
    /// session never ended: its power mode and paused services stayed until LLT exited.
    /// </summary>
    [Fact]
    public async Task AGameWhoseEndWasNeverReported_DoesNotKeepTheNextSessionOn()
    {
        var system = new ScriptedSystem();
        system.AddService("SysMain");
        var (controller, _, reports) = Create(system, Profile(p =>
        {
            p.PauseServices = ["SysMain"];
            p.PowerMode = PowerModeState.Performance;
        }));

        await controller.ProcessStartedAsync("game", GamePid);
        system.Ended.Add(GamePid);   // the first run quits, but its end event is lost

        await controller.ProcessStartedAsync("game", GamePid + 1);
        system.Ended.Add(GamePid + 1);
        await controller.ProcessStoppedAsync("game", GamePid + 1);

        Assert.Null(controller.ActiveGameName);
        Assert.Equal(PowerModeState.Balance, system.PowerMode);
        Assert.True(system.Services["SysMain"].Running);
        Assert.Empty(reports);
    }

    /// <summary>
    /// Turning the profile off mid-game stops the watching, so the game's end isn't reported. Turned on again and
    /// played again, that game's earlier run must not keep the new session on.
    /// </summary>
    [Fact]
    public async Task AProfileTurnedOffAndOnMidGame_StillEndsAfterTheNextRun()
    {
        var system = new ScriptedSystem();
        var profile = Profile(p => p.PowerMode = PowerModeState.Performance);
        var (controller, _, reports) = Create(system, profile);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync("game", GamePid);
        Assert.Equal(PowerModeState.Performance, system.PowerMode);

        profile.Enabled = false;
        await controller.RefreshAsync();     // ends the session; nothing is watched any more
        Assert.Equal(PowerModeState.Balance, system.PowerMode);
        system.Ended.Add(GamePid);           // the game quits while nothing watches

        profile.Enabled = true;
        await controller.RefreshAsync();
        await controller.ProcessStartedAsync("game", GamePid + 1);
        Assert.Equal(PowerModeState.Performance, system.PowerMode);

        system.Ended.Add(GamePid + 1);
        await controller.ProcessStoppedAsync("game", GamePid + 1);

        Assert.Null(controller.ActiveGameName);
        Assert.Equal(PowerModeState.Balance, system.PowerMode);
        Assert.Empty(reports);
    }

    /// <summary>Two runs at once: the first quitting (reported) leaves the second's session on, as before.</summary>
    [Fact]
    public async Task ASecondRunStillGoing_KeepsTheSessionOn()
    {
        var system = new ScriptedSystem();
        var (controller, _, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));

        await controller.ProcessStartedAsync("game", GamePid);
        await controller.ProcessStartedAsync("game", GamePid + 1);

        system.Ended.Add(GamePid);
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.Equal("My Game", controller.ActiveGameName);
        Assert.Equal(PowerModeState.Performance, system.PowerMode);
    }

    [Fact]
    public async Task TheGamesOwnProcess_IsNeverOpenedOrChanged_EvenWhenListed()
    {
        var system = new ScriptedSystem();
        system.AddApp(GamePid, "game");
        var (controller, _, _) = Create(system, Profile(p =>
        {
            p.EfficiencyApps = ["game", "Lenovo Legion Toolkit"];
            p.CloseApps = ["game"];
        }));

        await controller.ProcessStartedAsync("game", GamePid);
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.DoesNotContain(GamePid, system.TouchedPids);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task AppsLltMustNotTouch_AreLeftAlone()
    {
        var system = new ScriptedSystem();
        system.AddApp(200, "svchostlike", session: 0);                                 // a service process
        system.AddApp(201, "winapp", path: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "winapp.exe"));
        system.AddApp(202, "rt", ProcessPriorityClass.RealTime);
        var (controller, _, _) = Create(system, Profile(p => p.EfficiencyApps = ["svchostlike", "winapp", "rt"]));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Empty(system.Calls);
        Assert.Equal(3, events.Single().Problems);
    }

    /// <summary>Windows' own shell processes are never eased or closed, even if one is reported outside the Windows folder.</summary>
    [Fact]
    public async Task WindowsShellApps_AreNeverTouched_WhateverTheirPath()
    {
        var system = new ScriptedSystem();
        system.AddApp(300, "explorer");                   // the scripted path is a user folder
        system.AddApp(301, "StartMenuExperienceHost");
        var (controller, _, _) = Create(system, Profile(p =>
        {
            p.EfficiencyApps = ["explorer"];
            p.CloseApps = ["StartMenuExperienceHost"];
        }));

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Empty(system.Calls);
        Assert.DoesNotContain(300, system.TouchedPids);
        Assert.DoesNotContain(301, system.TouchedPids);
    }

    /// <summary>An app listed both to ease and to close is only closed: easing it first would be pointless.</summary>
    [Fact]
    public async Task AnAppWithHelpers_CountsOnce_AndItsHelpersArentLeftOpen()
    {
        // One window plus five windowless helpers (WebView2, Electron): Windows has six processes, the user one app.
        var system = new ScriptedSystem();
        system.AddApp(100, "claude");
        for (var pid = 101; pid <= 105; pid++)
        {
            system.AddApp(pid, "claude");
            system.Windowless.Add(pid);
        }
        var (controller, _, _) = Create(system, Profile(p => p.CloseApps = ["claude"]));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        var report = events.Single();
        Assert.Equal(1, report.AppsClosed);
        Assert.Equal(0, report.AppsLeftOpen);
    }

    [Fact]
    public async Task AnAppWithOnlyHelpers_IsNotCountedAtAll()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "msedgewebview2");
        system.AddApp(101, "msedgewebview2");
        system.Windowless.UnionWith([100, 101]);
        var (controller, _, _) = Create(system, Profile(p => p.CloseApps = ["msedgewebview2"]));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        var report = events.Single();
        Assert.Equal(0, report.AppsClosed);
        Assert.Equal(0, report.AppsLeftOpen);
        Assert.True(system.Processes.ContainsKey(100) && system.Processes.ContainsKey(101));   // nothing ended
    }

    [Fact]
    public async Task AWindowThatWontClose_CountsItsAppAsLeftOpen()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "editor");
        system.AddApp(101, "editor");
        system.Windowless.Add(101);
        system.WontClose.Add(100);   // asking whether to save
        var (controller, _, _) = Create(system, Profile(p => p.CloseApps = ["editor"]));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        var report = events.Single();
        Assert.Equal(0, report.AppsClosed);
        Assert.Equal(1, report.AppsLeftOpen);
    }

    [Fact]
    public async Task AnAppInBothLists_IsClosed_NotEasedFirst()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "comet");
        var (controller, session, _) = Create(system, Profile(p =>
        {
            p.EfficiencyApps = ["comet"];
            p.CloseApps = ["COMET"];
        }));

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Equal(["close comet"], system.Calls);
        Assert.Empty(session.Store.EasedApps);
    }

    [Fact]
    public async Task AnAppAlreadyInEfficiencyMode_KeepsIt_AndOnlyItsPriorityIsPutBack()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "browser", throttle: ThrottleState.Efficient);
        var (controller, _, _) = Create(system, Profile(p => p.EfficiencyApps = ["browser"]));

        await controller.ProcessStartedAsync("game", GamePid);
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.Equal(["set browser BelowNormal -", "set browser Normal -"], system.Calls);
        Assert.True(system.Processes[100].App.Throttle!.Value.IsEfficient);
    }

    [Fact]
    public async Task WhenWindowsCantReportEfficiencyMode_OnlyThePriorityChanges()
    {
        var system = new ScriptedSystem();
        system.Processes[100] = ("browser", new AppProcess(100, Started, UserApp, 1, ProcessPriorityClass.Normal, null));
        var (controller, _, _) = Create(system, Profile(p => p.EfficiencyApps = ["browser"]));

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Equal(["set browser BelowNormal -"], system.Calls);
    }

    [Fact]
    public async Task AnAppIdReusedByAnotherProcess_IsNotTouchedOnRestore()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "browser");
        var (controller, _, _) = Create(system, Profile(p => p.EfficiencyApps = ["browser"]));

        await controller.ProcessStartedAsync("game", GamePid);
        // The browser ends and another process gets the same id.
        system.Processes[100] = ("other", new AppProcess(100, Started.AddMinutes(5), UserApp, 1, ProcessPriorityClass.High, new ThrottleState(0, 0)));
        system.Calls.Clear();
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.Empty(system.Calls);
        Assert.Equal(ProcessPriorityClass.High, system.Processes[100].App.Priority);
    }

    // ---- Services ----

    [Theory]
    [InlineData("Spooler")]          // on the fixed deny list
    [InlineData("SPOOLER")]          // in any case
    [InlineData("vgc")]              // anti-cheat
    public async Task ProtectedServices_AreNeverStopped(string name)
    {
        var system = new ScriptedSystem();
        system.AddService(name);
        var (controller, _, _) = Create(system, Profile(p => p.PauseServices = [name]));

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.DoesNotContain(system.Calls, c => c.StartsWith("stop"));
    }

    [Fact]
    public async Task ServicesThatMustNotStop_AreLeftRunning()
    {
        var system = new ScriptedSystem();
        system.AddService("drv", driver: true);
        system.AddService("nostop", canStop: false);
        system.AddService("needed", dependents: true);
        system.AddService("off", running: false);
        var (controller, _, _) = Create(system, Profile(p => p.PauseServices = ["drv", "nostop", "needed", "off", "missing"]));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Empty(system.Calls);
        Assert.Equal(4, events.Single().Problems);   // "off" isn't running: nothing to pause, not a problem
    }

    [Theory]
    [InlineData("Spooler", null, ServiceRefusal.Protected)]
    [InlineData("Appinfo", null, ServiceRefusal.Protected)]     // UAC prompts and "Run as administrator"; it was pausable before
    [InlineData("appinfo", null, ServiceRefusal.Protected)]
    [InlineData("anything", null, ServiceRefusal.NotInstalled)]
    public void Rules_RefuseBeforeLookingAtTheService(string name, ServiceInfo? service, ServiceRefusal expected) =>
        Assert.Equal(expected, ServiceRules.Check(name, service));

    [Fact]
    public void Rules_AllowAStoppableRunningService_AndListAStoppedOne()
    {
        var running = new ServiceInfo("InventorySvc", "Inventory", false, true, true, false);
        Assert.Null(ServiceRules.Check("InventorySvc", running));

        var stopped = running with { Running = false };
        Assert.Equal(ServiceRefusal.NotRunning, ServiceRules.Check("InventorySvc", stopped));
        Assert.Null(ServiceRules.CheckForList("InventorySvc", stopped));   // may be added; paused if running then
    }

    [Fact]
    public async Task AServiceRunningAgainBeforeTheGameEnds_IsLeftAsItIs()
    {
        var system = new ScriptedSystem();
        system.AddService("InventorySvc");
        var (controller, _, _) = Create(system, Profile(p => p.PauseServices = ["InventorySvc"]));

        await controller.ProcessStartedAsync("game", GamePid);
        system.Services["InventorySvc"] = system.Services["InventorySvc"] with { Running = true };   // started by something else
        system.Calls.Clear();
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task AFailedStop_IsCounted_AndTheOtherStepsStillRun()
    {
        var system = new ScriptedSystem { StopFails = true };
        system.AddApp(100, "browser");
        system.AddService("InventorySvc");
        var (controller, session, _) = Create(system, Profile(p =>
        {
            p.PauseServices = ["InventorySvc"];
            p.EfficiencyApps = ["browser"];
        }));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Contains("set browser BelowNormal eco", system.Calls);
        Assert.Equal(1, events.Single().Problems);
        Assert.Empty(session.Store.PausedServices);   // still running, so nothing to start again later
    }

    // ---- Lifetimes ----

    [Fact]
    public async Task TwoInstancesOfTheGame_PutBackOnlyAfterTheLastEnds()
    {
        var system = new ScriptedSystem();
        system.AddService("InventorySvc");
        var (controller, _, _) = Create(system, Profile(p => p.PauseServices = ["InventorySvc"]));

        await controller.ProcessStartedAsync("game", GamePid);
        await controller.ProcessStartedAsync("game", GamePid + 1);
        await controller.ProcessStoppedAsync("game", GamePid);
        Assert.DoesNotContain("start InventorySvc", system.Calls);

        await controller.ProcessStoppedAsync("game", GamePid + 1);
        Assert.Contains("start InventorySvc", system.Calls);
        Assert.Single(system.Calls, c => c.StartsWith("stop"));
    }

    /// <summary>
    /// LLT ended while the game ran: the session note is still there at the next start, and everything in it is
    /// put back. A new controller over the same files plays the next start.
    /// </summary>
    [Fact]
    public async Task AfterLltWasEndedMidGame_TheNextStartPutsEverythingBack()
    {
        var system = new ScriptedSystem();
        system.AddApp(100, "browser");
        system.AddService("InventorySvc");
        var profile = Profile(p =>
        {
            p.EfficiencyApps = ["browser"];
            p.PauseServices = ["InventorySvc"];
        });
        var (first, _, _) = Create(system, profile);
        await first.ProcessStartedAsync("game", GamePid);
        system.Calls.Clear();

        var (next, session, _) = Create(system, profile);
        Assert.Equal(profile.Id, session.Store.ProfileId);
        await next.RecoverAsync();

        Assert.Equal(["start InventorySvc", "set browser Normal throttle 0/0"], system.Calls);
        Assert.Null(new GameProfileSessionSettings(_dir).Store.ProfileId);
    }

    [Fact]
    public async Task ForcedGameSafe_IsOnOnlyWhileTheGameRuns()
    {
        var system = new ScriptedSystem();
        var (controller, _, _) = Create(system, Profile(p => p.ForceGameSafe = true));
        var changes = 0;
        controller.ForceGameSafeChanged += (_, _) => changes++;

        await controller.ProcessStartedAsync("game", GamePid);
        Assert.True(controller.ForceGameSafe);

        await controller.ProcessStoppedAsync("game", GamePid);
        Assert.False(controller.ForceGameSafe);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task ADisabledProfile_DoesNothing()
    {
        var system = new ScriptedSystem();
        system.AddService("InventorySvc");
        var (controller, _, _) = Create(system, Profile(p =>
        {
            p.Enabled = false;
            p.PauseServices = ["InventorySvc"];
        }));

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Empty(system.Calls);
        Assert.Null(controller.ActiveGameName);
    }

    [Fact]
    public async Task TurningTheActiveProfileOff_PutsEverythingBackAtOnce()
    {
        var system = new ScriptedSystem();
        system.AddService("InventorySvc");
        var profile = Profile(p => p.PauseServices = ["InventorySvc"]);
        var (controller, _, _) = Create(system, profile);
        await controller.ProcessStartedAsync("game", GamePid);

        profile.Enabled = false;
        await controller.RefreshAsync();

        Assert.Contains("start InventorySvc", system.Calls);
        Assert.Null(controller.ActiveGameName);
    }

    // ---- Power mode while the game runs ----

    [Fact]
    public async Task ThePowerMode_IsSetFirst_AndPutBackWhenTheGameEnds()
    {
        var system = new ScriptedSystem();
        system.AddService("InventorySvc");
        var (controller, session, reports) = Create(system, Profile(p =>
        {
            p.PowerMode = PowerModeState.Performance;
            p.PauseServices = ["InventorySvc"];
        }));

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.Equal(["power Performance", "stop InventorySvc"], system.Calls);
        Assert.Equal(PowerModeState.Balance, session.Store.PowerModeBefore);
        Assert.Equal(PowerModeState.Performance, session.Store.PowerModeSet);

        system.Calls.Clear();
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.Equal(["start InventorySvc", "power Balance"], system.Calls);
        Assert.Equal(PowerModeState.Balance, system.PowerMode);
        Assert.Null(new GameProfileSessionSettings(_dir).Store.PowerModeSet);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task AModeChosenDuringTheGame_IsKept()
    {
        var system = new ScriptedSystem();
        var (controller, _, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));
        await controller.ProcessStartedAsync("game", GamePid);

        system.PowerMode = PowerModeState.Quiet;   // Fn+Q during the game
        system.Calls.Clear();
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.Empty(system.Calls);
        Assert.Equal(PowerModeState.Quiet, system.PowerMode);
    }

    [Fact]
    public async Task DontChange_LeavesThePowerModeAlone()
    {
        var system = new ScriptedSystem();
        var (controller, session, _) = Create(system, Profile());

        await controller.ProcessStartedAsync("game", GamePid);
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.DoesNotContain(system.Calls, c => c.StartsWith("power"));
        Assert.Null(session.Store.PowerModeBefore);
    }

    [Fact]
    public async Task TheActivePowerMode_IsShownOnlyWhileTheGamesProfileHasSetIt()
    {
        var system = new ScriptedSystem();
        var (withMode, _, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));

        Assert.Null(withMode.ActivePowerMode);
        await withMode.ProcessStartedAsync("game", GamePid);
        Assert.Equal(PowerModeState.Performance, withMode.ActivePowerMode);
        await withMode.ProcessStoppedAsync("game", GamePid);
        Assert.Null(withMode.ActivePowerMode);

        var (leavesItAlone, _, _) = Create(new ScriptedSystem(), Profile());
        await leavesItAlone.ProcessStartedAsync("game", GamePid);
        Assert.NotNull(leavesItAlone.ActiveGameName);
        Assert.Null(leavesItAlone.ActivePowerMode);
    }

    [Fact]
    public async Task AlreadyInTheGamesMode_NothingIsChangedOrPutBack()
    {
        var system = new ScriptedSystem { PowerMode = PowerModeState.Performance };
        var (controller, session, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));

        await controller.ProcessStartedAsync("game", GamePid);
        Assert.Null(session.Store.PowerModeSet);
        await controller.ProcessStoppedAsync("game", GamePid);

        Assert.DoesNotContain(system.Calls, c => c.StartsWith("power"));
    }

    [Fact]
    public async Task AModeThatCantBeRead_IsNotChanged_BecauseItCouldntBePutBack()
    {
        var system = new ScriptedSystem { PowerMode = null };
        var (controller, _, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);

        Assert.DoesNotContain(system.Calls, c => c.StartsWith("power"));
        Assert.Equal(1, events.Single().Problems);
    }

    [Fact]
    public async Task AModeTheLaptopRefuses_IsReported_AndNothingIsPutBack()
    {
        var system = new ScriptedSystem();
        system.RefusedModes.Add(PowerModeState.Performance);   // for example on battery
        var (controller, session, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));
        var events = new List<GameProfileReport>();
        controller.Reported += (_, r) => events.Add(r);

        await controller.ProcessStartedAsync("game", GamePid);
        Assert.Equal(1, events.Single().Problems);
        Assert.Null(session.Store.PowerModeSet);

        system.Calls.Clear();
        await controller.ProcessStoppedAsync("game", GamePid);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task AfterLltWasEndedMidGame_TheNextStartPutsThePowerModeBack()
    {
        var system = new ScriptedSystem();
        var (first, _, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));
        await first.ProcessStartedAsync("game", GamePid);   // then LLT is ended mid-game

        var (next, session, _) = Create(system, Profile(p => p.PowerMode = PowerModeState.Performance));
        await next.RecoverAsync();

        Assert.Equal(PowerModeState.Balance, system.PowerMode);
        Assert.Null(session.Store.ProfileId);
    }

    [Fact]
    public async Task GameMode_SetsItsPowerModeToo()
    {
        var system = new ScriptedSystem();
        var (controller, _) = CreateWithGameMode(system, m => m.PowerMode = PowerModeState.Performance, [Valorant]);
        await controller.RefreshAsync();

        await controller.ProcessStartedAsync(ValorantAsReported, GamePid);
        Assert.Equal(PowerModeState.Performance, system.PowerMode);

        await controller.ProcessStoppedAsync(ValorantAsReported, GamePid);
        Assert.Equal(PowerModeState.Balance, system.PowerMode);
    }
}