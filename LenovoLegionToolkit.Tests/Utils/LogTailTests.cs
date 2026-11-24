using System;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// <see cref="Log.TailLines"/>, used by the freeze watchdog's report: real log files in a temporary folder. Each test
/// has its own <see cref="Log"/> (not the app's), so they can run alongside others.
/// </summary>
public sealed class LogTailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-log-tail-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public void TheLastLines_AreReturned_InOrder()
    {
        var log = new Log(_dir, () => DateTime.UtcNow) { IsTraceEnabled = true };
        for (var i = 1; i <= 100; i++)
            log.Trace($"line {i}");

        var tail = log.TailLines(3);

        Assert.Equal(3, tail.Count);
        Assert.EndsWith("line 98", tail[0]);
        Assert.EndsWith("line 100", tail[^1]);
        Assert.Empty(log.TailLines(0));
    }

    [Fact]
    public void TheLines_AreRead_WhileSomethingHoldsTheLogOpenForWriting()
    {
        var log = new Log(_dir, () => DateTime.UtcNow) { IsTraceEnabled = true };
        log.Trace($"before the freeze");

        // Another writer (another copy of Indra, an editor) has the file open, allowing others to read and write.
        using (var writer = new FileStream(log.LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            var tail = log.TailLines(5);

            Assert.Single(tail);
            Assert.EndsWith("before the freeze", tail[0]);
        }
    }

    [Fact]
    public void WithoutALog_ThereAreNoLines() =>
        Assert.Empty(new Log(_dir, () => DateTime.UtcNow).TailLines(60));
}
