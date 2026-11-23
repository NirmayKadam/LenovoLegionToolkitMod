using System.IO;
using LenovoLegionToolkit.Tests.UI;
using Xunit;

namespace LenovoLegionToolkit.Tests.AutoListeners;

/// <summary>
/// Indra never opens a game's process: kernel anti-cheats watch for other programs holding handles to the game. The
/// code that finds games, lists the GPU's processes and reads process paths used to open them (and, when refused,
/// fell back to an all-access open and to reading the process's memory). These calls open a process, so they must
/// not appear there; the check reads the source, since the behaviour can't be watched from a test.
/// </summary>
public class NoGameProcessOpenedTests
{
    private static readonly string[] OpensAProcess =
    [
        "GetProcessById",
        "EnableRaisingEvents",
        "HasExited",
        ".SafeHandle",
        "MainModule",
        "OpenProcess",
        "WaitForExit",
        ".StartTime",
    ];

    [Theory]
    [InlineData("LenovoLegionToolkit.Lib", "AutoListeners", "GameAutoListener.cs")]
    [InlineData("LenovoLegionToolkit.Lib", "AutoListeners", "GameTracker.cs")]
    [InlineData("LenovoLegionToolkit.Lib", "AutoListeners", "ProcessAutoListener.cs")]
    [InlineData("LenovoLegionToolkit.Lib", "Extensions", "PhyscialGPUExtensions.cs")]
    [InlineData("LenovoLegionToolkit.Lib", "Extensions", "ProcessExtensions.cs")]
    [InlineData("LenovoLegionToolkit.Lib", "System", "ProcessImagePath.cs")]
    public void FindingGamesAndReadingTheirPaths_OpensNoProcess(params string[] relativePath)
    {
        var source = File.ReadAllText(Path.Combine([HelpContentTests.FindRepositoryRoot(), .. relativePath]));

        Assert.All(OpensAProcess, call => Assert.DoesNotContain(call, source));
    }
}
