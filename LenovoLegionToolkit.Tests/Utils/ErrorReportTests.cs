using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// Error reports: written to a local folder even when trace logging is off, bounded in number, and not repeated
// for the same error within a minute. A temp folder and a supplied clock are used, never the real log folder.
public sealed class ErrorReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-errorreport-tests-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private Log NewLog() => new(_dir, () => _now) { IsTraceEnabled = false };

    private string[] Reports() => Directory.Exists(_dir) ? Directory.GetFiles(_dir, "error_*.txt") : [];

    private static Exception Thrown(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void ReportIsWritten_WithTraceLoggingOff()
    {
        NewLog().ErrorReport("Unhandled error (test)", Thrown("boom"));

        var report = File.ReadAllText(Assert.Single(Reports()));
        Assert.Contains("Unhandled error (test)", report);
        Assert.Contains("boom", report);
        Assert.Contains(nameof(InvalidOperationException), report);
    }

    [Fact]
    public void TheSameError_WithinAMinute_IsWrittenOnce()
    {
        var log = NewLog();
        var error = Thrown("same");

        log.ErrorReport("first", error);
        _now = _now.AddSeconds(30);
        log.ErrorReport("second", error);

        Assert.Single(Reports());

        _now = _now.AddSeconds(31);
        log.ErrorReport("third", error);
        Assert.Equal(2, Reports().Length);
    }

    [Fact]
    public void DifferentErrors_AreAllWritten()
    {
        var log = NewLog();

        log.ErrorReport("a", Thrown("one"));
        _now = _now.AddSeconds(1);
        log.ErrorReport("b", Thrown("two"));

        Assert.Equal(2, Reports().Length);
    }

    [Fact]
    public void OnlyTheNewestReportsAreKept()
    {
        var log = NewLog();
        for (var i = 0; i < Log.MaxErrorReports + 1; i++)
        {
            log.ErrorReport($"report {i}", Thrown($"error {i}"));
            _now = _now.AddSeconds(1);
        }

        var kept = Reports().Select(File.ReadAllText).ToArray();
        Assert.Equal(Log.MaxErrorReports, kept.Length);
        Assert.DoesNotContain(kept, r => r.Contains("report 0" + Environment.NewLine));
        Assert.Contains(kept, r => r.Contains($"report {Log.MaxErrorReports}"));
    }
}
