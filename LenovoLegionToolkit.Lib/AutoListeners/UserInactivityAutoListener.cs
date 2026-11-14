using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using LenovoLegionToolkit.Lib.Utils;
using Timer = System.Threading.Timer;

namespace LenovoLegionToolkit.Lib.AutoListeners;

/// <summary>
/// Reports how long the user has been idle, in steps of <see cref="TimerResolution"/>, and when they become active
/// again. Activity is detected by checking Windows' own last-input time (GetLastInputInfo) once a second instead of
/// installing low-level keyboard and mouse hooks, so nothing in LLT sits in the input path of games and their
/// anti-cheat software. Idle steps come from a timer, as before, so time spent asleep doesn't count as idle.
/// </summary>
public class UserInactivityAutoListener : AbstractAutoListener<UserInactivityAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(TimeSpan timerResolution, uint tickCount) : EventArgs
    {
        public TimeSpan TimerResolution { get; } = timerResolution;
        public uint TickCount { get; } = tickCount;
    }

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly TimeSpan _timerResolution = TimeSpan.FromSeconds(10);
    private readonly object _lock = new();
    private readonly Func<uint> _lastInputTime;

    private Timer? _idleTimer;
    private Timer? _pollTimer;
    private uint _lastSeenInput;
    private uint _tickCount;

    public UserInactivityAutoListener() : this(LastInputTimeFromWindows) { }

    /// <summary>For tests: supplies the last-input time instead of asking Windows.</summary>
    internal UserInactivityAutoListener(Func<uint> lastInputTime) => _lastInputTime = lastInputTime;

    public TimeSpan InactivityTimeSpan => _timerResolution * _tickCount;

    protected override Task StartAsync()
    {
        lock (_lock)
        {
            _tickCount = 0;
            _lastSeenInput = ReadLastInputOr(0);
            _idleTimer = new Timer(_ => OnIdleTick(), null, _timerResolution, _timerResolution);
            _pollTimer = new Timer(_ => OnPoll(), null, PollInterval, PollInterval);
        }
        return Task.CompletedTask;
    }

    protected override Task StopAsync()
    {
        lock (_lock)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            _idleTimer?.Dispose();
            _idleTimer = null;
            _tickCount = 0;
        }
        return Task.CompletedTask;
    }

    /// <summary>Numbers each decided event, so one overtaken by a newer one isn't raised late.</summary>
    private long _eventSequence;

    /// <summary>Checks whether there was input since the last check; if so, idle time starts over.</summary>
    internal void OnPoll()
    {
        long sequence;
        lock (_lock)
        {
            var lastInput = ReadLastInputOr(_lastSeenInput);
            if (lastInput == _lastSeenInput)
                return;

            _lastSeenInput = lastInput;
            _idleTimer?.Change(_timerResolution, _timerResolution);

            if (_tickCount < 1)
                return;

            _tickCount = 0;
            sequence = ++_eventSequence;
        }

        Log.Instance.Trace($"User became active.");
        Raise(sequence, 0);
    }

    /// <summary>Another <see cref="_timerResolution"/> passed without input.</summary>
    internal void OnIdleTick()
    {
        long sequence;
        uint tickCount;
        lock (_lock)
        {
            tickCount = ++_tickCount;
            sequence = ++_eventSequence;
        }

        Log.Instance.Trace($"User is not active [time={_timerResolution * tickCount}]");
        Raise(sequence, tickCount);
    }

    /// <summary>
    /// Raises the event outside the lock, so handlers (automations) never run while it is held. An event that a
    /// newer one has already overtaken is dropped, so listeners never see an older idle state after a newer one.
    /// </summary>
    private void Raise(long sequence, uint tickCount)
    {
        if (Interlocked.Read(ref _eventSequence) != sequence)
            return;

        RaiseChanged(new ChangedEventArgs(_timerResolution, tickCount));
    }

    private uint ReadLastInputOr(uint fallback)
    {
        try
        {
            return _lastInputTime();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the last input time.", ex);
            return fallback;
        }
    }

    private static unsafe uint LastInputTimeFromWindows()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(&info))
            throw new InvalidOperationException($"GetLastInputInfo failed ({Marshal.GetLastWin32Error()}).");
        return info.dwTime;
    }
}
