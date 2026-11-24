using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Tasks = System.Threading.Tasks;
using WpfThreading = System.Windows.Threading;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// Runs test code on a single-threaded apartment thread, which WPF objects need.
/// </summary>
internal static class StaRunner
{
    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>
    /// Runs async test code on an STA thread with a running WPF dispatcher, so awaits and posted work come back to it
    /// as they do on the app's UI thread. Gives up after <paramref name="timeout"/> (default 30 s).
    /// </summary>
    public static void RunWithDispatcher(Func<Tasks.Task> action, TimeSpan? timeout = null) => Run(() =>
    {
        var dispatcher = WpfThreading.Dispatcher.CurrentDispatcher;
        var frame = new WpfThreading.DispatcherFrame();
        Tasks.Task? work = null;

        dispatcher.InvokeAsync(() =>
        {
            work = action();
            work.ContinueWith(_ => frame.Continue = false, Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        });

        using var giveUp = new Timer(_ => dispatcher.InvokeAsync(() => frame.Continue = false), null, timeout ?? TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        WpfThreading.Dispatcher.PushFrame(frame);

        if (work is not { IsCompleted: true })
            throw new TimeoutException("The test's UI work didn't finish in time.");
        work.GetAwaiter().GetResult();
    });
}
