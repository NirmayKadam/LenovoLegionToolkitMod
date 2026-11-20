using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>How a running game is recognised: the choices offered for the three game detection settings.</summary>
public enum GameDetectionMode
{
    /// <summary>All three signals.</summary>
    Auto,
    /// <summary>A program using the discrete GPU.</summary>
    DiscreteGpu,
    /// <summary>Windows' own list of games.</summary>
    GameList,
    /// <summary>Windows Game Mode turning on.</summary>
    GameMode,
    /// <summary>None: only the games and apps you list.</summary>
    Manual
}

public static class GameDetectionModes
{
    /// <summary>The mode the settings describe. A combination no mode offers counts as <see cref="GameDetectionMode.Auto"/>.</summary>
    public static GameDetectionMode From(ApplicationSettings.GameDetectionSettings settings) =>
        (settings.UseDiscreteGPU, settings.UseGameConfigStore, settings.UseEffectiveGameMode) switch
        {
            (true, false, false) => GameDetectionMode.DiscreteGpu,
            (false, true, false) => GameDetectionMode.GameList,
            (false, false, true) => GameDetectionMode.GameMode,
            (false, false, false) => GameDetectionMode.Manual,
            _ => GameDetectionMode.Auto
        };

    /// <summary>Sets the three settings for <paramref name="mode"/>.</summary>
    public static void Apply(GameDetectionMode mode, ApplicationSettings.GameDetectionSettings settings)
    {
        (settings.UseDiscreteGPU, settings.UseGameConfigStore, settings.UseEffectiveGameMode) = mode switch
        {
            GameDetectionMode.DiscreteGpu => (true, false, false),
            GameDetectionMode.GameList => (false, true, false),
            GameDetectionMode.GameMode => (false, false, true),
            GameDetectionMode.Manual => (false, false, false),
            _ => (true, true, true)
        };
    }
}
