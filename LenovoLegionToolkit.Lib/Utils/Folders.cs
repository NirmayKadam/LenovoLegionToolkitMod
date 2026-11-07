using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LenovoLegionToolkit.Lib.Utils;

public static class Folders
{
    internal const string MIGRATION_MARKER = ".migrated";
    private const string PLUGIN_CONFIGS = "Configs";

    private static readonly object SettingsLock = new();
    private static string? _settings;

    public static void EnsureParentDirectoryExists(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var folderPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
    }

    public static string Program => AppDomain.CurrentDomain.SetupInformation.ApplicationBase ?? string.Empty;

    /// <summary>
    /// Per-user folder that any program running as the user can write. Only for data LLT writes but
    /// never trusts, such as logs. Settings live in <see cref="Settings"/>.
    /// </summary>
    public static string AppData
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folderPath = Path.Combine(appData, "LenovoLegionToolkit");
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }
    }

    /// <summary>
    /// Where settings are stored. For an installed, elevated LLT this is
    /// &lt;install folder&gt;\UserData\&lt;user SID&gt;, which only administrators can change, so a normal
    /// program can't plant "Run program" automation steps, macros or flags that the elevated app would act on.
    /// Portable copies and non-elevated (debug) runs fall back to <see cref="AppData"/>.
    /// </summary>
    public static string Settings
    {
        get
        {
            lock (SettingsLock)
            {
                if (_settings is not null && Directory.Exists(_settings))
                    return _settings;

                _settings = ResolveSettings();
                return _settings;
            }
        }
    }

    public static string Temp
    {
        get
        {
            var appData = Path.GetTempPath();
            var folderPath = Path.Combine(appData, "LenovoLegionToolkit");
            Directory.CreateDirectory(folderPath);
            return folderPath;
        }
    }

    private static string ResolveSettings()
    {
        var user = WindowsIdentity.GetCurrent().User;
        var isElevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        var programProtected = DirectoryProtection.IsProgramDirectoryProtected();

        var protectedPath = ChooseProtectedSettingsFolder(isElevated, programProtected, Program, user);
        if (protectedPath is null)
        {
            Log.Instance.Trace($"Using unprotected settings folder (elevated={isElevated}, programFolderProtected={programProtected}). Settings can be changed by any program running as this user.");
            return AppData;
        }

        // Creating the folder must work. If it doesn't, fail loudly rather than silently falling back to
        // the writable folder, which would reintroduce the problem this folder exists to solve.
        Directory.CreateDirectory(protectedPath);

        // The folder already inherits the install folder's admin-only write permissions; the explicit
        // permissions additionally hide each user's settings from other users. A failure here is logged.
        try
        {
            ApplyProtectedAcl(protectedPath, user!);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Could not apply explicit permissions to {protectedPath}; inherited install-folder permissions still apply.", ex);
        }

        TrySetOwnerToAdministrators(protectedPath);

        if (!File.Exists(Path.Combine(protectedPath, MIGRATION_MARKER)))
        {
            Log.Instance.Trace($"Migrating settings to protected folder... [from={AppData}, to={protectedPath}]");
            MigrateSettings(AppData, protectedPath);
        }

        return protectedPath;
    }

    /// <summary>
    /// The protected settings folder to use, or null when LLT can't protect settings
    /// (not elevated, the install folder is writable by normal users, or the user is unknown).
    /// </summary>
    internal static string? ChooseProtectedSettingsFolder(bool isElevated, bool programFolderProtected, string programFolder, SecurityIdentifier? user)
    {
        if (!isElevated || !programFolderProtected || user is null || string.IsNullOrWhiteSpace(programFolder))
            return null;

        return Path.Combine(programFolder, "UserData", user.Value);
    }

    /// <summary>
    /// Replaces the folder's permissions: SYSTEM and Administrators full control, the user read-only,
    /// inherited by everything inside. Nothing else (including the parent's permissions) applies.
    /// </summary>
    internal static void ApplyProtectedAcl(string directory, SecurityIdentifier user)
    {
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        new DirectoryInfo(directory).SetAccessControl(security);
    }

    private static void TrySetOwnerToAdministrators(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl(AccessControlSections.Owner);
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            info.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Could not set owner of {directory} to Administrators.", ex);
        }
    }

    /// <summary>
    /// One-time copy of the old settings into the protected folder: every top-level file except the
    /// migration marker, plus plugin configs (Plugins\Configs or Plugin\Configs). The log folder is not copied.
    /// Existing files in the destination are kept. A file that fails to copy is logged and skipped.
    /// Writes the marker at the end so the copy never runs again (old files are not read afterwards).
    /// </summary>
    internal static void MigrateSettings(string from, string to)
    {
        if (Directory.Exists(from))
        {
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (name.Equals(MIGRATION_MARKER, StringComparison.OrdinalIgnoreCase))
                    continue;

                TryCopy(file, Path.Combine(to, name));
            }

            foreach (var pluginFolder in new[] { "Plugins", "Plugin" })
            {
                var configs = Path.Combine(from, pluginFolder, PLUGIN_CONFIGS);
                if (!Directory.Exists(configs))
                    continue;

                foreach (var file in Directory.EnumerateFiles(configs, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(configs, file);
                    TryCopy(file, Path.Combine(to, "Plugins", PLUGIN_CONFIGS, relative));
                }
            }
        }

        File.WriteAllText(Path.Combine(to, MIGRATION_MARKER), $"Migrated from {from} on {DateTime.UtcNow:O}");
    }

    private static void TryCopy(string source, string destination)
    {
        try
        {
            if (File.Exists(destination))
                return;

            EnsureParentDirectoryExists(destination);
            File.Copy(source, destination, false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to migrate {source}.", ex);
        }
    }
}
