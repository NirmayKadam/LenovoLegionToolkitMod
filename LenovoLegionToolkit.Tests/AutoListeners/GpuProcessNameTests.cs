using System;
using System.Diagnostics;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.AutoListeners;

/// <summary>
/// Names of the programs on the discrete GPU. The NVIDIA driver reported Indra itself under its package name
/// ("eef45acd-…_6qs7aha96dxnt"), so it wasn't recognised as Indra, counted as a game, and was dropped and added again
/// every 10 seconds; "game running" never ended. Names now come from the program's file, as Windows names it.
/// </summary>
public class GpuProcessNameTests
{
    private const string PackageName = "eef45acd-2cf3-4d7d-9d33-92f37c74cc31_6qs7aha96dxnt";

    [Fact]
    public void TheFileName_WinsOverTheDriversName() =>
        Assert.Equal("Lenovo Legion Toolkit", NVAPIExtensions.ProcessName(@"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe", PackageName));

    [Fact]
    public void WithoutAPath_TheDriversNameIsUsed() =>
        Assert.Equal("witcher3", NVAPIExtensions.ProcessName(null, "witcher3.exe"));

    [Theory]
    [InlineData(@"C:\Games\Heaven.exe", null, "Heaven")]
    [InlineData(null, @"C:\Games\Heaven.EXE", "Heaven")]
    [InlineData(@"C:\Tools\GameGuard.des", null, "GameGuard.des")]   // only ".exe" is dropped, as Windows does
    [InlineData(null, null, "")]
    [InlineData("  ", " ", "")]
    public void OnlyTheExeExtensionIsDropped(string? path, string? driverName, string expected) =>
        Assert.Equal(expected, NVAPIExtensions.ProcessName(path, driverName));

    [Fact]
    public void ARealProcess_IsNamedAsTheProcessListNamesIt()
    {
        using var current = Process.GetCurrentProcess();

        var name = NVAPIExtensions.ProcessName(ProcessImagePath.Get(Environment.ProcessId), PackageName);

        Assert.Equal(current.ProcessName, name, StringComparer.OrdinalIgnoreCase);
    }
}
