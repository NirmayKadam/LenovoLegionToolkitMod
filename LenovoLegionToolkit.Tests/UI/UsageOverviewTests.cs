using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>The usage rings and the 60-second temperature graph: what they keep and where they draw.</summary>
public class UsageOverviewTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    [Fact]
    public void TheHistory_KeepsTheLast60Seconds()
    {
        var history = new ReadingHistory(Minute);
        for (var s = 0; s <= 90; s += 10)
            history.Add(T0.AddSeconds(s), s);

        var kept = history.Readings(T0.AddSeconds(90));

        Assert.Equal([30d, 40, 50, 60, 70, 80, 90], kept.Select(r => r.Value!.Value));
        Assert.Empty(history.Readings(T0.AddSeconds(200)));
    }

    [Fact]
    public void UnknownReadings_StayAsGaps()
    {
        var history = new ReadingHistory(Minute);
        history.Add(T0, 40);
        history.Add(T0.AddSeconds(1), null);
        history.Add(T0.AddSeconds(2), 42);

        Assert.Equal([40d, null, 42d], history.Readings(T0.AddSeconds(2)).Select(r => r.Value));
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(-5, true, false)]
    [InlineData(100, false, true)]
    [InlineData(250, false, true)]
    [InlineData(double.NaN, true, false)]
    public void TheRing_IsEmptyAtZero_AndFullAt100(double percent, bool empty, bool full)
    {
        var arc = RingGeometry.Arc(percent, 30);
        Assert.Equal((empty, full), (arc.Empty, arc.Full));
    }

    [Theory]
    [InlineData(25, 60, 30, false)]    // a quarter: right side, middle height
    [InlineData(50, 30, 60, false)]    // half: bottom
    [InlineData(75, 0, 30, true)]      // three quarters: left side, the long way round
    public void TheRing_StartsAtTheTop_AndGoesClockwise(double percent, double x, double y, bool largeArc)
    {
        var arc = RingGeometry.Arc(percent, 30);

        Assert.Equal((30d, 0d), (arc.Start.X, arc.Start.Y));
        Assert.Equal(x, arc.End.X, 6);
        Assert.Equal(y, arc.End.Y, 6);
        Assert.Equal(largeArc, arc.IsLargeArc);
    }

    [Fact]
    public void TheGraph_PutsNowOnTheRight_AndTheScaleFromBottomToTop()
    {
        (DateTime, double?)[] readings = [(T0, 0), (T0.AddSeconds(30), 50), (T0.AddSeconds(60), 100)];

        var line = Assert.Single(GraphLayout.Map(readings, T0.AddSeconds(60), Minute, 200, 100, 0, 100));

        Assert.Equal([(0d, 100d), (100d, 50d), (200d, 0d)], line.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void TheGraph_BreaksAtUnknownReadings_AndClampsOffScaleOnes()
    {
        (DateTime, double?)[] readings = [(T0, 40), (T0.AddSeconds(10), 120), (T0.AddSeconds(20), null), (T0.AddSeconds(30), -10)];

        var lines = GraphLayout.Map(readings, T0.AddSeconds(60), Minute, 60, 100, 0, 100);

        Assert.Equal(2, lines.Count);
        Assert.Equal(0d, lines[0][1].Y);      // 120 °C drawn at the top
        Assert.Equal(100d, lines[1][0].Y);    // -10 drawn at the bottom
    }

    [Fact]
    public void TheTiles_SitRightAboveTheSensorCards_AndHideWithThem()
    {
        var code = File.ReadAllText(Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF", "Pages", "DashboardPage.xaml.cs")).Replace("\r\n", "\n");

        // Inserted before the content first, so the sensor cards, inserted at the same place afterwards, come below it.
        Assert.Contains("_panel.Children.Insert(_panel.Children.IndexOf(_content), _usageOverview);\n        RefreshSensorControl();", code);
        Assert.Equal(2, code.Split("_usageOverview.Visibility = _dashboardSettings.Store.ShowSensors").Length - 1);
    }
}
