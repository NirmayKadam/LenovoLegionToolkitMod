using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Reads real ACLs of real folders on this machine.
public class DirectoryProtectionTests : IDisposable
{
    private static readonly SecurityIdentifier CurrentUser = WindowsIdentity.GetCurrent().User!;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}");

    public DirectoryProtectionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        // As owner, the test user can always rewrite the DACL of folders it created; give itself
        // full control back so the folders can be deleted.
        foreach (var dir in Directory.GetDirectories(_dir, "*", SearchOption.AllDirectories))
            RestoreUserAccess(dir);
        RestoreUserAccess(_dir);

        Directory.Delete(_dir, true);
    }

    private static void RestoreUserAccess(string dir)
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

    private static DirectorySecurity AdminOnlyWithUserRights(FileSystemRights userRights)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(CurrentUser, userRights, AccessControlType.Allow));
        return security;
    }

    [Fact]
    public void UserTempFolder_IsWritableByNonAdmins()
    {
        Assert.True(DirectoryProtection.IsWritableByNonAdmins(_dir));
    }

    [Fact]
    public void ProgramFiles_IsProtected()
    {
        Assert.False(DirectoryProtection.IsWritableByNonAdmins(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
    }

    [Fact]
    public void WindowsFolder_IsProtected()
    {
        Assert.False(DirectoryProtection.IsWritableByNonAdmins(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
    }

    [Fact]
    public void AdminOnlyFolder_WithUserReadOnly_IsProtected()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_dir, "protected"));
        dir.SetAccessControl(AdminOnlyWithUserRights(FileSystemRights.ReadAndExecute));

        Assert.False(DirectoryProtection.IsWritableByNonAdmins(dir.FullName));
    }

    [Theory]
    [InlineData(FileSystemRights.WriteData)]
    [InlineData(FileSystemRights.AppendData)]
    [InlineData(FileSystemRights.Delete)]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.Modify)]
    public void AdminOnlyFolder_WithAnyUserWriteRight_IsWritable(FileSystemRights userRights)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_dir, $"user_{(int)userRights}"));
        dir.SetAccessControl(AdminOnlyWithUserRights(FileSystemRights.ReadAndExecute | userRights));

        Assert.True(DirectoryProtection.IsWritableByNonAdmins(dir.FullName));
    }

    [Fact]
    public void MissingFolder_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => DirectoryProtection.IsWritableByNonAdmins(Path.Combine(_dir, "missing")));
    }
}
