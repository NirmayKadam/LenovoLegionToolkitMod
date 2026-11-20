using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>Readings of the last <c>window</c> (for example 60 seconds); null is a reading that wasn't known.</summary>
public sealed class ReadingHistory(TimeSpan window)
{
    private readonly Queue<(DateTime Time, double? Value)> _readings = new();

    public TimeSpan Window { get; } = window;

    public void Add(DateTime time, double? value)
    {
        _readings.Enqueue((time, value));
        DropOlderThan(time);
    }

    public void Clear() => _readings.Clear();

    /// <summary>The readings still inside the window at <paramref name="now"/>, oldest first.</summary>
    public IReadOnlyList<(DateTime Time, double? Value)> Readings(DateTime now)
    {
        DropOlderThan(now);
        return [.. _readings];
    }

    private void DropOlderThan(DateTime now)
    {
        while (_readings.Count > 0 && now - _readings.Peek().Time > Window)
            _readings.Dequeue();
    }
}

/// <summary>A ring's arc: from the top, clockwise, as far as the percentage.</summary>
public readonly record struct RingArc(bool Empty, bool Full, Point Start, Point End, bool IsLargeArc);

public static class RingGeometry
{
    /// <summary>The arc for <paramref name="percent"/> (clamped to 0–100) on a circle of <paramref name="radius"/> centred at (radius, radius).</summary>
    public static RingArc Arc(double percent, double radius)
    {
        var p = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
        var start = new Point(radius, 0);

        if (p <= 0)
            return new RingArc(true, false, start, start, false);
        if (p >= 100)
            return new RingArc(false, true, start, start, true);

        var angle = p / 100 * 2 * Math.PI;
        var end = new Point(radius + radius * Math.Sin(angle), radius - radius * Math.Cos(angle));
        return new RingArc(false, false, start, end, p > 50);
    }
}

public static class GraphLayout
{
    /// <summary>
    /// Maps readings to points in a <paramref name="width"/> × <paramref name="height"/> area: now at the right edge,
    /// the start of the window at the left, <paramref name="min"/> at the bottom and <paramref name="max"/> at the top.
    /// Unknown readings split the line, so each returned list is one unbroken stretch.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Point>> Map(IReadOnlyList<(DateTime Time, double? Value)> readings, DateTime now, TimeSpan window,
        double width, double height, double min, double max)
    {
        var lines = new List<IReadOnlyList<Point>>();
        var current = new List<Point>();

        foreach (var (time, value) in readings)
        {
            if (value is not { } v || double.IsNaN(v))
            {
                if (current.Count > 0)
                    lines.Add(current);
                current = [];
                continue;
            }

            var age = (now - time).TotalSeconds / window.TotalSeconds;
            var x = width * (1 - Math.Clamp(age, 0, 1));
            var y = height * (1 - (Math.Clamp(v, min, max) - min) / (max - min));
            current.Add(new Point(x, y));
        }

        if (current.Count > 0)
            lines.Add(current);

        return lines.Where(l => l.Count > 0).ToList();
    }
}
