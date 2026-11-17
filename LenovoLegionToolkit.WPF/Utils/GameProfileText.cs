using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>The words for game profile results, shared by the notification and the Game profiles window.</summary>
public static class GameProfileText
{
    public static string Summary(GameProfileReport report)
    {
        var text = report.Started
            ? string.Format(Resource.GameProfiles_Summary_On, report.GameName, report.AppsEased, report.AppsClosed, report.AppsLeftOpen, report.ServicesPaused)
            : string.Format(Resource.GameProfiles_Summary_Off, report.GameName, report.ServicesPaused, report.AppsEased);

        return report.Problems > 0
            ? $"{text} {string.Format(Resource.GameProfiles_Summary_Problems, report.Problems)}"
            : text;
    }

    public static string Refusal(ServiceRefusal refusal) => refusal switch
    {
        ServiceRefusal.Protected => Resource.GameProfiles_Refusal_Protected,
        ServiceRefusal.Driver => Resource.GameProfiles_Refusal_Driver,
        ServiceRefusal.NotInstalled => Resource.GameProfiles_Refusal_NotInstalled,
        ServiceRefusal.NotRunning => Resource.GameProfiles_Refusal_NotRunning,
        ServiceRefusal.CannotStop => Resource.GameProfiles_Refusal_CannotStop,
        _ => Resource.GameProfiles_Refusal_Dependents,
    };
}
