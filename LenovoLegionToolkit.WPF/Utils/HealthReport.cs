using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Utils;

public enum HealthCheck
{
    BatteryAction,
    WindowsUpdate,
    VirusProtection,
    SecurityNotifications,
    MemoryIntegrity,
    DisabledServices,
    GpuDriverErrors,
    DiskSpace,
    BatteryWear
}

public enum HealthState
{
    Ok,
    Attention,
    Info,
    Unknown
}

/// <summary>Where an item's button leads: a Windows settings page, or Indra's own Battery page.</summary>
public enum HealthAction
{
    None,
    PowerOptions,
    WindowsUpdate,
    WindowsSecurity,
    Notifications,
    CoreIsolation,
    Services,
    Storage,
    BatteryPage
}

public sealed record HealthItem(HealthCheck Check, HealthState State, string Title, string Detail, HealthAction Action);

/// <summary>The battery's capacity now and when new, in mWh.</summary>
public sealed record BatteryWearInfo(int DesignCapacity, int FullChargeCapacity);

/// <summary>
/// What the Health page is built from. Each part is read on its own; null means Windows didn't answer, and the
/// page says so instead of guessing.
/// </summary>
public sealed record HealthInputs(
    BatteryActionInfo? BatteryAction,
    UpdatesInfo? Updates,
    VirusProtectionInfo? VirusProtection,
    bool? SecurityNotificationsOff,
    bool? MemoryIntegrityRunning,
    IReadOnlyList<string>? DisabledServices,
    int? GpuDriverErrorsLastDay,
    SystemDriveInfo? SystemDrive,
    BatteryWearInfo? BatteryWear);

/// <summary>Turns the inputs into the Health page's items. Pure: no Windows calls, so every rule can be tested.</summary>
public static class HealthReport
{
    /// <summary>Services Windows and games rely on; each is reported when its start type is Disabled.</summary>
    public static readonly IReadOnlyList<string> WatchedServices =
        ["wuauserv", "BITS", "WinDefend", "mpssvc", "Audiosrv", "AudioEndpointBuilder", "Winmgmt", "EventLog", "XblAuthManager", "XboxGipSvc"];

    internal const int UpdatesTooOldDays = 45;
    internal const double LowDiskFraction = 0.10;
    internal const long LowDiskBytes = 15L * 1024 * 1024 * 1024;
    internal const double WornBatteryPercent = 80;

    public static IReadOnlyList<HealthItem> Evaluate(HealthInputs inputs, DateTime now) =>
    [
        BatteryAction(inputs.BatteryAction),
        WindowsUpdate(inputs.Updates, now),
        VirusProtection(inputs.VirusProtection),
        SecurityNotifications(inputs.SecurityNotificationsOff),
        MemoryIntegrity(inputs.MemoryIntegrityRunning),
        DisabledServices(inputs.DisabledServices),
        GpuDriverErrors(inputs.GpuDriverErrorsLastDay),
        DiskSpace(inputs.SystemDrive),
        BatteryWear(inputs.BatteryWear)
    ];

    private static HealthItem Unknown(HealthCheck check, string title, HealthAction action) =>
        new(check, HealthState.Unknown, title, Resource.Health_Unknown_Detail, action);

    private static HealthItem BatteryAction(BatteryActionInfo? info)
    {
        const HealthCheck check = HealthCheck.BatteryAction;
        var title = Resource.Health_BatteryAction_Title;
        if (info is null)
            return Unknown(check, title, HealthAction.PowerOptions);

        return info.CriticalActionOnBattery switch
        {
            WindowsHealth.ActionHibernate when !info.HibernateAvailable =>
                new(check, HealthState.Attention, title, string.Format(Resource.Health_BatteryAction_HibernateOff, info.CriticalLevelPercent), HealthAction.PowerOptions),
            WindowsHealth.ActionNothing =>
                new(check, HealthState.Attention, title, string.Format(Resource.Health_BatteryAction_Nothing, info.CriticalLevelPercent), HealthAction.PowerOptions),
            _ => new(check, HealthState.Ok, title, string.Format(Resource.Health_BatteryAction_Ok, info.CriticalLevelPercent, info.CriticalActionOnBattery switch
            {
                WindowsHealth.ActionSleep => Resource.Health_BatteryAction_Sleep,
                WindowsHealth.ActionHibernate => Resource.Health_BatteryAction_Hibernate,
                _ => Resource.Health_BatteryAction_ShutDown
            }), HealthAction.PowerOptions)
        };
    }

