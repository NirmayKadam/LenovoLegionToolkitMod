using System.Linq;
using LenovoLegionToolkit.Lib.AutoListeners;
using Xunit;

namespace LenovoLegionToolkit.Tests.AutoListeners;

/// <summary>
/// The games game detection counts as running, kept by process id and reported name only (no process handle).
/// Snapshots are lists of (id, name) as the process list gives them.
/// </summary>
public class GameTrackerTests
{
    [Fact]
    public void AGame_IsCountedOnce_AndForgottenWhenItEnds()
    {
        var games = new GameTracker();

        Assert.True(games.Add(1744, "witcher3", "Known Game List"));
        Assert.False(games.Add(1744, "witcher3", "Discrete GPU"));
        Assert.Equal(1, games.Count);

        Assert.True(games.Remove(1744));
        Assert.True(games.IsEmpty);
        Assert.False(games.Remove(1744));
    }

    [Fact]
    public void AGameWhoseEndWasMissed_IsDroppedWhenTheProcessListNoLongerShowsIt()
    {
        var games = new GameTracker();
        games.Add(1744, "witcher3", "Known Game List");
        games.Add(2200, "game", "Included");

        var gone = games.Reconcile([(2200, "game"), (4, "System")]);

        Assert.Equal([1744], gone.Select(g => g.Id));
        Assert.Equal([2200], games.Games.Select(g => g.Id));
    }

    [Fact]
    public void AnIdNowUsedByAnotherProgram_IsNotCountedAsTheGame()
    {
        var games = new GameTracker();
        games.Add(1744, "witcher3", "Known Game List");

        var gone = games.Reconcile([(1744, "notepad")]);

        Assert.Single(gone);
        Assert.True(games.IsEmpty);
    }

    [Fact]
    public void ANameCutTo15Characters_StillMatchesTheRunningProgram()
    {
        // The process-start event reports the first 15 characters of "VALORANT-Win64-Shipping.exe".
        var games = new GameTracker();
        games.Add(3000, "VALORANT-Win64-", "Included");

        var gone = games.Reconcile([(3000, "VALORANT-Win64-Shipping")]);

        Assert.Empty(gone);
        Assert.Equal(1, games.Count);
    }

    [Fact]
    public void GamesFoundByWindowsGameMode_AreMarked()
    {
        var games = new GameTracker();
        games.Add(10, "launcher", "Included");
        Assert.False(games.AnyPinnedByGameMode);

        games.Add(20, "indiegame", "Windows Game Mode", pinnedByGameMode: true);
        Assert.True(games.AnyPinnedByGameMode);

        games.Reconcile([(10, "launcher")]);
        Assert.False(games.AnyPinnedByGameMode);
    }
}
