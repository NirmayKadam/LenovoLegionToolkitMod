using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LenovoLegionToolkit.Lib.Controllers.Sensors;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

/// <summary>Where the frame-time graph draws each frame. <see cref="Spikes"/>[i] is true when frame i took more than twice the median (a stutter).</summary>
public sealed record FrameTimeGraphLayout(IReadOnlyList<Point> Points, IReadOnlyList<bool> Spikes, double ScaleMs, double MedianMs, double MedianY)
{
    /// <summary>The graph's top is at least this (30 fps), so a smooth game doesn't look jagged.</summary>
    public const double MinScaleMs = 1000.0 / 30;

    /// <summary>And at most this; longer frames are drawn at the top.</summary>
    public const double MaxScaleMs = 100;

    public static FrameTimeGraphLayout Empty { get; } = new([], [], MinScaleMs, 0, 0);

    /// <summary>
    /// Places the frames of the last <paramref name="window"/> (ending at <paramref name="nowTicks"/>) across
    /// <paramref name="width"/>, oldest on the left; height is frame time on a scale of max(33.3 ms, 1.2 × the
    /// longest frame), capped at 100 ms. Fewer than two frames draw nothing.
    /// </summary>
    public static FrameTimeGraphLayout Compute(IReadOnlyList<FrameTimeSample> samples, long nowTicks, TimeSpan window, double width, double height)
    {
        if (samples.Count < 2 || width <= 0 || height <= 0 || window <= TimeSpan.Zero)
            return Empty;

        var longest = samples.Max(s => s.Milliseconds);
        var scale = Math.Min(MaxScaleMs, Math.Max(MinScaleMs, longest * 1.2));
        var median = Median(samples.Select(s => s.Milliseconds));
        var oldest = nowTicks - window.Ticks;

        double Y(double ms) => height - Math.Min(ms, scale) / scale * height;

        var points = new Point[samples.Count];
        var spikes = new bool[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var x = Math.Clamp((samples[i].Ticks - oldest) / (double)window.Ticks, 0, 1) * width;
            points[i] = new Point(x, Y(samples[i].Milliseconds));
            spikes[i] = samples[i].Milliseconds > 2 * median;
        }

        return new FrameTimeGraphLayout(points, spikes, scale, median, Y(median));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
