using System;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameProfiles;

/// <summary>The one mapping between the detection list and its three settings, used by both places the card appears.</summary>
public class GameDetectionModeTests
{
    [Theory]
    [InlineData(GameDetectionMode.Auto, true, true, true)]
    [InlineData(GameDetectionMode.DiscreteGpu, true, false, false)]
    [InlineData(GameDetectionMode.GameList, false, true, false)]
    [InlineData(GameDetectionMode.GameMode, false, false, true)]
    [InlineData(GameDetectionMode.Manual, false, false, false)]
    public void EachMode_SetsItsSettings_AndIsReadBack(GameDetectionMode mode, bool gpu, bool gameList, bool gameMode)
    {
        var settings = new ApplicationSettings.GameDetectionSettings();

        GameDetectionModes.Apply(mode, settings);

        Assert.Equal((gpu, gameList, gameMode), (settings.UseDiscreteGPU, settings.UseGameConfigStore, settings.UseEffectiveGameMode));
        Assert.Equal(mode, GameDetectionModes.From(settings));
    }

    [Fact]
    public void EveryModeIsCovered()
    {
        foreach (var mode in Enum.GetValues<GameDetectionMode>())
        {
            var settings = new ApplicationSettings.GameDetectionSettings();
            GameDetectionModes.Apply(mode, settings);
            Assert.Equal(mode, GameDetectionModes.From(settings));
        }
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void ACombinationNoModeOffers_CountsAsAuto(bool gpu, bool gameList, bool gameMode) =>
        Assert.Equal(GameDetectionMode.Auto, GameDetectionModes.From(new ApplicationSettings.GameDetectionSettings
        {
            UseDiscreteGPU = gpu,
            UseGameConfigStore = gameList,
            UseEffectiveGameMode = gameMode
        }));
}
