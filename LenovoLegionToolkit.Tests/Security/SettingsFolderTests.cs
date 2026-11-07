using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Uses real temp folders and real ACLs.
public class SettingsFolderTests : IDisposable
{
    private static readonly SecurityIdentifier CurrentUser = WindowsIdentity.GetCurrent().User!;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}");
    private readonly string _from;
    private readonly string _to;

    public SettingsFolderTests()
    {
        _from = Directory.CreateDirectory(Path.Combine(_root, "old")).FullName;
        _to = Directory.CreateDirectory(Path.Combine(_root, "new")).FullName;
    }

    public void Dispose()
    {
        foreach (var dir in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories).Append(_root))
        {
            try
            {
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(false, false);
                security.AddAccessRule(new FileSystemAccessRule(CurrentUser, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(dir).SetAccessControl(security);
            }
            catch
            {
                // Best effort.
            }
        }

        Directory.Delete(_root, true);
    }

    private void Write(string root, params string[] parts)
    {
        var path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("/", parts));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ProtectedFolder_NotUsed_WhenNotElevatedOrProgramFolderWritable(bool elevated, bool programProtected)
    {
        Assert.Null(Folders.ChooseProtectedSettingsFolder(elevated, programProtected, @"C:\Program Files\LenovoLegionToolkit", CurrentUser));
    }

    [Fact]
    public void ProtectedFolder_IsPerUser_UnderInstallFolder()
    {
        var path = Folders.ChooseProtectedSettingsFolder(true, true, @"C:\Program Files\LenovoLegionToolkit", CurrentUser);
        Assert.Equal(Path.Combine(@"C:\Program Files\LenovoLegionToolkit", "UserData", CurrentUser.Value), path);
    }

    [Fact]
    public void Migration_CopiesSettingsAndPluginConfigs_SkipsLogs_WritesMarker()
    {
        Write(_from, "settings.json");
        Write(_from, "automation.json");
        Write(_from, "args.txt");
        Write(_from, "lang");
        Write(_from, "log", "log_2026.txt");
        Write(_from, "Plugins", "Configs", "MyPlugin", "plugin.json");
        Write(_from, "Plugins", "Evil.dll");

        Folders.MigrateSettings(_from, _to);

        Assert.Equal("settings.json", File.ReadAllText(Path.Combine(_to, "settings.json")));
        Assert.True(File.Exists(Path.Combine(_to, "automation.json")));
        Assert.True(File.Exists(Path.Combine(_to, "args.txt")));
        Assert.True(File.Exists(Path.Combine(_to, "lang")));
        Assert.True(File.Exists(Path.Combine(_to, "Plugins", "Configs", "MyPlugin", "plugin.json")));
        Assert.False(Directory.Exists(Path.Combine(_to, "log")));
        Assert.False(File.Exists(Path.Combine(_to, "Plugins", "Evil.dll")));
        Assert.True(File.Exists(Path.Combine(_to, Folders.MIGRATION_MARKER)));
    }

    [Fact]
    public void Migration_KeepsExistingDestinationFiles()
    {
        Write(_from, "settings.json");
        File.WriteAllText(Path.Combine(_to, "settings.json"), "newer");

        Folders.MigrateSettings(_from, _to);

        Assert.Equal("newer", File.ReadAllText(Path.Combine(_to, "settings.json")));
    }

    [Fact]
    public void Migration_ContinuesAfterAFileFails()
    {
        Write(_from, "a.json");
        Write(_from, "b.json");

        // Lock a.json so it can't be read.
        using (new FileStream(Path.Combine(_from, "a.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Folders.MigrateSettings(_from, _to);
        }

        Assert.False(File.Exists(Path.Combine(_to, "a.json")));
        Assert.True(File.Exists(Path.Combine(_to, "b.json")));
        Assert.True(File.Exists(Path.Combine(_to, Folders.MIGRATION_MARKER)));
    }

    [Fact]
    public void Migration_FromMissingFolder_OnlyWritesMarker()
    {
        Folders.MigrateSettings(Path.Combine(_root, "missing"), _to);

        Assert.Equal([Path.Combine(_to, Folders.MIGRATION_MARKER)], Directory.GetFiles(_to));
    }

    [Fact]
    public void ProtectedAcl_LeavesFolderWritableOnlyByAdmins()
    {
        Assert.True(DirectoryProtection.IsWritableByNonAdmins(_to));

        Folders.ApplyProtectedAcl(_to, CurrentUser);

        Assert.False(DirectoryProtection.IsWritableByNonAdmins(_to));
    }
}
