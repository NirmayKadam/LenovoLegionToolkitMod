using System.Collections.Generic;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>One step of the first-run tour, and the page (and Settings category) its "Show me" opens.</summary>
public sealed record TourStep(SymbolRegular Icon, string Title, string Text, string PageTag, string? SettingsTab = null);

/// <summary>The first-run tour's steps, and when it opens by itself.</summary>
public static class TourSteps
{
    public static IReadOnlyList<TourStep> All =>
    [
        new(SymbolRegular.Gauge24, Resource.Tour_PowerModes_Title, Resource.Tour_PowerModes_Text, "dashboard"),
        new(SymbolRegular.Grid24, Resource.Tour_Dashboard_Title, Resource.Tour_Dashboard_Text, "dashboard"),
        new(SymbolRegular.WrenchScrewdriver24, Resource.Tour_GodMode_Title, Resource.Tour_GodMode_Text, "dashboard"),
        new(SymbolRegular.WeatherDuststorm24, Resource.Tour_FanCurves_Title, Resource.Tour_FanCurves_Text, "settings", "Power"),
        new(SymbolRegular.ProjectionScreen24, Resource.Tour_Osd_Title, Resource.Tour_Osd_Text, "settings", "AppBehavior"),
        new(SymbolRegular.ShieldCheckmark24, Resource.Tour_GameSafe_Title, Resource.Tour_GameSafe_Text, "settings", "AppBehavior"),
        new(SymbolRegular.Settings24, Resource.Tour_Settings_Title, Resource.Tour_Settings_Text, "settings", "AppBehavior"),
    ];

    /// <summary>
    /// The tour opens by itself once: on a normal start (not a tray or autostart start) when it hasn't been shown.
    /// </summary>
    public static bool ShouldShowAtStart(bool tourShown, bool startedMinimized) => !tourShown && !startedMinimized;
}
