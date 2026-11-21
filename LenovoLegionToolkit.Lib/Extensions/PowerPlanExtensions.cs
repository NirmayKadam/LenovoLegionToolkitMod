using System;
using System.Runtime.InteropServices;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class PowerPlanExtensions
{
    private static readonly Guid BalancedBaseGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid NoSubGroupGuid = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid PersonalityGuid = new("245d8541-3943-4422-b025-13a784f679b7");

    private const uint PersonalityBalanced = 2;

    [DllImport("PowrProf.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("PowrProf.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint valueIndex);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static bool IsPlanBasedOnBalanced(Guid schemeGuid)
    {
        var subGroup = NoSubGroupGuid;
        var personalitySetting = PersonalityGuid;
        var result = PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref personalitySetting, out var personality);
        if (result == 0)
            return personality == PersonalityBalanced;

        return schemeGuid == BalancedBaseGuid;
    }

    public static bool IsActivePlanBasedOnBalanced() => GetActiveSchemeGuid() is { } active && IsPlanBasedOnBalanced(active);

    /// <summary>
    /// The active power plan's GUID, or null when Windows doesn't answer. Windows allocates the memory the GUID is
    /// returned in; it is freed here, every time (callers used to read it and never free it, a small leak per read).
    /// </summary>
    public static Guid? GetActiveSchemeGuid()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (result != 0 || pointer == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }
}
