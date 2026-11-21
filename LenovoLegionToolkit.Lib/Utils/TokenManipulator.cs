using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Security;
using Microsoft.Win32.SafeHandles;

namespace LenovoLegionToolkit.Lib.Utils;

public static class TokenManipulator
{
    public const string SE_BACKUP_PRIVILEGE = "SeBackupPrivilege";
    public const string SE_RESTORE_PRIVILEGE = "SeRestorePrivilege";
    public const string SE_TAKE_OWNERSHIP_PRIVILEGE = "SeTakeOwnershipPrivilege";
    public const string SE_SYSTEM_ENVIRONMENT_PRIVILEGE = "SeSystemEnvironmentPrivilege";

    private static readonly Lock HoldersLock = new();
    private static readonly Dictionary<string, int> Holders = [];

    /// <summary>
    /// Turns the privileges on for the caller, who must call <see cref="RemovePrivileges"/> afterwards (also when this
    /// returns false). Counted per privilege: when two operations overlap (a UEFI read and a boot logo write), the
    /// privilege stays on until the last one is done.
    /// </summary>
    public static bool AddPrivileges(params string[] privileges)
    {
        lock (HoldersLock)
        {
            foreach (var privilege in privileges)
                Holders[privilege] = Holders.GetValueOrDefault(privilege) + 1;

            return AdjustPrivileges(privileges, true);
        }
    }

    /// <summary>
    /// Turns the privileges off again once no one holds them. This used to turn them on (a copy-paste slip), so every
    /// privilege Indra ever needed stayed on for as long as it ran.
    /// </summary>
    public static bool RemovePrivileges(params string[] privileges)
    {
        lock (HoldersLock)
        {
            var released = new List<string>();
            foreach (var privilege in privileges)
            {
                var holders = Holders.GetValueOrDefault(privilege);
                if (holders > 1)
                {
                    Holders[privilege] = holders - 1;
                    continue;
                }

                Holders.Remove(privilege);
                released.Add(privilege);
            }

            return released.Count == 0 || AdjustPrivileges([.. released], false);
        }
    }

    private static bool AdjustPrivileges(string[] privileges, bool enable)
    {
        SafeProcessHandle? safeHandle = null;
        SafeFileHandle? safeTokenHandle = null;

        try
        {
            safeHandle = new SafeProcessHandle(PInvoke.GetCurrentProcess(), true);
            if (!PInvoke.OpenProcessToken(safeHandle, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, out safeTokenHandle) && safeTokenHandle is null)
                return false;

            return privileges.All(p => AdjustPrivilege(safeTokenHandle, p, enable));
        }
        finally
        {
            safeTokenHandle?.Dispose();
            safeHandle?.Dispose();
        }
    }

    private static unsafe bool AdjustPrivilege(SafeFileHandle safeTokenHandle, string privilegeName, bool enable)
    {
        PInvoke.LookupPrivilegeValue(null, privilegeName, out var luid);

        var state = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
        state.Privileges[0] = new LUID_AND_ATTRIBUTES
        {
            Luid = luid,
            Attributes = enable ? TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED : 0
        };

        return PInvoke.AdjustTokenPrivileges(new Windows.Win32.Foundation.HANDLE(safeTokenHandle.DangerousGetHandle()), false, &state, 0, null, null);
    }
}
