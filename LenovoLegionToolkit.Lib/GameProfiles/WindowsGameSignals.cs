using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.GameDetection;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>
/// The real sources Game mode uses to tell a game is running, besides process start and end events: Windows' own
/// list of games it has recognised (watched for changes), and Windows Game Mode turning on (the foreground window's
/// program is taken as the game). The game's process is found in the process list and never opened. Runs only
/// while Game mode is on.
/// </summary>
internal sealed class WindowsGameSignals
{
    private readonly GameConfigStoreDetector _knownGames = new();
    private readonly EffectiveGameModeDetector _gameMode = new();
    private bool _running;

    public WindowsGameSignals()
    {
        _knownGames.GamesDetected += (_, _) => KnownGamesChanged?.Invoke(this, EventArgs.Empty);
        _gameMode.Changed += GameMode_Changed;
    }

    /// <summary>Windows' known-games list changed (a game was added or removed).</summary>
    public event EventHandler? KnownGamesChanged;

    /// <summary>Windows Game Mode turned on with this program in front: its name (without ".exe") and process id.</summary>
    public event EventHandler<(string Name, int ProcessId)>? GameModeGameStarted;

    /// <summary>The names (without ".exe") of the games Windows has recognised on this PC.</summary>
    public static IReadOnlyCollection<string> ReadKnownGameNames() =>
        GameConfigStoreDetector.GetDetectedGamePaths().Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task SetRunningAsync(bool running)
    {
        if (running == _running)
            return;

        _running = running;
        if (running)
        {
            await _knownGames.StartAsync().ConfigureAwait(false);
            await _gameMode.StartAsync().ConfigureAwait(false);
        }
        else
        {
            await _knownGames.StopAsync().ConfigureAwait(false);
            await _gameMode.StopAsync().ConfigureAwait(false);
        }
    }

    private void GameMode_Changed(object? sender, bool on)
    {
        if (!on)
            return;

        try
        {
            if (ForegroundProcess() is { } game)
                GameModeGameStarted?.Invoke(this, game);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Game mode: couldn't tell which program turned Windows Game Mode on.", ex);
        }
    }

    /// <summary>The foreground window's program, from the process list (the process itself isn't opened).</summary>
    private static unsafe (string Name, int ProcessId)? ForegroundProcess()
    {
        var hWnd = Windows.Win32.PInvoke.GetForegroundWindow();
        if (hWnd == Windows.Win32.Foundation.HWND.Null)
            return null;

        uint processId = 0;
        Windows.Win32.PInvoke.GetWindowThreadProcessId(hWnd, &processId);
        if (processId == 0)
            return null;

        (string, int)? found = null;
        foreach (var process in Process.GetProcesses())
        {
            if (found is null && process.Id == processId)
                found = (process.ProcessName, process.Id);
            process.Dispose();
        }
        return found;
    }
}
