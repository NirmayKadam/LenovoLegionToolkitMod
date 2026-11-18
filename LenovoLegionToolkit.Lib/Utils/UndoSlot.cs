using System;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

public enum UndoStatus
{
    /// <summary>Nothing to undo, and no recent result to show.</summary>
    None,

    /// <summary>A change can be undone until it expires.</summary>
    Pending,

    /// <summary>The undo is running.</summary>
    Running,

    /// <summary>Everything was put back.</summary>
    Done,

    /// <summary>The earlier settings are saved again, but the laptop couldn't be set to them right now.</summary>
    NotAllApplied,
}

/// <summary>What the undo bars show: the status, the change's description and, while pending, the time left.</summary>
public readonly record struct UndoView(UndoStatus Status, string Description, TimeSpan Remaining);

/// <summary>
/// The one change that can be undone right now (God Mode or fan curve saves). A newer change replaces the older
/// one; an offer expires after <see cref="Window"/>; an undo runs once. Kept in memory only: after a restart the
/// newer values, which are already saved, simply stay.
/// </summary>
public sealed class UndoSlot
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>How long the result of an undo stays on the bars.</summary>
    public static readonly TimeSpan ResultShownFor = TimeSpan.FromSeconds(6);

    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();

    private UndoStatus _status = UndoStatus.None;
    private string _description = string.Empty;
    private Func<Task<bool>>? _undo;
    private DateTime _untilUtc;

    public UndoSlot() : this(() => DateTime.UtcNow) { }

    /// <summary>For tests: the clock is supplied by the caller.</summary>
    internal UndoSlot(Func<DateTime> utcNow) => _utcNow = utcNow;

    /// <summary>Raised when a change is offered and when an undo starts or ends. May be raised on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Offers <paramref name="undo"/> for <see cref="Window"/>. It returns true when everything was put back, and
    /// false when the settings are back but the laptop couldn't be set to them (an exception counts as false).
    /// </summary>
    public void Offer(string description, Func<Task<bool>> undo)
    {
        lock (_lock)
        {
            if (_status == UndoStatus.Running)
                return;   // the running undo is about to change things again; don't mix the two

            _status = UndoStatus.Pending;
            _description = description;
            _undo = undo;
            _untilUtc = _utcNow() + Window;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public UndoView Current
    {
        get
        {
            lock (_lock)
            {
                var now = _utcNow();
                if ((_status is UndoStatus.Pending or UndoStatus.Done or UndoStatus.NotAllApplied) && now >= _untilUtc)
                    Reset();

                var remaining = _status == UndoStatus.Pending ? _untilUtc - now : TimeSpan.Zero;
                return new UndoView(_status, _description, remaining);
            }
        }
    }

    /// <summary>Runs the pending undo once. Returns its status, or <see cref="UndoStatus.None"/> when nothing (still) was pending.</summary>
    public async Task<UndoStatus> TryUndoAsync()
    {
        Func<Task<bool>> undo;
        string description;
        lock (_lock)
        {
            if (_status != UndoStatus.Pending || _undo is null || _utcNow() >= _untilUtc)
                return UndoStatus.None;

            undo = _undo;
            description = _description;
            _undo = null;
            _status = UndoStatus.Running;
        }

        Changed?.Invoke(this, EventArgs.Empty);

        bool allApplied;
        try
        {
            allApplied = await undo().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Undo failed: {description}", ex);
            allApplied = false;
        }

        var status = allApplied ? UndoStatus.Done : UndoStatus.NotAllApplied;
        lock (_lock)
        {
            _status = status;
            _untilUtc = _utcNow() + ResultShownFor;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return status;
    }

    private void Reset()
    {
        _status = UndoStatus.None;
        _description = string.Empty;
        _undo = null;
    }
}
