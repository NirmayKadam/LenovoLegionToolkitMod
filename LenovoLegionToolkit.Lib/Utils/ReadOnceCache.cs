using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Keeps the first successful result of a read and returns it from then on, until <see cref="Clear"/>. A read
/// that fails is not kept, so the next call tries again. Concurrent first calls share one read.
/// </summary>
public sealed class ReadOnceCache<T>
{
    private sealed record Holder(T Value);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Holder? _held;

    public async Task<T> GetAsync(Func<Task<T>> read)
    {
        if (Volatile.Read(ref _held) is { } cached)
            return cached.Value;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_held is { } again)
                return again.Value;

            var value = await read().ConfigureAwait(false);
            Volatile.Write(ref _held, new Holder(value));
            return value;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Clear() => Volatile.Write(ref _held, null);
}
