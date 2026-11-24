using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

public class ThrottleLastDispatcher(TimeSpan interval, string? tag = null)
{
    private CancellationTokenSource? _cancellationTokenSource;

    public async Task DispatchAsync(Func<Task> task)
    {
        try
        {
            // Swapped in one step: two events at once (plugging in the charger raises two) used to both cancel the
            // same earlier wait and then both run. Now each newer call cancels the one before it, and only the last runs.
            var mine = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _cancellationTokenSource, mine);
            if (previous is not null)
                await previous.CancelAsync().ConfigureAwait(false);

            var token = mine.Token;

            await Task.Delay(interval, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            if (tag is not null)
                Log.Instance.Trace($"Allowing... [tag={tag}]");

            await task().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (tag is not null)
                Log.Instance.Trace($"Throttling... [tag={tag}]");
        }
    }

    /// <summary>
    /// Like <see cref="DispatchAsync"/>, for event handlers (<c>async void</c>): an error in <paramref name="task"/> is
    /// logged and goes no further. Raised on a listener's thread, it would otherwise end the whole app.
    /// </summary>
    public async Task DispatchLoggingErrorsAsync(Func<Task> task)
    {
        try
        {
            await DispatchAsync(task).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Throttled work failed. [tag={tag}]", ex);
        }
    }

    public async Task DispatchImmediateAsync(Func<Task> task)
    {
        try
        {
            if (Volatile.Read(ref _cancellationTokenSource) is { } waiting)
                await waiting.CancelAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        if (tag is not null)
            Log.Instance.Trace($"Immediate dispatch... [tag={tag}]");

        await task().ConfigureAwait(false);
    }
}
