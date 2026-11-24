using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;

namespace LenovoLegionToolkit.WPF.Controls;

public abstract class AbstractRefreshingControl : UserControl
{
    private Task? _refreshTask;

    /// <summary>A refresh was asked for while one was running; it runs again once that one ends.</summary>
    private bool _refreshAgain;

    /// <summary>The card is hidden because its last refresh failed (not because the feature isn't supported).</summary>
    private bool _hiddenAfterFailure;

    /// <summary>How many times in a row a failed refresh has been tried again.</summary>
    private int _retries;

    protected bool IsRefreshing => _refreshTask is not null;

    protected virtual bool DisablesWhileRefreshing => true;

    /// <summary>The wait before trying a failed refresh again: 5 s, 15 s, then 45 s; after that the card stays hidden.</summary>
    internal static TimeSpan? RetryDelay(int retriesSoFar) => retriesSoFar switch
    {
        0 => TimeSpan.FromSeconds(5),
        1 => TimeSpan.FromSeconds(15),
        2 => TimeSpan.FromSeconds(45),
        _ => null
    };

    /// <summary>For tests: how long to wait instead of <see cref="RetryDelay"/> (null: the real waits).</summary>
    internal Func<int, TimeSpan?>? RetryDelayForTests { get; set; }

    protected AbstractRefreshingControl()
    {
        IsEnabled = false;

        Loaded += RefreshingControl_Loaded;
        IsVisibleChanged += RefreshingControl_IsVisibleChanged;
    }

    private void RefreshingControl_Loaded(object sender, RoutedEventArgs e)
    {
        OnFinishedLoading();
    }

    protected abstract void OnFinishedLoading();

    private async void RefreshingControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    protected async Task RefreshAsync()
    {
        if (_refreshTask is { } running)
        {
            // A state change while the card was being read: the read in progress may already have the old state, so
            // it is read again once it ends (joining it used to show the old state until the next change).
            _refreshAgain = true;
            try
            {
                await running;
            }
            catch
            {
                // The refresh that is running reports its own error.
            }
            return;
        }

        Log.Instance.Trace($"Refreshing control... [feature={GetType().Name}]");

        var unsupported = false;
        var failed = false;

        try
        {
            if (DisablesWhileRefreshing)
                IsEnabled = false;

            _refreshTask = OnRefreshAsync();
            await _refreshTask;
        }
        catch (NotSupportedException)
        {
            unsupported = true;

            Log.Instance.Trace($"Unsupported. [feature={GetType().Name}]");
        }
        catch (Exception ex)
        {
            failed = true;

            Log.Instance.Trace($"Exception when refreshing control. [feature={GetType().Name}]", ex);
        }
        finally
        {
            _refreshTask = null;

            if (unsupported || failed)
            {
                if (AppFlags.DebugFlagOn)
                {
                    Visibility = Visibility.Visible;
                    IsEnabled = true;
                }
                else
                {
                    Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                IsEnabled = true;
                _retries = 0;

                // Hidden after a failure that has passed (a slow answer from the firmware at start-up): shown again.
                if (_hiddenAfterFailure)
                {
                    _hiddenAfterFailure = false;
                    Visibility = Visibility.Visible;
                }
            }
        }

        if (failed && !AppFlags.DebugFlagOn)
        {
            // A hidden card is never shown again by itself (it can't become visible to refresh), so a failure that
            // has passed used to hide it for the rest of the session. It is tried again a few times.
            _hiddenAfterFailure = true;
            ScheduleRetry();
        }

        if (_refreshAgain)
        {
            _refreshAgain = false;
            if (!unsupported && !failed)
                await RefreshAsync();
        }
    }

    private void ScheduleRetry()
    {
        var delay = (RetryDelayForTests ?? RetryDelay)(_retries);
        if (delay is not { } wait)
        {
            Log.Instance.Trace($"Still failing after {_retries} retries; the card stays hidden. [feature={GetType().Name}]");
            return;
        }

        _retries++;
        Task.Delay(wait).ContinueWith(_ => Dispatcher.Post(async () =>
        {
            // Taken off the page meanwhile (the dashboard was rebuilt): nothing to retry.
            if (!_hiddenAfterFailure || !IsLoaded)
                return;

            await RefreshAsync();
        }), TaskScheduler.Default);
    }

    protected abstract Task OnRefreshAsync();
}
