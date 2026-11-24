using System;
using System.Globalization;
using System.Threading;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Notices when Indra's window stops answering. On its own background thread (not the thread pool, which a busy app
/// can starve), it asks the window for a tiny no-op every 2 s. After 15 s without an answer it writes an error report
/// (when, for how long, the last lines of the log); when the window answers again it says so in a notification. It
/// never ends, restarts or changes anything.
/// </summary>
public sealed class UiFreezeWatchdog : IDisposable
{
    private static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FreezeAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(10);
    private const int ReportedLogLines = 60;

    private readonly Dispatcher _dispatcher;
    private readonly FreezeDetector _detector = new(FreezeAfter, SleepGap);
    private readonly object _lock = new();
    private readonly ManualResetEventSlim _stop = new(false);

    private Thread? _thread;
    private bool _answerPending;
    private DateTime _frozenSinceLocal;

    public UiFreezeWatchdog(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start()
    {
        lock (_lock)
        {
            if (_thread is not null)
                return;

            _thread = new Thread(Loop) { IsBackground = true, Name = "Indra freeze watchdog", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }
    }

    /// <summary>Stops watching (at shutdown, so closing is never taken for a freeze). Never throws.</summary>
    public void Stop()
    {
        try
        {
            _stop.Set();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        try
        {
            while (!_stop.Wait(TickEvery))
            {
                var now = Environment.TickCount64;
                TimeSpan? silentFor;
                bool ask;
                lock (_lock)
                {
                    silentFor = _detector.OnTick(now);
                    ask = !_answerPending;   // one question at a time: a frozen window isn't flooded with them
                    _answerPending = true;
                }

                if (silentFor is { } silent)
                    Report(silent);

                if (ask && !_dispatcher.HasShutdownStarted)
                    _dispatcher.BeginInvoke(DispatcherPriority.Normal, Answer);
            }
        }
        catch (Exception ex)
        {
            // The watchdog must never take Indra down with it.
            Log.Instance.Trace($"The freeze watchdog stopped.", ex);
        }
    }

    private void Answer()
    {
        TimeSpan? recovered;
        lock (_lock)
        {
            _answerPending = false;
            recovered = _detector.OnAnswer(Environment.TickCount64);
        }

        if (recovered is { } duration)
        {
            Log.Instance.Trace($"The window answers again after {duration.TotalSeconds:0} s.");
            MessagingCenter.Publish(new NotificationMessage(NotificationType.UiFreeze,
                string.Format(Resource.UiFreeze_Notification, (int)Math.Round(duration.TotalSeconds),
                    _frozenSinceLocal.ToString("HH:mm", CultureInfo.CurrentCulture))));
        }
    }

    private void Report(TimeSpan silentFor)
    {
        _frozenSinceLocal = DateTime.Now - silentFor;
        var tail = string.Join(Environment.NewLine, Log.Instance.TailLines(ReportedLogLines));
        var details = $"The window hasn't answered for {silentFor.TotalSeconds:0} s (since {_frozenSinceLocal:HH:mm:ss})."
                      + Environment.NewLine + "Last log lines:" + Environment.NewLine
                      + (tail.Length > 0 ? tail : "(none: Detailed log is off)");

        Log.Instance.Trace($"The window stopped answering. [silentFor={silentFor.TotalSeconds:0}s]");
        Log.Instance.ErrorReport("Indra's window stopped answering (freeze watchdog; nothing was ended or changed).", new TimeoutException(details));
    }
}
