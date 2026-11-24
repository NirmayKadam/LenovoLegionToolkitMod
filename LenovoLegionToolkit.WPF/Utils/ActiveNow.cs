using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>What Indra is doing in the background right now, as the dashboard's "Active now" strip shows it.</summary>
/// <param name="Games">The games game detection counts as running.</param>
/// <param name="AiBoost">AI mode has the firmware's game boost on.</param>
/// <param name="GameSafe">Game-safe mode is on.</param>
/// <param name="GameProfile">The game whose profile is in effect, or null.</param>
/// <param name="FanCurveMode">The power mode whose own fan curve is in use, or null (the firmware's curve).</param>
/// <param name="ServicesStopped">Background services Indra keeps stopped.</param>
/// <param name="Afterburner">MSI Afterburner runs, so Indra leaves GPU clocks to it.</param>
/// <param name="SafeStart">This start skipped the hardware settings (the last start didn't finish).</param>
public sealed record ActiveNowState(
    IReadOnlyList<string> Games,
    bool AiBoost,
    bool GameSafe,
    string? GameProfile,
    string? FanCurveMode,
    int ServicesStopped,
    bool Afterburner,
    bool SafeStart);

/// <summary>One chip in the strip: a short text and a tooltip that says what it means and where to change it.</summary>
public sealed record ActiveNowChip(string Key, string Text, string ToolTip);

public static class ActiveNow
{
    /// <summary>The chips to show, in a fixed order; none when nothing is going on.</summary>
    public static IReadOnlyList<ActiveNowChip> Build(ActiveNowState state)
    {
        var chips = new List<ActiveNowChip>();

        if (state.SafeStart)
            chips.Add(new("SafeStart", Resource.ActiveNow_SafeStart, Resource.ActiveNow_SafeStart_Tip));

        var games = state.Games.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (games.Count > 0)
            chips.Add(new("Game", string.Format(Resource.ActiveNow_Game, string.Join(", ", games)), Resource.ActiveNow_Game_Tip));

        if (!string.IsNullOrWhiteSpace(state.GameProfile))
            chips.Add(new("GameProfile", string.Format(Resource.ActiveNow_GameProfile, state.GameProfile), Resource.ActiveNow_GameProfile_Tip));

        if (state.GameSafe)
            chips.Add(new("GameSafe", Resource.ActiveNow_GameSafe, Resource.ActiveNow_GameSafe_Tip));

        if (state.AiBoost)
            chips.Add(new("AiBoost", Resource.ActiveNow_AiBoost, Resource.ActiveNow_AiBoost_Tip));

        if (!string.IsNullOrWhiteSpace(state.FanCurveMode))
            chips.Add(new("FanCurve", string.Format(Resource.ActiveNow_FanCurve, state.FanCurveMode), Resource.ActiveNow_FanCurve_Tip));

        if (state.ServicesStopped > 0)
            chips.Add(new("Services", string.Format(Resource.ActiveNow_Services, state.ServicesStopped), Resource.ActiveNow_Services_Tip));

        if (state.Afterburner)
            chips.Add(new("Afterburner", Resource.ActiveNow_Afterburner, Resource.ActiveNow_Afterburner_Tip));

        return chips;
    }

    /// <summary>The chips as one line ("Game: witcher3 · AI mode boost"), for the tray's hover window; null when there are none.</summary>
    public static string? Line(IReadOnlyList<ActiveNowChip> chips) =>
        chips.Count == 0 ? null : string.Join(" · ", chips.Select(c => c.Text));
}
