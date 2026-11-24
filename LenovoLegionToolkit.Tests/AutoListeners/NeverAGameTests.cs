using LenovoLegionToolkit.Lib.AutoListeners;
using Xunit;

namespace LenovoLegionToolkit.Tests.AutoListeners;

/// <summary>
/// The built-in never-a-game list. On 2026-11-23 the discrete-GPU source counted Steam, the Snipping Tool, Game Bar, the
/// Xbox Game Bar widgets and the NVIDIA App as games (and Indra itself), which kept AI mode boosted with no game running.
/// </summary>
public class NeverAGameTests
{
    [Theory]
    [InlineData("steam")]
    [InlineData("SnippingTool")]
    [InlineData("GameBar")]
    [InlineData("XboxGameBarWidgets")]
    [InlineData("XboxPcApp")]
    [InlineData("GameBarFTServer")]
    [InlineData("NVIDIA App")]
    [InlineData("NVIDIA Overlay")]
    [InlineData("nvcontainer")]
    [InlineData("msedge")]
    [InlineData("msedgewebview2")]
    [InlineData("Lenovo Legion Toolkit")]
    public void TodaysFalseGames_AreNeverGames(string name) => Assert.True(GameAutoListener.IsNeverAGame(name));

    [Theory]
    [InlineData("explorer")]
    [InlineData("SearchUI")]
    [InlineData("LockApp")]
    [InlineData("TextInputHost")]
    [InlineData("ShellExperienceHost")]
    [InlineData("StartMenuExperienceHost")]
    [InlineData("SearchHost")]
    [InlineData("dwm")]
    [InlineData("csrss")]
    [InlineData("WmiApSrv")]
    [InlineData("HWiNFO64")]
    [InlineData("HWiNFO32")]
    [InlineData("nvidia-smi")]
    [InlineData("steamwebhelper")]
    [InlineData("EpicWebHelper")]
    [InlineData("GalaxyCommunicationService")]
    [InlineData("GalaxyClientHelper")]
    [InlineData("EABackgroundService")]
    [InlineData("Link2EA")]
    [InlineData("RiotClientCrashHandler")]
    [InlineData("CrashReportClient")]
    [InlineData("UnityCrashHandler64")]
    [InlineData("UnityCrashHandler32")]
    [InlineData("UE4-CrashTracker")]
    [InlineData("crs-handler")]
    [InlineData("cefsharp.browsersubprocess")]
    [InlineData("conhost")]
    [InlineData("nvngx_update")]
    [InlineData("GameGuard")]
    [InlineData("GameGuard.des")]
    [InlineData("GameMon")]
    [InlineData("GameMon64")]
    [InlineData("EasyAntiCheat")]
    [InlineData("EasyAntiCheat_EOS")]
    [InlineData("BEService")]
    [InlineData("BEService_x64")]
    [InlineData("vgc")]
    [InlineData("vgtray")]
    [InlineData("EAAntiCheat.GameService")]
    [InlineData("EAAntiCheat.GameServiceLauncher")]
    [InlineData("ACE-Base")]
    [InlineData("UbisoftConnect")]
    [InlineData("UbisoftGameLauncher")]
    [InlineData("Battle.net Helper")]
    [InlineData("Agent")]
    public void TheNamesListedBefore_AreStillNeverGames(string name) => Assert.True(GameAutoListener.IsNeverAGame(name));

    [Theory]
    [InlineData("witcher3")]
    [InlineData("Heaven")]
    [InlineData("VALORANT-Win64-Shipping")]
    [InlineData("eldenring")]
    public void Games_AreNotOnTheList(string name) => Assert.False(GameAutoListener.IsNeverAGame(name));

    [Theory]
    [InlineData("STEAM")]
    [InlineData("snippingtool")]
    [InlineData("nvidia app")]
    public void NamesCompare_IgnoringCase(string name) => Assert.True(GameAutoListener.IsNeverAGame(name));

    // ---- Long names from the start event ----

    [Theory]
    [InlineData("UnityCrashHand", @"C:\Games\X\UnityCrashHandler64.exe", "UnityCrashHandler64")]
    [InlineData("EAAntiCheat.Ga", @"C:\Program Files\EA\EAAntiCheat.GameService.exe", "EAAntiCheat.GameService")]
    [InlineData("SomeLongGameNa", @"D:\Games\SomeLongGameName.exe", "SomeLongGameName")]
    public void WithAPath_TheFullName_IsChecked_NotTheCutOne(string reported, string path, string expected)
    {
        // Windows' start event carries only the first 15 characters of the file name (".exe" included).
        Assert.Equal(expected, GameAutoListener.NameToCheck(reported, path));
    }

    [Fact]
    public void ALongNeverAGameName_IsCaught_OnceTheFullNameIsUsed()
    {
        const string reported = "UnityCrashHand";   // as the start event reports UnityCrashHandler64.exe

        Assert.False(GameAutoListener.IsNeverAGame(reported));   // the cut name slipped past before
        Assert.True(GameAutoListener.IsNeverAGame(GameAutoListener.NameToCheck(reported, @"C:\Games\X\UnityCrashHandler64.exe")));
    }

    [Fact]
    public void WithoutAPath_TheReportedName_IsAllThereIs() =>
        Assert.Equal("witcher3", GameAutoListener.NameToCheck("witcher3", null));
}
