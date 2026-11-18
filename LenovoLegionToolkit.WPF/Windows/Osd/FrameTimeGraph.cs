using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

/// <summary>
/// The OSD's frame-time graph: each frame of the last few seconds, oldest on the left, stutters (frames over twice
/// the median) in <see cref="SpikeBrush"/>, and a faint line at the median. It reads <see cref="History"/> at most
/// 30 times a second, and only while it is visible (the OSD is shown and the item is on); it draws nothing until real
/// frames arrive.
/// </summary>
public sealed class FrameTimeGraph : FrameworkElement
{
    private const double LineThickness = 1.25;
    private const double MedianOpacity = 0.35;

    private readonly DispatcherTimer _timer;

    private IReadOnlyList<FrameTimeSample> _samples = [];
    private long _nowTicks;

    private Pen _linePen = CreatePen(Brushes.White, 1);
    private Pen _medianPen = CreatePen(Brushes.White, MedianOpacity);
    private Pen _spikePen = CreatePen(Brushes.Red, 1);

    public FrameTimeGraph()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Where the frames come from (the FPS measurement's history). Nothing is drawn without it.</summary>
    public FrameTimeHistory? History { get; set; }

    /// <summary>Sets the colours: frames and the median line in <paramref name="line"/>, stutters in <paramref name="spike"/>.</summary>
    public void SetBrushes(Brush line, Brush spike)
    {
        _linePen = CreatePen(line, 1);
        _medianPen = CreatePen(line, MedianOpacity);
        _spikePen = CreatePen(spike, 1);
        InvalidateVisual();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _timer.Start();
            return;
        }

        _timer.Stop();
        _samples = [];
        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (History is null)
                return;

            var (samples, nowTicks) = History.Snapshot();
            if (samples.Count == 0 && _samples.Count == 0)
                return;   // nothing drawn, nothing to draw

            _samples = samples;
            _nowTicks = nowTicks;
            InvalidateVisual();   // the frames move left as time passes, so redraw while there are any
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Frame-time graph update failed", ex);
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        try
        {
            var layout = FrameTimeGraphLayout.Compute(_samples, _nowTicks, FrameTimeHistory.Window, ActualWidth, ActualHeight);
            var points = layout.Points;
            if (points.Count == 0)
                return;

            drawingContext.DrawLine(_medianPen, new Point(0, layout.MedianY), new Point(ActualWidth, layout.MedianY));

            var line = new StreamGeometry();
            using (var context = line.Open())
            {
                context.BeginFigure(points[0], false, false);
                for (var i = 1; i < points.Count; i++)
                    context.LineTo(points[i], true, false);
            }
            line.Freeze();
            drawingContext.DrawGeometry(null, _linePen, line);

            var spikes = new StreamGeometry();
            var anySpike = false;
            using (var context = spikes.Open())
            {
                for (var i = 1; i < points.Count; i++)
                {
                    if (!layout.Spikes[i])
                        continue;

                    context.BeginFigure(points[i - 1], false, false);
                    context.LineTo(points[i], true, false);
                    anySpike = true;
                }
            }

            if (!anySpike)
                return;

            spikes.Freeze();
            drawingContext.DrawGeometry(null, _spikePen, spikes);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Frame-time graph drawing failed", ex);
        }
    }

    /// <summary>A frozen pen of a copy of <paramref name="brush"/>, so the OSD's shared brushes aren't changed.</summary>
    private static Pen CreatePen(Brush brush, double opacity)
    {
        var copy = brush.CloneCurrentValue();
        copy.Opacity = brush.Opacity * opacity;
        copy.Freeze();

        var pen = new Pen(copy, LineThickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
}
