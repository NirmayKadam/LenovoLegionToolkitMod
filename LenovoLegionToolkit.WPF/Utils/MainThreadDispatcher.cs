using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

public class MainThreadDispatcher : IMainThreadDispatcher
{
    private readonly Func<Dispatcher> _dispatcher;

    public MainThreadDispatcher() : this(() => Application.Current.Dispatcher) { }

    /// <summary>For tests: a dispatcher running on a thread of their own.</summary>
    internal MainThreadDispatcher(Dispatcher dispatcher) : this(() => dispatcher) { }

    private MainThreadDispatcher(Func<Dispatcher> dispatcher) => _dispatcher = dispatcher;

    // Synchronous on purpose: callers in Lib rely on the work being done when this returns.
    public void Dispatch(Action callback) => _dispatcher().Invoke(callback);

    /// <summary>
    /// Runs <paramref name="callback"/> on the UI thread; the task completes when its async work has finished.
    /// On the UI thread it starts at once: the app's container starts listeners there and waits for them, so queuing
    /// the work behind that wait would freeze start-up.
    /// </summary>
    public Task DispatchAsync(Func<Task> callback)
    {
        var dispatcher = _dispatcher();
        return dispatcher.CheckAccess() ? callback() : dispatcher.InvokeAsync(callback).Task.Unwrap();
    }
}
