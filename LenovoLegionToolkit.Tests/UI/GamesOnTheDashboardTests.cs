using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.WPF;
using LenovoLegionToolkit.WPF.Controls.Games;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The Games group on the Dashboard: its place in the default layout, its cards, the status lines they show, and
/// Settings → App behaviour using the same cards. Reads the real resources and XAML.
/// </summary>
public class GamesOnTheDashboardTests
{
    private static readonly DashboardItem[] GameItems =
        [DashboardItem.GameDetection, DashboardItem.GameSafeMode, DashboardItem.GameProfiles, DashboardItem.BackgroundServices];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDefaultLayout_HasGamesRightAfterPower(bool supportsItsMode)
    {
        var groups = DashboardGroup.GetDefaultGroups(supportsItsMode);

        Assert.Equal(DashboardGroupType.Power, groups[0].Type);
        Assert.Equal(DashboardGroupType.Games, groups[1].Type);
        Assert.Equal(GameItems, groups[1].Items);
        Assert.Equal("Games", groups[1].GetName());
        Assert.Equal(supportsItsMode, groups[0].Items.Contains(DashboardItem.ItsMode));
    }

    [Fact]
    public void EachItem_IsInTheDefaultLayoutOnce()
    {
        var items = DashboardGroup.GetDefaultGroups(true).SelectMany(g => g.Items).ToList();
        Assert.Equal(items.Count, items.Distinct().Count());
    }

    [Fact]
    public void EveryDashboardItem_HasAnIconAndATitle()
    {
        foreach (var item in Enum.GetValues<DashboardItem>())
        {
            _ = item.GetIcon();
            Assert.False(string.IsNullOrWhiteSpace(item.GetTitle()), $"{item} has no title.");
        }
    }

    [Fact]
    public void GameSafeStatus_SaysWhyItIsOn()
    {
        Assert.Equal(string.Empty, GameStatusText.GameSafeMode(false, ["vgc"], "Valorant"));
        Assert.Equal(string.Format(Resource.SettingsPage_GameSafeMode_Active, "vgc, EasyAntiCheat"), GameStatusText.GameSafeMode(true, ["vgc", "EasyAntiCheat"], null));
        Assert.Equal(string.Format(Resource.SettingsPage_GameSafeMode_ActiveProfile, "Valorant"), GameStatusText.GameSafeMode(true, [], "Valorant"));
    }

    [Fact]
    public void GameProfileStatus_NamesTheGame_AndThePowerModeItsProfileSet()
    {
        Assert.Null(GameStatusText.GameProfile(null, null));
        Assert.Equal("Running: Witcher 3", GameStatusText.GameProfile("Witcher 3", null));
        Assert.Equal("Running: Witcher 3, Performance mode", GameStatusText.GameProfile("Witcher 3", "Performance"));
    }

    [Fact]
    public void BackgroundServicesStatus_CountsWhatIndraKeepsStopped()
    {
        Assert.Null(GameStatusText.BackgroundServices(false, 3));
        Assert.Equal("0 stopped while Indra runs", GameStatusText.BackgroundServices(true, 0));
        Assert.Equal("4 stopped while Indra runs", GameStatusText.BackgroundServices(true, 4));
    }

    [Fact]
    public void AppBehaviour_ShowsTheSameCards_AndHasNoCopyOfTheirCode()
    {
        var folder = Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF", "Controls", "Settings");
        var xaml = File.ReadAllText(Path.Combine(folder, "SettingsAppBehaviorControl.xaml"));
        var code = File.ReadAllText(Path.Combine(folder, "SettingsAppBehaviorControl.xaml.cs"));

        foreach (var card in new[] { "GameDetectionControl", "GameSafeModeControl", "GameProfilesControl", "BackgroundServicesControl" })
            Assert.Contains($"<games:{card} />", xaml);

        foreach (var old in new[] { "DetectionModeComboBox_SelectionChanged", "ExcludeProcesses_Click", "GameSafeModeToggle_Click", "GameProfilesButton_Click", "BackgroundServicesCard_Click", "UpdateGameSafeModeStatus" })
        {
            Assert.DoesNotContain(old, xaml);
            Assert.DoesNotContain(old, code);
        }
    }
}
