using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Tells whether a folder can be changed by accounts other than SYSTEM, Administrators or TrustedInstaller.
/// LLT runs elevated, so anything it executes or trusts must come from a folder normal programs can't write.
/// </summary>
public static class DirectoryProtection
{
    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData
        | FileSystemRights.AppendData
        | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.WriteAttributes
        | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership
        | (FileSystemRights)0x10000000  // GENERIC_ALL
        | (FileSystemRights)0x40000000; // GENERIC_WRITE

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    /// <summary>
    /// True when the folder containing LLT itself can only be changed by administrators
    /// (for example a normal install under Program Files). False for portable copies in user folders.
    /// </summary>
    public static bool IsProgramDirectoryProtected()
    {
        try
        {
            return !IsWritableByNonAdmins(Folders.Program);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Could not check program directory protection.", ex);
            return false;
        }
    }

    /// <summary>
    /// True if any allow rule that applies to the folder itself grants a write-type right to an account
    /// other than SYSTEM, Administrators or TrustedInstaller. Also true if the folder is a reparse point,
    /// because its contents then live somewhere else.
    /// </summary>
    public static bool IsWritableByNonAdmins(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
            throw new DirectoryNotFoundException(directory);

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return true;

        var rules = info.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;

            // Inherit-only rules apply to children, not to this folder.
            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
                continue;

            if ((rule.FileSystemRights & WriteRights) == 0)
                continue;

            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid.Equals(LocalSystem) || sid.Equals(Administrators) || sid.Equals(TrustedInstaller))
                continue;

            return true;
        }

        return false;
    }
}
