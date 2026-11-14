using System;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// One hardware reading shared by everyone who asks within a short window. The dashboard sensors, the status strip
/// and the HWiNFO integration each read the same sensors every second; with this they share one real read instead
/// of making three. A read already in progress is shared too. A failed read is not kept, so the next caller tries
/// again. Values are never made up: a caller gets either a fresh read or one at most <c>maxAge</c> old.
/// </summary>
public sealed class SharedReading<T>
{
    private readonly TimeSpan _maxAge;
    private readonly Func<long> _nowMs;
    private readonly object _lock = new();

    private Task<T>? _inFlight;
    private T _last = default!;
    private long _lastAtMs;
    private bool _hasValue;

    public SharedReading(TimeSpan maxAge) : this(maxAge, () => Environment.TickCount64) { }

    /// <summary>For tests: the clock is supplied by the caller.</summary>
    internal SharedReading(TimeSpan maxAge, Func<long> nowMs)
    {
        _maxAge = maxAge;
        _nowMs = nowMs;
    }

    private bool IsFresh => _hasValue && _nowMs() - _lastAtMs < _maxAge.TotalMilliseconds;

    /// <summary>The shared value if it is fresh enough, the read already in progress, or a new read.</summary>
    public Task<T> GetAsync(Func<Task<T>> read)
    {
        lock (_lock)
        {
            if (IsFresh)
                return Task.FromResult(_last);

            if (_inFlight is not null)
                return _inFlight;

            var reading = ReadAndKeepAsync(read);
            if (!reading.IsCompleted)
                _inFlight = reading;
            return reading;
        }
    }

    /// <summary>Synchronous form for readings that are synchronous (e.g. driver calls). Concurrent callers wait for one read.</summary>
    public T Get(Func<T> read)
    {
        lock (_lock)
        {
            if (IsFresh)
                return _last;

            var value = read();
            Keep(value);
            return value;
        }
    }

    /// <summary>Forgets the shared value, so the next caller reads again.</summary>
    public void Invalidate()
    {
        lock (_lock)
            _hasValue = false;
    }

    private async Task<T> ReadAndKeepAsync(Func<Task<T>> read)
    {
        try
        {
            var value = await read().ConfigureAwait(false);
            lock (_lock)
                Keep(value);
            return value;
        }
        finally
        {
            lock (_lock)
                _inFlight = null;
        }
    }

    private void Keep(T value)
    {
        _last = value;
        _lastAtMs = _nowMs();
        _hasValue = true;
    }
}
