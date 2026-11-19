using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using LenovoLegionToolkit.Lib.Automation.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

// Real files and real Windows permissions: a temp folder (owned by the test user), and Windows' own cmd.exe.
public class RunTargetCheckTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsers = new(WellKnownSidType.AuthenticatedUserSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly IReadOnlySet<SecurityIdentifier> Others = new HashSet<SecurityIdentifier> { Users, AuthenticatedUsers };

    [Fact]
    public void AProgramInAFolderTheUserCanWrite_IsReplaceable()
    {
        var path = Path.Combine(_dir, "tool.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A]);

        var result = RunTargetCheck.Check(path);

        Assert.Equal(RunTargetProblem.Replaceable, result.Problem);
        Assert.Equal(_dir, result.Path);
        Assert.Contains(_dir, RunTargetCheck.Describe(result));
    }

    [Fact]
    public void WindowsOwnCmd_IsNotReplaceable()
    {
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.Equal(RunTargetProblem.None, RunTargetCheck.Check(cmd).Problem);
        Assert.Null(RunTargetCheck.Describe(RunTargetCheck.Check(cmd)));
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell")]
    [InlineData("\"tool.exe\"")]
    public void ANameWithoutAFolder_IsFlagged(string name) =>
        Assert.Equal(RunTargetProblem.BareName, RunTargetCheck.Check(name).Problem);

    [Fact]
    public void AMissingProgram_IsFlagged() =>
        Assert.Equal(RunTargetProblem.NotFound, RunTargetCheck.Check(Path.Combine(_dir, "missing.exe")).Problem);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoProgram_IsNotFlagged(string? path) =>
        Assert.Equal(RunTargetProblem.None, RunTargetCheck.Check(path).Problem);

    // ---- The permission rule itself, on in-memory security descriptors ----

    private static AuthorizationRuleCollection Rules(params FileSystemAccessRule[] rules)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var rule in rules)
            security.AddAccessRule(rule);
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier));
    }

    private const FileSystemRights Write = FileSystemRights.WriteData | FileSystemRights.Delete;

    [Fact]
    public void AdminOnlyWrite_IsNotReplaceable() =>
        Assert.False(RunTargetCheck.GrantsWrite(Administrators, Rules(
            new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow),
            new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, AccessControlType.Allow),
            new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, AccessControlType.Allow)), Write, Others));

    [Fact]
    public void ModifyForAuthenticatedUsers_IsReplaceable() =>
        Assert.True(RunTargetCheck.GrantsWrite(Administrators, Rules(
            new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow),
            new FileSystemAccessRule(AuthenticatedUsers, FileSystemRights.Modify, AccessControlType.Allow)), Write, Others));

    [Fact]
    public void AnOwnerThatIsAnOrdinaryUser_CanChangeThePermissions_SoIsReplaceable() =>
        Assert.True(RunTargetCheck.GrantsWrite(Users, Rules(
            new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow)), Write, Others));

    [Fact]
    public void DenyRules_AreNotTakenAsWriteAccess() =>
        Assert.False(RunTargetCheck.GrantsWrite(Administrators, Rules(
            new FileSystemAccessRule(Users, FileSystemRights.Modify, AccessControlType.Deny)), Write, Others));
}
