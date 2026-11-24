using System.Linq;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The dashboard's "Active now" strip. On 2026-11-23 AI mode stayed boosted for 1.5 hours because of a false game and
/// nothing on screen showed it; the strip shows what runs in the background.
/// </summary>
public class ActiveNowTests
{
    private static readonly ActiveNowState Quiet = new([], false, false, null, null, 0, false, false);

    [Fact]
    public void WhenNothingIsGoingOn_ThereAreNoChips() => Assert.Empty(ActiveNow.Build(Quiet));

    [Fact]
    public void EachState_ShowsItsChip()
    {
        Assert.Equal(["SafeStart"], Keys(Quiet with { SafeStart = true }));
        Assert.Equal(["Game"], Keys(Quiet with { Games = ["witcher3"] }));
        Assert.Equal(["GameProfile"], Keys(Quiet with { GameProfile = "Game mode: witcher3" }));
        Assert.Equal(["GameSafe"], Keys(Quiet with { GameSafe = true }));
        Assert.Equal(["AiBoost"], Keys(Quiet with { AiBoost = true }));
        Assert.Equal(["FanCurve"], Keys(Quiet with { FanCurveMode = "Balance" }));
        Assert.Equal(["Services"], Keys(Quiet with { ServicesStopped = 3 }));
        Assert.Equal(["Afterburner"], Keys(Quiet with { Afterburner = true }));
    }

    [Fact]
    public void TheChips_AreAlwaysInTheSameOrder()
    {
        var everything = new ActiveNowState(["witcher3"], true, true, "Game mode: witcher3", "Balance", 2, true, true);

        Assert.Equal(["SafeStart", "Game", "GameProfile", "GameSafe", "AiBoost", "FanCurve", "Services", "Afterburner"], Keys(everything));
    }

    [Fact]
    public void Games_AreListedOnce_WithoutBlanks()
    {
        var chip = ActiveNow.Build(Quiet with { Games = ["witcher3", "Witcher3", " ", "Heaven"] }).Single();

        Assert.Equal(string.Format(Resource.ActiveNow_Game, "witcher3, Heaven"), chip.Text);
    }

    [Fact]
    public void TheTexts_SayWhatIsActive()
    {
        var chips = ActiveNow.Build(Quiet with { FanCurveMode = "Performance", ServicesStopped = 4, GameProfile = "VALORANT" });

        Assert.Contains(chips, c => c.Text == string.Format(Resource.ActiveNow_FanCurve, "Performance"));
        Assert.Contains(chips, c => c.Text == string.Format(Resource.ActiveNow_Services, 4));
        Assert.Contains(chips, c => c.Text == string.Format(Resource.ActiveNow_GameProfile, "VALORANT"));
        Assert.All(chips, c => Assert.False(string.IsNullOrWhiteSpace(c.ToolTip)));
    }

    [Fact]
    public void TheTrayLine_JoinsTheChipsInOrder_OrIsEmpty()
    {
        Assert.Null(ActiveNow.Line(ActiveNow.Build(Quiet)));

        var line = ActiveNow.Line(ActiveNow.Build(Quiet with { Games = ["witcher3"], AiBoost = true }));
        Assert.Equal(string.Format(Resource.ActiveNow_Game, "witcher3") + " · " + Resource.ActiveNow_AiBoost, line);
    }

    private static string[] Keys(ActiveNowState state) => [.. ActiveNow.Build(state).Select(c => c.Key)];
}
