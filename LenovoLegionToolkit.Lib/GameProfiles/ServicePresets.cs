using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>
/// A group of Windows services often suggested for stopping, picked in one go. <see cref="Id"/> names its title and
/// note in the app's strings. <see cref="HasWarning"/> marks a preset that turns off something people use (Search,
/// Xbox features); those are not chosen by default.
/// </summary>
public sealed record ServicePreset(string Id, IReadOnlyList<string> Services, bool OnByDefault, bool HasWarning);

/// <summary>
/// The presets LLT offers. They only list services; every one still goes through <see cref="ServiceRules.Check"/>
/// before it is stopped, and none is on the protected list (checked by a test). Stopping them usually changes FPS
/// little or not at all; what they save is background disk, CPU and network activity.
/// </summary>
public static class ServicePresets
{
    public static IReadOnlyList<ServicePreset> All { get; } =
    [
        new("Telemetry", ["DiagTrack", "dmwappushservice", "WerSvc"], OnByDefault: true, HasWarning: false),
        new("SysMain", ["SysMain"], OnByDefault: true, HasWarning: false),
        new("WindowsSearch", ["WSearch"], OnByDefault: false, HasWarning: true),
        new("UnusedExtras", ["MapsBroker", "Fax", "RetailDemo", "RemoteRegistry"], OnByDefault: true, HasWarning: false),
        new("Xbox", ["XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"], OnByDefault: false, HasWarning: true),
    ];
}
