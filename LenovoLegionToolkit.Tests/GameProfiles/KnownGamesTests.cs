using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameProfiles;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameProfiles;

/// <summary>"Add game → Games Windows knows": what is offered from Windows' list (given here as plain entries).</summary>
public class KnownGamesTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Fact]
    public void OffersGames_ButNotLltWindowsAppsOrGamesAlreadyProfiled()
    {
        var entries = new[]
        {
            new ProcessInfo("VALORANT-Win64-Shipping", @"C:\Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe"),
            new ProcessInfo("cs2", @"D:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe"),
            new ProcessInfo("Lenovo Legion Toolkit", @"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe"),
            new ProcessInfo("explorer", Path.Combine(Windows, "explorer.exe")),
            new ProcessInfo("solitaire", Path.Combine(Windows, "System32", "solitaire.exe")),
            new ProcessInfo("cs2", @"E:\Other\cs2.exe"),   // the same name twice: offered once
        };

        var offered = GameProfileCatalog.FilterKnownGames(entries, excludeNames: ["cs2"]);

        Assert.Equal(["VALORANT-Win64-Shipping"], offered.Select(g => g.Name));
        Assert.EndsWith("VALORANT-Win64-Shipping.exe", offered.Single().Path);
    }

    [Fact]
    public void AnEmptyList_OffersNothing()
    {
        Assert.Empty(GameProfileCatalog.FilterKnownGames([], []));
    }
}
