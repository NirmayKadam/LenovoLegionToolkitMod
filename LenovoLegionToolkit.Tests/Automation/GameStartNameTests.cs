using LenovoLegionToolkit.Lib.AutoListeners;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

/// <summary>
/// How the game detection used by the automation game triggers matches a started process against the known games
/// and the user's included programs. Windows' start event gives only the first 15 characters of the file name
/// (".exe" included), so a long name arrives cut short.
/// </summary>
public class GameStartNameTests
{
    [Theory]
    [InlineData("VALORANT-Win64-", "VALORANT-Win64-Shipping")]   // as Windows reports VALORANT-Win64-Shipping.exe
    [InlineData("valorant-win64-", "VALORANT-Win64-Shipping")]
    [InlineData("Game", "Game")]                                  // short names arrive whole
    public void AStartedGame_IsRecognised_EvenWhenWindowsCutItsName(string reported, string known)
    {
        Assert.True(GameAutoListener.MatchesStartedName(reported, [known]));
    }

    [Theory]
    [InlineData("VALORANT", "VALORANT-Win64-Shipping")]           // the launcher is a different program
    [InlineData("Game2", "Game")]
    public void ADifferentProgram_IsNotTakenForTheGame(string reported, string known)
    {
        Assert.False(GameAutoListener.MatchesStartedName(reported, [known]));
    }
}
