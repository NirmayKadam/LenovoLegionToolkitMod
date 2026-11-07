using System;
using System.IO;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Checks real folders: a temp folder (writable) and Program Files / System32 (admin-only).
public class AutorunLocationTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ExeInUserTempFolder_IsNotProtected()
    {
        Assert.False(Autorun.IsLaunchLocationProtected(Path.Combine(_dir, "Lenovo Legion Toolkit.exe")));
    }

    [Fact]
    public void ExeInProgramFiles_IsProtected()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo Legion Toolkit.exe");
        Assert.True(Autorun.IsLaunchLocationProtected(path));
    }

    [Fact]
    public void ExeInSystem32_IsProtected()
    {
        Assert.True(Autorun.IsLaunchLocationProtected(Path.Combine(Environment.SystemDirectory, "notepad.exe")));
    }

    [Fact]
    public void ExeInMissingFolder_IsNotProtected()
    {
        Assert.False(Autorun.IsLaunchLocationProtected(Path.Combine(_dir, "missing", "app.exe")));
    }

    [Theory]
    [InlineData("2.36.0.0|C:\\Program Files\\LenovoLegionToolkit\\Lenovo Legion Toolkit.exe", "C:\\Program Files\\LenovoLegionToolkit\\Lenovo Legion Toolkit.exe")]
    [InlineData("2.36.0.0|D:\\Downloads\\LLT\\Lenovo Legion Toolkit.exe", "D:\\Downloads\\LLT\\Lenovo Legion Toolkit.exe")]
    [InlineData("2.36.0.0", null)]
    [InlineData("2.36.0.0|", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TaskData_TargetIsParsed(string? data, string? expected)
    {
        Assert.Equal(expected, Autorun.GetTargetFromTaskData(data));
    }
}
