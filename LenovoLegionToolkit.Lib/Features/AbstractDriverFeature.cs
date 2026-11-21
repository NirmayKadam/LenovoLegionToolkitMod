using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32.SafeHandles;

namespace LenovoLegionToolkit.Lib.Features;

internal static class GlobalDriverLock
{
    public static readonly SemaphoreSlim Queue = new(1, 1);

    /// <summary>How long a request waits for its turn, and for the driver to answer. Requests normally take milliseconds.</summary>
    public static readonly TimeSpan AnswerTimeLimit = TimeSpan.FromSeconds(15);
}

public abstract class AbstractDriverFeature<T>(
    Func<SafeFileHandle> driverHandleHandle,
    uint controlCode,
    bool useDriverQueue = false)
    : IFeature<T>, IDisposable
    where T : struct, Enum, IComparable
{
    private const int DRIVER_COOLDOWN_MS = 20;

    protected readonly uint ControlCode = controlCode;
    protected readonly Func<SafeFileHandle> DriverHandle = driverHandleHandle;
    protected readonly bool UseQueue = useDriverQueue;

    protected T LastState;
    private CancellationTokenSource? _lastSetCts;

    public virtual async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            _ = await GetStateInternalAsync(bypassQueue: true).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public virtual Task<T[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<T>());

    public virtual Task<T> GetStateAsync() => GetStateInternalAsync(bypassQueue: false);

    protected virtual async Task<T> GetStateInternalAsync(bool bypassQueue)
    {
        Log.Instance.Trace($"Getting state... [feature={GetType().Name}]");
        var outBuffer = await SendCodeAsync(DriverHandle(), ControlCode, GetInBufferValue(), bypassQueue).ConfigureAwait(false);
        var state = await FromInternalAsync(outBuffer).ConfigureAwait(false);
        LastState = state;
        return state;
    }

    public virtual Task SetStateAsync(T state) => SetStateAsync(state, verify: true);

    public virtual async Task SetStateAsync(T state, bool verify)
    {
        _lastSetCts?.Cancel();
        _lastSetCts = new CancellationTokenSource();
        var ct = _lastSetCts.Token;

        try
        {
            Log.Instance.Trace($"Setting state to {state}... [feature={GetType().Name}]");

            var codes = await ToInternalAsync(state).ConfigureAwait(false);
            foreach (var code in codes)
            {
                ct.ThrowIfCancellationRequested();
                await SendCodeAsync(DriverHandle(), ControlCode, code, bypassQueue: false).ConfigureAwait(false);
            }

            LastState = state;

            if (verify)
                await VerifyStateSetAsync(state, ct).ConfigureAwait(false);

            Log.Instance.Trace($"State set to {state} [feature={GetType().Name}]");
        }
        catch (OperationCanceledException)
        {
            Log.Instance.Trace($"SetStateAsync cancelled for {state} [feature={GetType().Name}]");
        }
    }

    protected abstract Task<T> FromInternalAsync(uint state);
    protected abstract uint GetInBufferValue();
    protected abstract Task<uint[]> ToInternalAsync(T state);

    protected async Task<uint> SendCodeAsync(SafeFileHandle handle, uint controlCode, uint inBuffer, bool bypassQueue = false)
    {
        Task<uint> CoreAction() => Task.Run(() =>
        {
            if (PInvokeExtensions.DeviceIoControl(handle, controlCode, inBuffer, out uint outBuffer))
                return outBuffer;

            var error = Marshal.GetLastWin32Error();
            // The request is not repeated here (it may have been a write); the next one gets a fresh handle.
            if (Drivers.IsStaleHandleError(error))
                Drivers.ForgetEnergy(handle);
            throw new InvalidOperationException($"DeviceIoControl failed, error: {error}");
        });

        var what = $"driver request, feature={GetType().Name}, code={controlCode}";

        if (!UseQueue || bypassQueue)
        {
            return await TimeLimit.WithinAsync(CoreAction(), GlobalDriverLock.AnswerTimeLimit, what).ConfigureAwait(false);
        }

        if (!await GlobalDriverLock.Queue.WaitAsync(GlobalDriverLock.AnswerTimeLimit).ConfigureAwait(false))
            throw new TimeoutException($"The Lenovo driver is still busy with an earlier request. [{what}]");

        Task<uint> work;
        try
        {
            work = CoreAction();
        }
        catch
        {
            GlobalDriverLock.Queue.Release();
            throw;
        }

        // The queue is freed only once the driver has really answered (plus a short cooldown), even if this caller
        // stops waiting: two requests are never in the driver at the same time.
        _ = work.ContinueWith(async _ =>
        {
            try
            {
                await Task.Delay(DRIVER_COOLDOWN_MS).ConfigureAwait(false);
            }
            finally
            {
                GlobalDriverLock.Queue.Release();
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();

        return await TimeLimit.WithinAsync(work, GlobalDriverLock.AnswerTimeLimit, what).ConfigureAwait(false);
    }

    private async Task VerifyStateSetAsync(T state, CancellationToken ct)
    {
        var retries = 0;
        while (retries < 10)
        {
            if (ct.IsCancellationRequested) return;

            var currentState = await GetStateInternalAsync(bypassQueue: true).ConfigureAwait(false);
            if (state.Equals(currentState))
            {
                Log.Instance.Trace($"Verify state {state} succeeded. [feature={GetType().Name}]");
                return;
            }

            retries++;
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        Log.Instance.Trace($"Verify state {state} failed after 10 retries. [feature={GetType().Name}]");
    }

    public void Dispose()
    {
        _lastSetCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
