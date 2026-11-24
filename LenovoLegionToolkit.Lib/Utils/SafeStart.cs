using System;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Whether this run is a safe start (<see cref="StartupGuard"/>): the previous start didn't finish, so the hardware
/// settings normally applied at start-up (God Mode, ITS mode, GPU overclock and P-state lock, AMD overclocking, the
/// first fan-curve apply, automations' run-on-start) are skipped once. Set once, before anything is applied.
/// </summary>
public static class SafeStart
{
    /// <summary>True when this start skips the hardware settings.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>When the start that didn't finish began (local time), if known.</summary>
    public static DateTime? UnfinishedStartedAt { get; private set; }

    public static void Activate(DateTime? unfinishedStartedAt)
    {
        IsActive = true;
        UnfinishedStartedAt = unfinishedStartedAt;
    }

    /// <summary>Logs and returns true when <paramref name="what"/> is skipped because of a safe start.</summary>
    public static bool Skips(string what)
    {
        if (IsActive)
            Log.Instance.Trace($"Safe start: not applying {what}.");
        return IsActive;
    }
}
