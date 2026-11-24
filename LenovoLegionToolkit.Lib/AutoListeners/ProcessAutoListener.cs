using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public class ProcessAutoListener(
    InstanceStartedEventAutoAutoListener instanceStartedEventAutoAutoListener,
    InstanceStoppedEventAutoAutoListener instanceStoppedEventAutoAutoListener)
    : AbstractAutoListener<ProcessAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(ProcessEventInfoType type, ProcessInfo processInfo) : EventArgs
    {
        public ProcessEventInfoType Type { get; } = type;

        public ProcessInfo ProcessInfo { get; } = processInfo;
    }

    private static readonly object Lock = new();

    // ReSharper disable StringLiteralTypo

    private static readonly string[] IgnoredNames =
    [
        "backgroundTaskHost",
        "cmd",
        "CompPkgSrv",
        "conhost",
        "dllhost",
        "Lenovo Legion Toolkit",
        "msedge",
        "msedgewebview2",
        "NvOAWrapperCache",
        "SearchProtocolHost",
        "svchost",
        "taskhostw",
        "WmiApSrv",
        "WmiPrvSE"
    ];

    // ReSharper restore StringLiteralTypo

    private static readonly string[] IgnoredPaths =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    ];

    private readonly Dictionary<int, ProcessInfo> _processCache = [];

    /// <summary>The cache size at which the next clean-up runs (see <see cref="NextCleanUpAt"/>).</summary>
    private int _cleanUpAt = MinCleanUpAt;

    private const int MinCleanUpAt = 250;

    /// <summary>
    /// When to clean up next, given how many entries are left after a clean-up. With many programs really running (browser
    /// tabs), the cache can't shrink below 250, and a fixed limit then took a snapshot of every process on every start
    /// and stop. The limit grows with what is left instead.
    /// </summary>
    internal static int NextCleanUpAt(int leftAfterCleanUp) => Math.Max(MinCleanUpAt, leftAfterCleanUp * 2);

    protected override async Task StartAsync()
    {
        await instanceStartedEventAutoAutoListener.SubscribeChangedAsync(InstanceStartedEventAutoListener_Changed).ConfigureAwait(false);
        await instanceStoppedEventAutoAutoListener.SubscribeChangedAsync(InstanceStoppedEventAutoListener_Changed).ConfigureAwait(false);
    }

    protected override async Task StopAsync()
    {
        await instanceStartedEventAutoAutoListener.UnsubscribeChangedAsync(InstanceStartedEventAutoListener_Changed).ConfigureAwait(false);
        await instanceStoppedEventAutoAutoListener.UnsubscribeChangedAsync(InstanceStoppedEventAutoListener_Changed).ConfigureAwait(false);

        lock (Lock)
        {
            _processCache.Clear();
            _cleanUpAt = MinCleanUpAt;
        }
    }

    private void InstanceStartedEventAutoListener_Changed(object? sender, InstanceStartedEventAutoAutoListener.ChangedEventArgs e)
    {
        lock (Lock)
        {
            if (e.ProcessId < 0)
                return;

            if (string.IsNullOrWhiteSpace(e.ProcessName))
                return;

            if (IgnoredNames.Contains(e.ProcessName, StringComparer.OrdinalIgnoreCase))
                return;

            // Read without opening the process: every started program passes through here, games included.
            var processPath = ProcessImagePath.Get(e.ProcessId);
            if (processPath is null)
                Log.Instance.Trace($"Path of {e.ProcessName} not readable (ended already?). [processId={e.ProcessId}]");

            if (!string.IsNullOrEmpty(processPath) && IgnoredPaths.Any(p => processPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return;

            // With a path, the name comes from it: the start event cuts long names to 15 characters.
            var processInfo = string.IsNullOrEmpty(processPath)
                ? new ProcessInfo(e.ProcessName, null)
                : ProcessInfo.FromPath(processPath);
            _processCache[e.ProcessId] = processInfo;

            CleanUpCacheIfNecessary();

            RaiseChanged(new ChangedEventArgs(ProcessEventInfoType.Started, processInfo));
        }
    }

    private void InstanceStoppedEventAutoListener_Changed(object? sender, InstanceStoppedEventAutoAutoListener.ChangedEventArgs e)
    {
        lock (Lock)
        {
            CleanUpCacheIfNecessary();

            if (e.ProcessId < 0)
                return;

            if (string.IsNullOrWhiteSpace(e.ProcessName))
                return;

            if (IgnoredNames.Contains(e.ProcessName, StringComparer.OrdinalIgnoreCase))
                return;

            if (!_processCache.Remove(e.ProcessId, out var processInfo))
                return;

            RaiseChanged(new ChangedEventArgs(ProcessEventInfoType.Stopped, processInfo));
        }
    }

    private void CleanUpCacheIfNecessary()
    {
        if (_processCache.Count < _cleanUpAt)
            return;

        Log.Instance.Trace($"Cleaning up process cache. Current size: {_processCache.Count}.");

        // Which ids still run, from one snapshot of the process list (no process is opened).
        var processes = Process.GetProcesses();
        HashSet<int> running;
        try
        {
            running = processes.Select(p => p.Id).ToHashSet();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        foreach (var processId in _processCache.Keys.Where(id => !running.Contains(id)).ToList())
            _processCache.Remove(processId);

        _cleanUpAt = NextCleanUpAt(_processCache.Count);
        Log.Instance.Trace($"Cleaned up process cache. Current size: {_processCache.Count}; next clean-up at {_cleanUpAt}.");
    }
}
