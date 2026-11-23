using System;
using System.Diagnostics;
using System.IO;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>
/// A process's file path, read from Windows' process list by id without opening the process. Real processes: this
/// test run and a short-lived cmd.exe.
/// </summary>
public class ProcessImagePathTests
{
    [Fact]
    public void ThisProcess_IsReadAsTheFileItWasStartedFrom()
    {
        var expected = Environment.ProcessPath;
        Assert.NotNull(expected);

        Assert.Equal(expected, ProcessImagePath.Get(Environment.ProcessId), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnotherProgram_IsReadAsItsOwnFile()
    {
        using var cmd = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        try
        {
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ProcessImagePath.Get(cmd.Id), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            cmd.Kill(true);
        }
    }

    [Fact]
    public void AnIdNoProcessHas_HasNoPath()
    {
        // Windows hands out ids far below this; it is never in use.
        const int unused = int.MaxValue - 3;
        Assert.DoesNotContain(Process.GetProcesses(), p => { using (p) return p.Id == unused; });

        Assert.Null(ProcessImagePath.Get(unused));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoProcessId_HasNoPath(int processId) => Assert.Null(ProcessImagePath.Get(processId));

    [Fact]
    public void ANetworkPath_IsWrittenAsAShare() =>
        Assert.Equal(@"\\server\share\game.exe", ProcessImagePath.ToDrivePath(@"\Device\Mup\server\share\game.exe"));

    [Fact]
    public void ADeviceNoDriveUsesHasNoPath() =>
        Assert.Null(ProcessImagePath.ToDrivePath(@"\Device\NoSuchDevice42\game.exe"));
}
