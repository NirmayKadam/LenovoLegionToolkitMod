using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class DispatcherExtensions
{
    /// <summary>
    /// Queues <paramref name="work"/> on the UI thread and returns at once, for event handlers that run on a
    /// background thread and don't need the result. An error is logged with the caller's name instead of reaching the
    /// app's global handler. Work queued once the app is shutting down is dropped (and logged).
    /// </summary>
    public static void Post(this Dispatcher dispatcher, Func<Task> work, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
    {
        if (IsShuttingDown(dispatcher, caller))
            return;

        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                LogFailure(ex, caller, file);
            }
        });
    }

    /// <inheritdoc cref="Post(Dispatcher, Func{Task}, string, string)"/>
    public static void Post(this Dispatcher dispatcher, Action work, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
    {
        if (IsShuttingDown(dispatcher, caller))
            return;

        dispatcher.InvokeAsync(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                LogFailure(ex, caller, file);
            }
        });
    }

    public static async Task InvokeIfRequired(this DispatcherObject dispatcherObject, Action action)
    {
        if (dispatcherObject.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            await dispatcherObject.Dispatcher.InvokeAsync(action);
        }
    }

    public static async Task<T> InvokeIfRequired<T>(this DispatcherObject dispatcherObject, Func<T> func)
    {
        if (dispatcherObject.Dispatcher.CheckAccess())
        {
            return func();
        }
        else
        {
            return await dispatcherObject.Dispatcher.InvokeAsync(func);
        }
    }

    private static bool IsShuttingDown(Dispatcher dispatcher, string caller)
    {
        if (!dispatcher.HasShutdownStarted)
            return false;

        Log.Instance.Trace($"UI work dropped: the app is shutting down. [caller={caller}]");
        return true;
    }

    private static void LogFailure(Exception ex, string caller, string file) =>
        Log.Instance.Trace($"UI work failed. [caller={caller}, file={System.IO.Path.GetFileName(file)}]", ex);
}
