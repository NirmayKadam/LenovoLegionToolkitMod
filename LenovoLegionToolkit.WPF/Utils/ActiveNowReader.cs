using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Reads what Indra is doing in the background right now (for the dashboard's "Active now" strip and the tray's hover
/// window). Only reads state other parts already keep, plus the process list for MSI Afterburner; it starts nothing.
/// Call it off the UI thread.
/// </summary>
public sealed class ActiveNowReader
{
    private readonly GameAutoListener _games = IoCContainer.Resolve<GameAutoListener>();
    private readonly AIController _ai = IoCContainer.Resolve<AIController>();
    private readonly GameSafeMode _gameSafe = IoCContainer.Resolve<GameSafeMode>();
    private readonly GameProfileController _gameProfiles = IoCContainer.Resolve<GameProfileController>();
    private readonly FanProfileController _fanProfiles = IoCContainer.Resolve<FanProfileController>();
    private readonly BackgroundServicesController _services = IoCContainer.Resolve<BackgroundServicesController>();
    private readonly PowerModeFeature _powerMode = IoCContainer.Resolve<PowerModeFeature>();

    /// <summary>
    /// The current state. <paramref name="knownMode"/> is the power mode when the caller already knows it (from the
    /// power-mode listener); otherwise it is read.
    /// </summary>
    public async Task<(ActiveNowState State, PowerModeState? Mode)> ReadAsync(PowerModeState? knownMode = null)
    {
        var mode = knownMode;
        if (mode is null)
        {
            try
            {
                if (await _powerMode.IsSupportedAsync())
                    mode = await _powerMode.GetStateAsync();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Active now: couldn't read the power mode.", ex);
            }
        }

        string? fanCurveMode = null;
        if (mode is { } current && _fanProfiles.CurveInUse(current).Source == FanCurveSource.UserCurve)
            fanCurveMode = current.GetDisplayName();

        var state = new ActiveNowState(
            _games.RunningGames,
            _ai.IsBoosting,
            _gameSafe.IsActive,
            _gameProfiles.ActiveGameName,
            fanCurveMode,
            _services.NotedCount,
            AfterburnerDetector.IsRunning(),
            SafeStart.IsActive);

        return (state, mode);
    }
}
