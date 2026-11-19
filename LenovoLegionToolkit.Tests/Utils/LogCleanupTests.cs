using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// Real files in a temporary folder; the logger's test constructor keeps the real log folder untouched.
public sealed class LogCleanupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}");
    private readonly DateTime _now = new(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private void WriteLog(string time, int bytes) => File.WriteAllBytes(Path.Combine(_dir, $"log_{time}.txt"), new byte[bytes]);

    private string[] LogNames() => [.. Directory.GetFiles(_dir, "log_*.txt").Select(Path.GetFileName).Order()!];

    [Fact]
    public void TheOldestLogsGo_UntilTheRestFit_AndThisRunsLogAndErrorReportsStay()
    {
        var log = new Log(_dir, () => _now) { IsTraceEnabled = true };
        log.Trace($"this run");                                  // this run's own log exists
        WriteLog("2026_11_10_10_00_00", 400);
        WriteLog("2026_11_12_10_00_00", 400);
        WriteLog("2026_11_15_10_00_00", 400);
        File.WriteAllBytes(Path.Combine(_dir, "error_2026_11_01_10_00_00.txt"), new byte[5000]);
        var current = Path.GetFileName(log.LogPath);

        log.RemoveOldLogs(maxTotalBytes: 900);

        Assert.Equal(new[] { "log_2026_11_15_10_00_00.txt", current }.Order(), LogNames());
        Assert.True(File.Exists(Path.Combine(_dir, "error_2026_11_01_10_00_00.txt")));
    }

    [Fact]
    public void LogsWithinTheLimit_AreAllKept()
    {
        var log = new Log(_dir, () => _now);
        WriteLog("2026_11_10_10_00_00", 100);
        WriteLog("2026_11_12_10_00_00", 100);

        log.RemoveOldLogs(maxTotalBytes: 1000);

        Assert.Equal(2, LogNames().Length);
    }

    [Fact]
    public void TheDefaultLimit_Is50Megabytes() => Assert.Equal(50L * 1024 * 1024, Log.MaxLogBytes);
}
