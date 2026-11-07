using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.Automation;

public class AutomationProcessor(
    AutomationSettings settings,
    DisplayConfigurationListener displayConfigurationListener,
    NativeWindowsMessageListener nativeWindowsMessageListener,
    PowerStateListener powerStateListener,
    PowerModeListener powerModeListener,
    GodModeController godModeController,
    BatteryAutoListener batteryAutoListener,
    GameAutoListener gameAutoListener,
    ProcessAutoListener processAutoListener,
    SessionLockUnlockListener sessionLockUnlockListener,
    TimeAutoListener timeAutoListener,
    UserInactivityAutoListener userInactivityAutoListener,
    WiFiAutoListener wifiAutoListener)
{
    private readonly AsyncLock _ioLock = new();
    private readonly AsyncLock _runLock = new();
    private readonly Lock _ctsLock = new();

    private List<AutomationPipeline> _pipelines = [];

    /// <summary>
    /// Token source of the event-triggered run currently holding <see cref="_runLock"/>, if any.
    /// Only read or written under <see cref="_ctsLock"/>.
    /// </summary>
    private CancellationTokenSource? _cts;

    public bool IsEnabled => settings.Store.IsEnabled;

    public event EventHandler<List<AutomationPipeline>>? PipelinesChanged;

    #region Initialization / pipeline reloading

    public async Task InitializeAsync()
    {
        using (await _ioLock.LockAsync().ConfigureAwait(false))
        {
            displayConfigurationListener.Changed += DisplayConfigurationListener_Changed;
            nativeWindowsMessageListener.Changed += NativeWindowsMessageListener_Changed;
            powerStateListener.Changed += PowerStateListener_Changed;
            powerModeListener.Changed += PowerModeListener_Changed;
            godModeController.PresetChanged += GodModeController_PresetChanged;
            sessionLockUnlockListener.Changed += SessionLockUnlockListener_Changed;

            _pipelines = [.. settings.Store.Pipelines];

            RaisePipelinesChanged();

            await UpdateListenersAsync().ConfigureAwait(false);
            await nativeWindowsMessageListener.EnsureInitializedAsync().ConfigureAwait(false);
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        using (await _ioLock.LockAsync().ConfigureAwait(false))
        {
            settings.Store.IsEnabled = enabled;
            settings.SynchronizeStore();

            await UpdateListenersAsync().ConfigureAwait(false);
        }
    }

    public async Task ReloadPipelinesAsync(List<AutomationPipeline> pipelines)
    {
        Log.Instance.Trace($"Pipelines reload pending...");

        using (await _ioLock.LockAsync().ConfigureAwait(false))
        {
            Log.Instance.Trace($"Pipelines reloading...");

            _pipelines = pipelines.Select(p => p.DeepCopy()).ToList();

            settings.Store.Pipelines = pipelines;
            settings.SynchronizeStore();

            RaisePipelinesChanged();

            await UpdateListenersAsync().ConfigureAwait(false);

            Log.Instance.Trace($"Pipelines reloaded.");
        }
    }

    public async Task<List<AutomationPipeline>> GetPipelinesAsync()
    {
        using (await _ioLock.LockAsync().ConfigureAwait(false))
            return _pipelines.Select(p => p.DeepCopy()).ToList();
    }

    public async Task RestartListenersAsync()
    {
        using (await _ioLock.LockAsync().ConfigureAwait(false))
        {
            await UpdateListenersAsync().ConfigureAwait(false);
        }
    }

    #endregion

    #region Run

    public void RunOnStartup()
    {
        if (!IsEnabled)
        {
            Log.Instance.Trace($"Not enabled. Pipeline run on startup ignored.");

            return;
        }

        Log.Instance.Trace($"Pipeline run on startup pending...");

        Task.Run(() => ProcessEvent(new StartupAutomationEvent()));
    }

    public async Task RunNowAsync(AutomationPipeline pipeline)
    {
        await Task.Run(() => RunNowCoreAsync(pipeline)).ConfigureAwait(false);
    }

    private async Task RunNowCoreAsync(AutomationPipeline pipeline)
    {
        Log.Instance.Trace($"Pipeline run now pending...");

        using (await _runLock.LockAsync().ConfigureAwait(false))
        {
            Log.Instance.Trace($"Pipeline run starting...");

            try
            {
                List<AutomationPipeline> pipelines;
                using (await _ioLock.LockAsync().ConfigureAwait(false))
                {
                    pipelines = _pipelines.ToList();
                }

                var otherPipelines = pipelines.Where(p => p.Id != pipeline.Id).ToList();
                await pipeline.DeepCopy().RunAsync(otherPipelines).ConfigureAwait(false);

                Log.Instance.Trace($"Pipeline run finished successfully.");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Pipeline run failed.", ex);

                throw;
            }
        }
    }

    public async Task RunNowAsync(Guid pipelineId)
    {
        AutomationPipeline? pipeline;
        using (await _ioLock.LockAsync().ConfigureAwait(false))
            pipeline = _pipelines.Where(p => p.Trigger is null).FirstOrDefault(p => p.Id == pipelineId);

        if (pipeline is null)
            return;

        await RunNowAsync(pipeline).ConfigureAwait(false);
    }

    private async Task RunAsync(IAutomationEvent automationEvent)
    {
        await Task.Run(() => RunCoreAsync(automationEvent)).ConfigureAwait(false);
    }

    private async Task RunCoreAsync(IAutomationEvent automationEvent)
    {
        Log.Instance.Trace($"Run pending...");

        // Interrupt the run in flight *before* waiting for the lock. (Cancelling after acquiring
        // the lock, as before, only ever hit a run that had already finished.)
        lock (_ctsLock)
        {
            if (_cts is not null)
            {
                Log.Instance.Trace($"Interrupting run in progress...");
                _cts.Cancel();
            }
        }

        using (await _runLock.LockAsync().ConfigureAwait(false))
        {
            Log.Instance.Trace($"Run starting...");

            if (!IsEnabled)
                return;

            List<AutomationPipeline> pipelines;
            using (await _ioLock.LockAsync().ConfigureAwait(false))
                pipelines = _pipelines.ToList();

            // Created only once this run holds the lock, so a later event interrupts this run
            // but never drops a run that is still queued.
            var cts = new CancellationTokenSource();
            lock (_ctsLock)
                _cts = cts;

            try
            {
                await RunPipelinesAsync(automationEvent, pipelines, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_ctsLock)
                {
                    if (ReferenceEquals(_cts, cts))
                        _cts = null;
                }

                cts.Dispose();
            }
        }
    }

    private static async Task RunPipelinesAsync(IAutomationEvent automationEvent, List<AutomationPipeline> pipelines, CancellationToken ct)
    {
        foreach (var pipeline in pipelines)
        {
            if (ct.IsCancellationRequested)
            {
                Log.Instance.Trace($"Run interrupted.");
                return;
            }

            try
            {
                if (automationEvent is StartupAutomationEvent &&
                    !pipeline.RunOnStartup &&
                    pipeline.Trigger is not OnStartupAutomationPipelineTrigger)
                {
                    Log.Instance.Trace($"Pipeline configured to skip startup. [name={pipeline.Name}]");
                    continue;
                }

                if (pipeline.Trigger is null || !await pipeline.Trigger.IsMatchingEvent(automationEvent).ConfigureAwait(false))
                {
                    Log.Instance.Trace($"Pipeline triggers not satisfied. [name={pipeline.Name}, trigger={pipeline.Trigger}, steps.Count={pipeline.Steps.Count}]");
                    continue;
                }

                Log.Instance.Trace($"Running pipeline... [name={pipeline.Name}, trigger={pipeline.Trigger}, steps.Count={pipeline.Steps.Count}]");

                var otherPipelines = pipelines.Where(p => p.Id != pipeline.Id).ToList();
                await pipeline.RunAsync(otherPipelines, ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                {
                    Log.Instance.Trace($"Run interrupted. [name={pipeline.Name}, trigger={pipeline.Trigger}]");
                    return;
                }

                Log.Instance.Trace($"Pipeline completed successfully. [name={pipeline.Name}, trigger={pipeline.Trigger}]");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Log.Instance.Trace($"Run interrupted. [name={pipeline.Name}, trigger={pipeline.Trigger}]");
                return;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Pipeline run failed. [name={pipeline.Name}, trigger={pipeline.Trigger}]", ex);
            }

            if (pipeline.IsExclusive)
            {
                Log.Instance.Trace($"Pipeline is exclusive. Breaking. [name={pipeline.Name}, trigger={pipeline.Trigger}, steps.Count={pipeline.Steps.Count}]");
                break;
            }
        }

        Log.Instance.Trace($"Run finished successfully.");
    }

    #endregion

    #region Listeners

    private async void DisplayConfigurationListener_Changed(object? sender, DisplayConfigurationListener.ChangedEventArgs args)
    {
        var e = new HDRAutomationEvent(args.HDR);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void NativeWindowsMessageListener_Changed(object? sender, NativeWindowsMessageListener.ChangedEventArgs args)
    {
        var e = new NativeWindowsMessageEvent(args.Message, args.Data);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void PowerStateListener_Changed(object? sender, PowerStateListener.ChangedEventArgs args)
    {
        var e = new PowerStateAutomationEvent(args.PowerStateEvent, args.PowerAdapterStateChanged);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void PowerModeListener_Changed(object? sender, PowerModeListener.ChangedEventArgs args)
    {
        var e = new PowerModeAutomationEvent(args.State);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void GodModeController_PresetChanged(object? sender, Guid presetId)
    {
        var e = new CustomModePresetAutomationEvent(presetId);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void GameAutoListener_Changed(object? sender, GameAutoListener.ChangedEventArgs args)
    {
        var e = new GameAutomationEvent(args.Running);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void ProcessAutoListener_Changed(object? sender, ProcessAutoListener.ChangedEventArgs args)
    {
        var e = new ProcessAutomationEvent(args.Type, args.ProcessInfo);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void SessionLockUnlockListener_Changed(object? sender, SessionLockUnlockListener.ChangedEventArgs args)
    {
        var e = new SessionLockUnlockAutomationEvent(args.Locked);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void TimeAutoListener_Changed(object? sender, TimeAutoListener.ChangedEventArgs args)
    {
        var e = TimeAutomationEvent.FromLocal(args.UtcInstant.ToLocalTime());
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void UserInactivityAutoListener_Changed(object? sender, UserInactivityAutoListener.ChangedEventArgs args)
    {
        var e = new UserInactivityAutomationEvent(args.TimerResolution * args.TickCount);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void WiFiAutoListener_Changed(object? sender, WiFiAutoListener.ChangedEventArgs args)
    {
        var e = new WiFiAutomationEvent(args.IsConnected, args.Ssid);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    private async void BatteryAutoListener_Changed(object? sender, BatteryAutoListener.ChangedEventArgs args)
    {
        var e = new BatteryPercentageAutomationEvent(args.Percentage);
        await ProcessEvent(e).ConfigureAwait(false);
    }

    #endregion

    #region Event processing

    // Called from async void event handlers: it must never throw, or the exception ends the process.
    private async Task ProcessEvent(IAutomationEvent e)
    {
        try
        {
            var triggerMatches = await Task.WhenAll(_pipelines.SelectMany(p => p.AllTriggers)
                    .Select(async t => await t.IsMatchingEvent(e).ConfigureAwait(false)))
                .ConfigureAwait(false);

            if (!triggerMatches.Any(t => t))
            {
                return;
            }

            Log.Instance.Trace($"Processing event {e}... [type={e.GetType().Name}]");

            await RunAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to process event. [type={e.GetType().Name}]", ex);
        }
    }

    #endregion

    #region Helper methods

    private async Task UpdateListenersAsync()
    {
        Log.Instance.Trace($"Stopping listeners...");
        var wasRunning = gameAutoListener.AreGamesRunning();
        Log.Instance.Trace($"Current Game Listener State: Running={wasRunning}");

        if (wasRunning)
        {
            gameAutoListener.PreserveStateOnRestart();
        }

        await batteryAutoListener.UnsubscribeChangedAsync(BatteryAutoListener_Changed).ConfigureAwait(false);
        await gameAutoListener.UnsubscribeChangedAsync(GameAutoListener_Changed).ConfigureAwait(false);
        await processAutoListener.UnsubscribeChangedAsync(ProcessAutoListener_Changed).ConfigureAwait(false);
        await timeAutoListener.UnsubscribeChangedAsync(TimeAutoListener_Changed).ConfigureAwait(false);
        await userInactivityAutoListener.UnsubscribeChangedAsync(UserInactivityAutoListener_Changed).ConfigureAwait(false);
        await wifiAutoListener.UnsubscribeChangedAsync(WiFiAutoListener_Changed).ConfigureAwait(false);

        Log.Instance.Trace($"Stopped listeners...");

        if (!IsEnabled)
        {
            Log.Instance.Trace($"Not enabled. Will not start listeners.");
            return;
        }

        Log.Instance.Trace($"Starting listeners...");

        var triggers = _pipelines.SelectMany(p => p.AllTriggers).ToArray();

        if (triggers.OfType<IGameAutomationPipelineTrigger>().Any())
        {
            Log.Instance.Trace($"Starting game listener...");

            await gameAutoListener.SubscribeChangedAsync(GameAutoListener_Changed).ConfigureAwait(false);
        }

        if (triggers.OfType<IProcessesAutomationPipelineTrigger>().Any())
        {
            Log.Instance.Trace($"Starting process listener...");

            await processAutoListener.SubscribeChangedAsync(ProcessAutoListener_Changed).ConfigureAwait(false);
        }

        if (triggers.OfType<ITimeAutomationPipelineTrigger>().Any() || triggers.OfType<ITimeRangeAutomationPipelineTrigger>().Any() || triggers.OfType<IPeriodicAutomationPipelineTrigger>().Any())
        {
            Log.Instance.Trace($"Starting time listener...");

            await timeAutoListener.SubscribeChangedAsync(TimeAutoListener_Changed).ConfigureAwait(false);
        }

        if (triggers.OfType<IUserInactivityPipelineTrigger>().Any())
        {
            Log.Instance.Trace($"Starting user inactivity listener...");

            await userInactivityAutoListener.SubscribeChangedAsync(UserInactivityAutoListener_Changed).ConfigureAwait(false);
        }

        if (triggers.OfType<IWiFiConnectedPipelineTrigger>().Any() || triggers.OfType<WiFiDisconnectedAutomationPipelineTrigger>().Any())
        {
            Log.Instance.Trace($"Starting WiFi listener...");

            await wifiAutoListener.SubscribeChangedAsync(WiFiAutoListener_Changed).ConfigureAwait(false);
        }

        if (triggers.OfType<IBatteryPercentageAutomationPipelineTrigger>().Any())
        {
            Log.Instance.Trace($"Starting battery listener...");

            await batteryAutoListener.SubscribeChangedAsync(BatteryAutoListener_Changed).ConfigureAwait(false);
        }

        Log.Instance.Trace($"Started relevant listeners.");
    }

    private void RaisePipelinesChanged()
    {
        PipelinesChanged?.Invoke(this, _pipelines.Select(p => p.DeepCopy()).ToList());
    }

    #endregion

}
