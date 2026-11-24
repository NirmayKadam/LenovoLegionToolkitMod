using System;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Work that runs once at a time: callers that arrive while it runs share that run instead of starting another. A run
/// that finishes at once (on the caller's own thread) is never kept as "running", so the next caller always gets a new
/// run. Before, the sensor update could keep a finished run as the one in progress, and from then on every update
/// waited for it and did nothing: the sensors stopped until Indra was restarted.
/// </summary>
public sealed class SingleFlight
{
    private readonly object _lock = new();
    private Task? _running;

    /// <summary>The run in progress, or a new run of <paramref name="work"/>.</summary>
    public Task RunAsync(Func<Task> work)
    {
        lock (_lock)
        {
            if (_running is not null)
                return _running;

            var run = RunAndForgetAsync(work);
            if (!run.IsCompleted)
                _running = run;
            return run;
        }
    }

    /// <summary>True while a run is in progress.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _running is not null;
        }
    }

    private async Task RunAndForgetAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
                _running = null;
        }
    }
}
