using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class TaskExtensions
{
    public static ValueTask AsValueTask(this Task task) => new(task);

    public static Task<T?> OrNullIfException<T>(this Task<T> task) where T : struct
    {
        return task.ContinueWith(t => t.IsCompletedSuccessfully ? (T?)t.Result : null);
    }

    /// <summary>
    /// Lets <paramref name="task"/> run on its own, logging a failure as "<paramref name="what"/> failed". A background
    /// task started and left alone used to lose its error, or have it surface later as an unexpected-error pop-up.
    /// </summary>
    public static void Forget(this Task task, string what) => _ = ObserveAsync(task, what);

    /// <summary>Awaits <paramref name="task"/> and logs its failure; never throws (cancellation is not a failure).</summary>
    internal static async Task ObserveAsync(Task task, string what)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose.
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"{what} failed.", ex);
        }
    }
}
