using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Settings;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Runs the Health check two minutes after start-up and then once a day, and notifies only about items that newly
/// need attention. Only reads Windows; never changes a setting.
/// </summary>
public sealed class HealthMonitor(HealthSettings settings) : IDisposable
{
    private static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private Timer? _timer;

    /// <summary>The items of the last check, or empty before the first one.</summary>
    public IReadOnlyList<HealthItem> Items { get; private set; } = [];

    public DateTime? LastCheckUtc => settings.Store.LastCheckUtc;

    public int AttentionCount => Items.Count(i => i.State == HealthState.Attention);

    /// <summary>Raised after every check (on a background thread).</summary>
    public event EventHandler? Updated;

    public void Start() => _timer ??= new Timer(_ => _ = CheckAsync(notify: true), null, FirstCheckAfter, CheckEvery);

    /// <summary>
    /// Checks now. With <paramref name="notify"/> off (the page's Check now), new items are only marked as seen: you are
    /// looking at them already.
    /// </summary>
    public async Task CheckAsync(bool notify)
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var inputs = await HealthCollector.CollectAsync().ConfigureAwait(false);
            var items = HealthReport.Evaluate(inputs, DateTime.Now);
            var (toNotify, notified) = HealthNotifier.Next(settings.Store.Notified, items);

            Items = items;
            settings.Store.Notified = [.. notified];
            settings.Store.LastCheckUtc = DateTime.UtcNow;
            settings.SynchronizeStore();

            Log.Instance.Trace($"Health check done. [attention={string.Join(",", items.Where(i => i.State == HealthState.Attention).Select(i => i.Check))}, notify={toNotify.Count}]");

            if (notify && toNotify.Count > 0)
                MessagingCenter.Publish(new NotificationMessage(NotificationType.HealthAttention, string.Join(", ", toNotify.Select(i => i.Title))));

            try
            {
                Updated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Showing the health check failed.", ex);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Health check failed.", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
