using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// <summary>Process calls and properties that open the process (each needs a handle to it), as whole words.</summary>
    private static readonly Regex OpensAProcess = new(
        @"\b(GetProcessById|EnableRaisingEvents|HasExited|OpenProcess|WaitForExit|MainModule)\b" +
        @"|\.(SafeHandle|StartTime|Modules|PriorityClass|PriorityBoostEnabled|ProcessorAffinity|TotalProcessorTime|UserProcessorTime|ExitCode|ExitTime|Kill)\b" +
        @"|(?<!\bgpu)\.Handle\b");   // NVIDIA's GPU handle (gpu.Handle) is not a process handle

    [Fact]
    public void TheCheck_FindsEachCall_ButNotLookAlikes()
    {
        Assert.Matches(OpensAProcess, "var t = process.StartTime;");
        Assert.Matches(OpensAProcess, "process.Kill(true);");
        Assert.Matches(OpensAProcess, "_ = process.Handle;");
        Assert.Matches(OpensAProcess, "process.PriorityClass = ProcessPriorityClass.High;");
        Assert.DoesNotMatch(OpensAProcess, "var h = key.HandleCount;");
        Assert.DoesNotMatch(OpensAProcess, "GPUApi.QueryActiveApps(gpu.Handle)");
        Assert.DoesNotMatch(OpensAProcess, "StartTimeUtc = info.StartTimeUtc");
    }

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

        Assert.Empty(OpensAProcess.Matches(source).Select(m => m.Value));
    }
}
