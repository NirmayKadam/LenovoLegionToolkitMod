using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

public class MainThreadDispatcher : IMainThreadDispatcher
{
    // Synchronous on purpose: callers in Lib rely on the work being done when this returns.
    public void Dispatch(Action callback) => Application.Current.Dispatcher.Invoke(callback);

    // Completes when the async work itself has finished (Invoke returned at its first await).
    public Task DispatchAsync(Func<Task> callback) => Application.Current.Dispatcher.InvokeAsync(callback).Task.Unwrap();
}
