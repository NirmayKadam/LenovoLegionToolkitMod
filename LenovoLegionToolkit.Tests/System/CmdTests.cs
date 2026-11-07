using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

// These tests start real cmd.exe processes.
public class CmdTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task LargeOutput_DoesNotDeadlock_AndIsReturnedInFull()
    {
        // ~60 KB of stdout, well above the ~4 KB pipe buffer that made the old code hang.
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, output) = await CMD.RunAsync("cmd.exe", "/c for /L %i in (1,1,5000) do @echo line %i", token: cts.Token);

        Assert.Equal(0, exitCode);
        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5000, lines.Length);
        Assert.Equal("line 1", lines.First());
        Assert.Equal("line 5000", lines.Last());
    }

    [Fact]
    public async Task LargeStderr_DoesNotDeadlock()
    {
        // The old code never read stderr at all.
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, output) = await CMD.RunAsync("cmd.exe", "/c (for /L %i in (1,1,5000) do @echo err %i 1>&2) & echo done", token: cts.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output.Trim());
    }

    [Fact]
    public async Task ExitCode_IsReturned()
    {
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, _) = await CMD.RunAsync("cmd.exe", "/c exit 7", token: cts.Token);

        Assert.Equal(7, exitCode);
    }

    [Fact]
    public async Task NoWait_ReturnsImmediately()
    {
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, output) = await CMD.RunAsync("cmd.exe", "/c for /L %i in (1,1,5000) do @echo line %i", waitForExit: false, token: cts.Token);

        Assert.Equal(-1, exitCode);
        Assert.Equal(string.Empty, output);
    }
}
