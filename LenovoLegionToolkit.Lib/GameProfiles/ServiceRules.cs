using System;
using System.Collections.Frozen;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>What LLT knows about a Windows service when deciding whether a game profile may pause it.</summary>
/// <param name="Name">The service's name (not its display name).</param>
/// <param name="DisplayName">Shown in the window.</param>
/// <param name="IsDriver">A kernel or file-system driver rather than a program.</param>
/// <param name="Running">Running (or starting) right now.</param>
/// <param name="CanStop">Windows says the service accepts a stop request.</param>
/// <param name="HasRunningDependents">Other running services depend on it.</param>
public sealed record ServiceInfo(string Name, string DisplayName, bool IsDriver, bool Running, bool CanStop, bool HasRunningDependents);

/// <summary>Why a service can't be paused by a game profile.</summary>
public enum ServiceRefusal
{
    /// <summary>On LLT's fixed list of services Windows, the hardware, security or games depend on.</summary>
    Protected,

    /// <summary>A driver, not a program.</summary>
    Driver,

    /// <summary>Not installed.</summary>
    NotInstalled,

    /// <summary>Not running, so there is nothing to pause.</summary>
    NotRunning,

    /// <summary>Windows says it can't be stopped.</summary>
    CannotStop,

    /// <summary>Other running services need it; stopping it would stop them too.</summary>
    HasRunningDependents,
}

/// <summary>
/// The rules for pausing a service during a game. A fixed deny list protects what Windows, the laptop's hardware,
/// security, Windows Update, input and display, NVIDIA's display driver, Lenovo's own services and anti-cheats need.
/// It can't be changed from the UI. LLT also never stops a driver or a service other running services depend on.
/// </summary>
public static class ServiceRules
{
    public static readonly FrozenSet<string> Protected = new[]
    {
        // Windows core
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "KeyIso", "Winmgmt", "EventLog", "PlugPlay", "Power",
        "ProfSvc", "Schedule", "UserManager", "BrokerInfrastructure", "SystemEventsBroker", "CoreMessagingRegistrar",
        "StateRepository", "TimeBrokerSvc", "CryptSvc", "gpsvc", "StorSvc", "VSS",
        // Audio
        "Audiosrv", "AudioEndpointBuilder",
        // Network
        "Dhcp", "Dnscache", "NlaSvc", "nsi", "netprofm", "LanmanWorkstation", "WlanSvc", "Wcmsvc", "BFE", "mpssvc",
        "WinHttpAutoProxySvc",
        // Security
        "WinDefend", "WdNisSvc", "Sense", "SecurityHealthService", "wscsvc",
        // Windows Update core
        "wuauserv", "UsoSvc", "WaaSMedicSvc", "TrustedInstaller", "BITS",
        // Input and display
        "hidserv", "TabletInputService", "TextInputManagementService", "DispBrokerDesktopSvc",
        // NVIDIA
        "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem",
        // Gaming and printing
        "GamingServices", "GamingServicesNet", "Spooler",
        // Lenovo
        "ImControllerService", "LenovoFnAndFunctionKeys", "LITSSVC", "LenovoVantageService",
        // Anti-cheat
        "vgc", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when the service may be paused now; otherwise why not. <paramref name="service"/> null means not installed.</summary>
    public static ServiceRefusal? Check(string name, ServiceInfo? service)
    {
        if (Protected.Contains(name))
            return ServiceRefusal.Protected;
        if (service is null)
            return ServiceRefusal.NotInstalled;
        if (service.IsDriver)
            return ServiceRefusal.Driver;
        if (!service.Running)
            return ServiceRefusal.NotRunning;
        if (!service.CanStop)
            return ServiceRefusal.CannotStop;
        if (service.HasRunningDependents)
            return ServiceRefusal.HasRunningDependents;
        return null;
    }

    /// <summary>Whether a service can be added to a profile at all (it may simply not be running yet).</summary>
    public static ServiceRefusal? CheckForList(string name, ServiceInfo? service) =>
        Check(name, service) is { } refusal && refusal != ServiceRefusal.NotRunning ? refusal : null;
}
