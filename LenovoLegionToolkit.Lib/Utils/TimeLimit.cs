using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Bounds how long a caller waits for work it can't cancel: a WMI call, a driver request, a Windows service. When the
/// time is up the caller gets a <see cref="TimeoutException"/> and its normal error path, instead of waiting for good.
/// The work itself keeps its thread until Windows returns; if it then fails, that is logged rather than lost.
/// </summary>
public static class TimeLimit
{
    public static async Task<T> WithinAsync<T>(Task<T> work, TimeSpan limit, string what)
    {
        try
        {
            return await work.WaitAsync(limit).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!work.IsCompleted)
        {
            Log.Instance.Trace($"No answer within {limit.TotalSeconds:0} s. [{what}]");
            _ = work.ContinueWith(t => Log.Instance.Trace($"Late answer failed. [{what}]", t.Exception),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw new TimeoutException($"Windows didn't answer within {limit.TotalSeconds:0} s. [{what}]");
        }
    }

    public static async Task WithinAsync(Task work, TimeSpan limit, string what) =>
        await WithinAsync(work.ContinueWith(t =>
        {
            t.GetAwaiter().GetResult(); // completed here: passes the work's own exception on, never blocks
            return true;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default), limit, what).ConfigureAwait(false);
}
