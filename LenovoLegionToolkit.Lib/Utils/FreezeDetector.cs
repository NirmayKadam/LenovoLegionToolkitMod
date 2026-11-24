using System;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Decides when Indra's window has stopped answering. A watchdog ticks it every couple of seconds (and asks the window
/// to answer); the window's answers come in through <see cref="OnAnswer"/>. Pure: times are passed in, in milliseconds
/// of a steady clock.
/// <para>
/// A long gap between two ticks means the laptop slept or the process was paused: that restarts the count, so it is
/// never taken for a frozen window.
/// </para>
/// </summary>
public sealed class FreezeDetector(TimeSpan freezeAfter, TimeSpan sleepGap)
{
    private long? _lastTick;
    private long _lastAnswer;
    private bool _frozen;
    private long _frozenSince;

    /// <summary>True while the window counts as not answering.</summary>
    public bool IsFrozen => _frozen;

    /// <summary>
    /// A watchdog tick. Returns how long the window has been silent when it has just been silent for
    /// <c>freezeAfter</c> (once per freeze); otherwise null.
    /// </summary>
    public TimeSpan? OnTick(long nowMs)
    {
        if (_lastTick is not { } last || nowMs - last > sleepGap.TotalMilliseconds)
        {
            // First tick, or the laptop slept: start counting from now.
            _lastTick = nowMs;
            _lastAnswer = nowMs;
            _frozen = false;
            return null;
        }

        _lastTick = nowMs;

        if (_frozen || nowMs - _lastAnswer < freezeAfter.TotalMilliseconds)
            return null;

        _frozen = true;
        _frozenSince = _lastAnswer;
        return TimeSpan.FromMilliseconds(nowMs - _lastAnswer);
    }

    /// <summary>The window answered. Returns how long it was silent when this ends a freeze; otherwise null.</summary>
    public TimeSpan? OnAnswer(long nowMs)
    {
        _lastAnswer = nowMs;
        if (!_frozen)
            return null;

        _frozen = false;
        return TimeSpan.FromMilliseconds(nowMs - _frozenSince);
    }
}
