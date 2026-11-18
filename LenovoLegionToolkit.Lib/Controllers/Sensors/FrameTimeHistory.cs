using System;
using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

/// <summary>One frame: when it was presented (UTC ticks) and how long it took since the one before.</summary>
public readonly record struct FrameTimeSample(long Ticks, double Milliseconds);

/// <summary>
/// The game's frame times over the last <see cref="Window"/>, for the OSD's frame-time graph. Fed once per frame
/// by the FPS measurement (PresentMon, no driver); read by the graph at most 30 times a second. The lock only
/// guards copying a small array; nothing else runs under it.
/// </summary>
public sealed class FrameTimeHistory
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>At most this many frames are kept (5 s at 600 fps).</summary>
    public const int Capacity = 3000;

    /// <summary>Longer than this isn't a frame time but a pause (alt-tab, loading); it isn't drawn.</summary>
    public const double MaxMilliseconds = 1000;

    private readonly Func<long> _utcNowTicks;
    private readonly object _lock = new();
    private readonly FrameTimeSample[] _buffer = new FrameTimeSample[Capacity];
    private int _start;
    private int _count;

    public FrameTimeHistory() : this(() => DateTime.UtcNow.Ticks) { }

    /// <summary>For tests: the clock is supplied by the caller.</summary>
    internal FrameTimeHistory(Func<long> utcNowTicks) => _utcNowTicks = utcNowTicks;

    public void Add(double milliseconds)
    {
        if (double.IsNaN(milliseconds) || milliseconds <= 0 || milliseconds > MaxMilliseconds)
            return;

        var sample = new FrameTimeSample(_utcNowTicks(), milliseconds);
        lock (_lock)
        {
            if (_count < Capacity)
            {
                _buffer[(_start + _count) % Capacity] = sample;
                _count++;
            }
            else
            {
                _buffer[_start] = sample;   // the oldest gives way
                _start = (_start + 1) % Capacity;
            }
        }
    }

    /// <summary>The frames of the last <see cref="Window"/>, oldest first, and the time they were read at.</summary>
    public (IReadOnlyList<FrameTimeSample> Samples, long NowTicks) Snapshot()
    {
        var now = _utcNowTicks();
        var oldest = now - Window.Ticks;

        lock (_lock)
        {
            while (_count > 0 && _buffer[_start].Ticks < oldest)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }

            var copy = new FrameTimeSample[_count];
            for (var i = 0; i < _count; i++)
                copy[i] = _buffer[(_start + i) % Capacity];
            return (copy, now);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _start = 0;
            _count = 0;
        }
    }
}
