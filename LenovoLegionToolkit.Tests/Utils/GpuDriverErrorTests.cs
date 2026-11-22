using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// What a GPU driver error is shown with, from given times (no clock), and reading Windows' real System log.
public class GpuDriverErrorTests
{
    private static readonly DateTime Error = new(2026, 11, 19, 7, 31, 28, DateTimeKind.Utc);

    [Fact]
    public void ARecentPowerModeChange_AfterburnerProfileAndGame_AreAllMentioned()
    {
        var context = GpuDriverErrorMonitor.DescribeContext(Error,
            new PowerModeChange(PowerModeState.Performance, Error.AddSeconds(-2)),
            new AfterburnerStart(4, Error.AddSeconds(-1)),
            "witcher3");

        Assert.Equal(string.Join("; ",
            string.Format(Resource.GpuDriverError_AfterPowerMode, 2, PowerModeState.Performance.GetDisplayName()),
            string.Format(Resource.GpuDriverError_AfterAfterburner, 4, 1),
            string.Format(Resource.GpuDriverError_Game, "witcher3")), context);
    }

    [Fact]
    public void ChangesLongBeforeTheError_OrAfterIt_AreLeftOut()
    {
        var old = GpuDriverErrorMonitor.Recent + TimeSpan.FromSeconds(1);

        Assert.Null(GpuDriverErrorMonitor.DescribeContext(Error,
            new PowerModeChange(PowerModeState.Performance, Error - old),
            new AfterburnerStart(4, Error.AddSeconds(5)),   // after the error: not its cause
            null));
    }

    [Fact]
    public void WithNothingKnown_TheNotificationJustGivesTheTime()
    {
        var local = new DateTime(2026, 11, 19, 13, 1, 28);
        Assert.Equal(string.Format(Resource.GpuDriverError_Notification, "13:01:28"), GpuDriverErrorMonitor.Describe(local, null));
        Assert.Equal(string.Format(Resource.GpuDriverError_NotificationWithContext, "13:01:28", "game: x"), GpuDriverErrorMonitor.Describe(local, "game: x"));
    }

    [Fact]
    public void TheRealSystemLog_IsRead_AndOnlyGpuDriverErrorsComeBack()
    {
        var since = DateTime.UtcNow - TimeSpan.FromDays(7);

        var errors = GpuDriverErrorMonitor.ReadFromLog(since, new Dictionary<DateTime, string>());

        Assert.NotNull(errors);
        Assert.All(errors, e =>
        {
            Assert.True(e.Source is "nvlddmkm" || (e.Source == "Display" && e.EventId == 4101), $"{e.Source} {e.EventId}");
            Assert.True(e.TimeUtc >= since.AddSeconds(-1));
        });
        Assert.Equal(errors.OrderByDescending(e => e.TimeUtc).Select(e => e.TimeUtc), errors.Select(e => e.TimeUtc));
    }

    [SkippableFact]
    public void WhatWasKnownLive_IsAddedToTheMatchingError()
    {
        var since = DateTime.UtcNow - TimeSpan.FromDays(7);
        var all = GpuDriverErrorMonitor.ReadFromLog(since, new Dictionary<DateTime, string>())!;

        // Reported as skipped, not passed, on a machine without a GPU driver error in the last week (CI, most days).
        Skip.If(all.Count == 0, "No GPU driver error in this machine's System log in the last 7 days, so nothing to match.");

        var first = all[0];
        var withContext = GpuDriverErrorMonitor.ReadFromLog(since, new Dictionary<DateTime, string> { [first.TimeUtc] = "seen live" })!;

        Assert.Equal("seen live", withContext.First(e => e.TimeUtc == first.TimeUtc).Context);
    }
}
