using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.GameProfiles;

namespace LenovoLegionToolkit.Lib.AutoListeners;

/// <summary>
/// The games game detection counts as running, by process id and the name Windows reported for it. Nothing here
/// opens a process: games are added from process events and process-list snapshots, and dropped when Windows
/// reports them ended or a snapshot no longer shows them. (A held process handle, as before, is exactly what kernel
/// anti-cheats look for.)
/// </summary>
internal sealed class GameTracker
{
    /// <param name="Name">As Windows reported it; a process-start event may cut it to 15 characters.</param>
    /// <param name="PinnedByGameMode">Found because Windows Game Mode turned on for it.</param>
    public readonly record struct Game(int Id, string Name, string Source, bool PinnedByGameMode);

    private readonly Dictionary<int, Game> _games = [];

    public int Count => _games.Count;

    public bool IsEmpty => _games.Count == 0;

    public IReadOnlyCollection<Game> Games => _games.Values;

    public bool AnyPinnedByGameMode => _games.Values.Any(g => g.PinnedByGameMode);

    public bool Contains(int processId) => _games.ContainsKey(processId);

    /// <summary>Starts counting a game; false when that process is already counted.</summary>
    public bool Add(int processId, string name, string source, bool pinnedByGameMode = false) =>
        _games.TryAdd(processId, new Game(processId, name, source, pinnedByGameMode));

    /// <summary>Windows reported the process ended; false when it wasn't a counted game.</summary>
    public bool Remove(int processId) => _games.Remove(processId);

    public void Clear() => _games.Clear();

    /// <summary>
    /// Keeps only the games the snapshot of running processes still shows under the same name, and returns the ones
    /// dropped: ended without the end being reported, or their id now belongs to another program.
    /// </summary>
    public IReadOnlyList<Game> Reconcile(IEnumerable<(int Id, string Name)> running)
    {
        var names = running.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Name);

        var gone = _games.Values
            .Where(game => !names.TryGetValue(game.Id, out var now) || !IsSameProgram(game.Name, now))
            .ToList();

        foreach (var game in gone)
            _games.Remove(game.Id);

        return gone;
    }

    /// <summary>The reported name may be the 15-character cut of the full one (see <see cref="GameProfileController.IsGame"/>).</summary>
    private static bool IsSameProgram(string reportedName, string runningName) => GameProfileController.IsGame(reportedName, runningName);
}
