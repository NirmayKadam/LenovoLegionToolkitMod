using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Automation.Utils;

public enum RunTargetProblem
{
    None,
    /// <summary>No folder given: Windows searches for the program, including folders others may change.</summary>
    BareName,
    NotFound,
    /// <summary>The program, or the folder it is in, can be changed by programs that aren't elevated.</summary>
    Replaceable
}

public readonly record struct RunTargetResult(RunTargetProblem Problem, string? Path);

/// <summary>
/// Checks whether the program a Run step starts (as administrator, because LLT runs elevated) could be replaced
/// by a program that isn't elevated. Only reads permissions; never changes them. Only the program itself is
/// checked, not files named in its arguments (for example the script in "cmd /c script.bat").
/// </summary>
public static class RunTargetCheck
{
    private const FileSystemRights FileWriteRights =
        FileSystemRights.WriteData | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>CreateFiles lets a program be placed next to it (for example a DLL it loads); DeleteSubdirectoriesAndFiles lets it be swapped.</summary>
    private const FileSystemRights FolderWriteRights =
        FileSystemRights.CreateFiles | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>GENERIC_WRITE and GENERIC_ALL, which can appear unmapped in a rule.</summary>
    private const int GenericWriteOrAll = 0x40000000 | 0x10000000;

    public static RunTargetResult Check(string? scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            return new(RunTargetProblem.None, null);

        var path = scriptPath.Trim().Trim('"');

        try
        {
            if (!Path.IsPathFullyQualified(path))
                return new(RunTargetProblem.BareName, path);

            if (!File.Exists(path))
                return new(RunTargetProblem.NotFound, path);

            var folder = Path.GetDirectoryName(path);
            var others = NonElevatedPrincipals();

            var file = new FileInfo(path).GetAccessControl();
            if (GrantsWrite(file.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier,
                    file.GetAccessRules(true, true, typeof(SecurityIdentifier)), FileWriteRights, others))
                return new(RunTargetProblem.Replaceable, folder);

            if (folder is not null)
            {
                var directory = new DirectoryInfo(folder).GetAccessControl();
                if (GrantsWrite(directory.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier,
                        directory.GetAccessRules(true, true, typeof(SecurityIdentifier)), FolderWriteRights, others))
                    return new(RunTargetProblem.Replaceable, folder);
            }

            return new(RunTargetProblem.None, path);
        }
        catch (Exception ex)
        {
            // Permissions that can't be read can't be judged; say nothing rather than guess.
            Log.Instance.Trace($"Could not check the permissions of {path}.", ex);
            return new(RunTargetProblem.None, path);
        }
    }

    public static string? Describe(RunTargetResult result) => result.Problem switch
    {
        RunTargetProblem.BareName => Resource.RunTarget_BareName,
        RunTargetProblem.NotFound => string.Format(Resource.RunTarget_NotFound, result.Path),
        RunTargetProblem.Replaceable => string.Format(Resource.RunTarget_Replaceable, result.Path),
        _ => null
    };

    /// <summary>
    /// True when one of <paramref name="others"/> owns the object (an owner can always change its permissions)
    /// or has an allow rule with one of <paramref name="writeRights"/> that applies to the object itself.
    /// Deny rules are ignored, so a rare deny can make this say "writable" when it isn't; never the reverse.
    /// </summary>
    internal static bool GrantsWrite(SecurityIdentifier? owner, AuthorizationRuleCollection rules, FileSystemRights writeRights, IReadOnlySet<SecurityIdentifier> others)
    {
        if (owner is not null && others.Contains(owner))
            return true;

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;

            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
                continue;

            if (rule.IdentityReference is not SecurityIdentifier sid || !others.Contains(sid))
                continue;

            if ((rule.FileSystemRights & writeRights) != 0 || ((int)rule.FileSystemRights & GenericWriteOrAll) != 0)
                return true;
        }

        return false;
    }

    /// <summary>Groups every program a user starts normally belongs to, plus the user. Administrators and SYSTEM are not among them.</summary>
    internal static IReadOnlySet<SecurityIdentifier> NonElevatedPrincipals()
    {
        var set = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.WorldSid, null),
            new(WellKnownSidType.AuthenticatedUserSid, null),
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.InteractiveSid, null),
        };

        if (WindowsIdentity.GetCurrent().User is { } user)
            set.Add(user);

        return set;
    }
}
