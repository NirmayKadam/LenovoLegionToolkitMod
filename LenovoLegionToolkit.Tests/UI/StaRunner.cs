using System;
using System.Runtime.ExceptionServices;
using System.Threading;

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
}
