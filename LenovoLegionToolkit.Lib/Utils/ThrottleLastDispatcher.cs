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
            if (_cancellationTokenSource is not null)
                await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);

            _cancellationTokenSource = new();

            var token = _cancellationTokenSource.Token;

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
            if (_cancellationTokenSource is not null)
            {
                await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }

        if (tag is not null)
            Log.Instance.Trace($"Immediate dispatch... [tag={tag}]");

        await task().ConfigureAwait(false);
    }
}
