using System.Collections.Generic;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Controls.Games;

/// <summary>The live status lines of the Games cards, from plain values so they can be tested.</summary>
public static class GameStatusText
{
    /// <summary>Empty while game-safe mode is off; otherwise the anti-cheat services that turned it on, or the game whose profile did.</summary>
    public static string GameSafeMode(bool isActive, IReadOnlyCollection<string> antiCheatServices, string? activeGameName) =>
        !isActive
            ? string.Empty
            : antiCheatServices.Count > 0
                ? string.Format(Resource.SettingsPage_GameSafeMode_Active, string.Join(", ", antiCheatServices))
                : string.Format(Resource.SettingsPage_GameSafeMode_ActiveProfile, activeGameName);

    /// <summary>The game whose profile is in effect, with the power mode the profile set; null when no game runs.</summary>
    public static string? GameProfile(string? activeGameName, string? powerModeName) =>
        activeGameName is null
            ? null
            : powerModeName is null
                ? string.Format(Resource.GamesCard_Running, activeGameName)
                : string.Format(Resource.GamesCard_RunningWithPowerMode, activeGameName, powerModeName);

    /// <summary>How many services Indra keeps stopped right now; null when the feature is off.</summary>
    public static string? BackgroundServices(bool enabled, int stoppedByIndra) =>
        enabled ? string.Format(Resource.GamesCard_ServicesStopped, stoppedByIndra) : null;
}