    private static HealthItem WindowsUpdate(UpdatesInfo? info, DateTime now)
    {
        const HealthCheck check = HealthCheck.WindowsUpdate;
        var title = Resource.Health_Updates_Title;
        if (info is null)
            return Unknown(check, title, HealthAction.WindowsUpdate);

        int? days = info.LastInstalledDate is { } last ? Math.Max(0, (int)(now.Date - last.Date).TotalDays) : null;
        var lastText = days is { } d ? string.Format(Resource.Health_Updates_LastDays, d) : Resource.Health_Updates_LastUnknown;

        if (info.AutomaticUpdatesOff)
            return new(check, HealthState.Attention, title, string.Format(Resource.Health_Updates_Off, lastText), HealthAction.WindowsUpdate);

        if (days > UpdatesTooOldDays)
            return new(check, HealthState.Attention, title, string.Format(Resource.Health_Updates_Old, days), HealthAction.WindowsUpdate);

        return new(check, HealthState.Ok, title, string.Format(Resource.Health_Updates_Ok, lastText), HealthAction.WindowsUpdate);
    }

    private static HealthItem VirusProtection(VirusProtectionInfo? info)
    {
        const HealthCheck check = HealthCheck.VirusProtection;
        var title = Resource.Health_Virus_Title;
        if (info is null)
            return Unknown(check, title, HealthAction.WindowsSecurity);

        if (info.DefenderRealTimeOn)
            return new(check, HealthState.Ok, title, Resource.Health_Virus_Defender, HealthAction.WindowsSecurity);

        return info.OtherAntivirusOn.Count > 0
            ? new(check, HealthState.Ok, title, string.Format(Resource.Health_Virus_Other, string.Join(", ", info.OtherAntivirusOn)), HealthAction.WindowsSecurity)
            : new(check, HealthState.Attention, title, Resource.Health_Virus_Off, HealthAction.WindowsSecurity);
    }

    private static HealthItem SecurityNotifications(bool? off) => off switch
    {
        null => Unknown(HealthCheck.SecurityNotifications, Resource.Health_Notifications_Title, HealthAction.Notifications),
        true => new(HealthCheck.SecurityNotifications, HealthState.Attention, Resource.Health_Notifications_Title, Resource.Health_Notifications_Off, HealthAction.Notifications),
        false => new(HealthCheck.SecurityNotifications, HealthState.Ok, Resource.Health_Notifications_Title, Resource.Health_Notifications_Ok, HealthAction.Notifications)
    };

    private static HealthItem MemoryIntegrity(bool? running) => running switch
    {
        null => Unknown(HealthCheck.MemoryIntegrity, Resource.Health_MemoryIntegrity_Title, HealthAction.CoreIsolation),
        true => new(HealthCheck.MemoryIntegrity, HealthState.Info, Resource.Health_MemoryIntegrity_Title, Resource.Health_MemoryIntegrity_On, HealthAction.CoreIsolation),
        false => new(HealthCheck.MemoryIntegrity, HealthState.Info, Resource.Health_MemoryIntegrity_Title, Resource.Health_MemoryIntegrity_Off, HealthAction.CoreIsolation)
    };

    private static HealthItem DisabledServices(IReadOnlyList<string>? disabled)
    {
        const HealthCheck check = HealthCheck.DisabledServices;
        var title = Resource.Health_Services_Title;
        if (disabled is null)
            return Unknown(check, title, HealthAction.Services);

        if (disabled.Count == 0)
            return new(check, HealthState.Ok, title, Resource.Health_Services_Ok, HealthAction.Services);

        var names = disabled.Select(name => Resource.ResourceManager.GetString($"Health_Service_{name}") ?? name);
        return new(check, HealthState.Attention, title, string.Format(Resource.Health_Services_Disabled, string.Join("; ", names)), HealthAction.Services);
    }

    private static HealthItem GpuDriverErrors(int? count) => count switch
    {
        null => Unknown(HealthCheck.GpuDriverErrors, Resource.Health_Gpu_Title, HealthAction.None),
        0 => new(HealthCheck.GpuDriverErrors, HealthState.Ok, Resource.Health_Gpu_Title, Resource.Health_Gpu_Ok, HealthAction.None),
        _ => new(HealthCheck.GpuDriverErrors, HealthState.Attention, Resource.Health_Gpu_Title, string.Format(Resource.Health_Gpu_Errors, count), HealthAction.None)
    };

    private static HealthItem DiskSpace(SystemDriveInfo? drive)
    {
        const HealthCheck check = HealthCheck.DiskSpace;
        var title = Resource.Health_Disk_Title;
        if (drive is null || drive.TotalBytes <= 0)
            return Unknown(check, title, HealthAction.Storage);

        var free = Gb(drive.FreeBytes);
        var total = Gb(drive.TotalBytes);
        var low = drive.FreeBytes < LowDiskBytes || (double)drive.FreeBytes / drive.TotalBytes < LowDiskFraction;

        return low
            ? new(check, HealthState.Attention, title, string.Format(Resource.Health_Disk_Low, free, total, drive.Name), HealthAction.Storage)
            : new(check, HealthState.Ok, title, string.Format(Resource.Health_Disk_Ok, free, total, drive.Name), HealthAction.Storage);
    }

    private static HealthItem BatteryWear(BatteryWearInfo? battery)
    {
        const HealthCheck check = HealthCheck.BatteryWear;
        var title = Resource.Health_Battery_Title;
        if (battery is null || battery.DesignCapacity <= 0 || battery.FullChargeCapacity <= 0)
            return Unknown(check, title, HealthAction.BatteryPage);

        var percent = Math.Round(100.0 * battery.FullChargeCapacity / battery.DesignCapacity, 1);
        var shown = percent.ToString("0.#", CultureInfo.CurrentCulture);

        return percent < WornBatteryPercent
            ? new(check, HealthState.Attention, title, string.Format(Resource.Health_Battery_Worn, shown), HealthAction.BatteryPage)
            : new(check, HealthState.Info, title, string.Format(Resource.Health_Battery_Ok, shown), HealthAction.BatteryPage);
    }

    private static string Gb(long bytes) => (bytes / 1024d / 1024 / 1024).ToString("0", CultureInfo.CurrentCulture);
}

/// <summary>Which items to notify about: only those that newly need attention, never the same one twice in a row.</summary>
public static class HealthNotifier
{
    /// <summary>GPU driver errors are already notified the moment they happen (Round 14), so they aren't repeated here.</summary>
    private static readonly HealthCheck[] NotNotified = [HealthCheck.GpuDriverErrors];

    /// <summary>
    /// <paramref name="notifiedBefore"/> are the items notified and still unresolved. Returns the items to notify now,
    /// and the new notified set: items that recovered are dropped (so they notify again if they come back), while an
    /// item that couldn't be checked this time keeps its place.
    /// </summary>
    public static (IReadOnlyList<HealthItem> ToNotify, IReadOnlyList<HealthCheck> Notified) Next(IReadOnlyCollection<HealthCheck> notifiedBefore, IReadOnlyList<HealthItem> items)
    {
        var attention = items.Where(i => i.State == HealthState.Attention && !NotNotified.Contains(i.Check)).ToList();
        var unknown = items.Where(i => i.State == HealthState.Unknown).Select(i => i.Check).ToHashSet();

        var toNotify = attention.Where(i => !notifiedBefore.Contains(i.Check)).ToList();
        var notified = attention.Select(i => i.Check)
            .Concat(notifiedBefore.Where(unknown.Contains))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        return (toNotify, notified);
    }
}
