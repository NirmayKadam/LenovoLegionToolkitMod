using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using LenovoLegionToolkit.Tests.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>Tests that replace the app-wide logger run alone, so no other test writes to it meanwhile.</summary>
[CollectionDefinition(nameof(AppLoggerCollection), DisableParallelization = true)]
public sealed class AppLoggerCollection;

/// <summary>
/// <see cref="DispatcherExtensions.Post(Dispatcher, Func{Task}, string, string)"/> on a real WPF dispatcher running on
/// its own STA thread, the way the app's UI thread runs.
/// </summary>
[Collection(nameof(AppLoggerCollection))]
public sealed class DispatcherPostTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private int _unhandled;

    public DispatcherPostTests()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, e) =>
            {
                Interlocked.Increment(ref _unhandled);
                e.Handled = true;
            };
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(Timeout);
        _dispatcher = dispatcher!;
    }

    public void Dispose()
    {
        if (!_dispatcher.HasShutdownStarted)
            _dispatcher.InvokeShutdown();
        _thread.Join(Timeout);
    }

    [Fact]
    public async Task Post_ReturnsAtOnce_EvenWhileTheUiThreadIsBusy()
    {
        using var release = new ManualResetEventSlim();
        _ = _dispatcher.InvokeAsync(() => release.Wait(Timeout));   // the UI thread is busy
        var ran = new TaskCompletionSource();

        var sw = Stopwatch.StartNew();
        _dispatcher.Post(() => ran.SetResult());
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"Post waited {sw.Elapsed}.");
        Assert.False(ran.Task.IsCompleted);

        release.Set();
        await ran.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task AsyncWork_RunsToTheEnd_OnTheUiThread()
    {
        var result = new TaskCompletionSource<bool>();

        _dispatcher.Post(async () =>
        {
            await Task.Delay(20);
            result.SetResult(Dispatcher.CurrentDispatcher == _dispatcher);
        });

        Assert.True(await result.Task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Errors_AreCaughtWhereTheyHappen_NotLeftForAGlobalHandler()
    {
        // Without Post's catch, an error inside InvokeAsync is kept in its task and only shows up later, without
        // the caller's name, as an unobserved task exception when that task is collected.
        var marker = Guid.NewGuid().ToString("N");
        var leaked = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.Flatten().InnerExceptions.Any(x => x.Message.Contains(marker)))
                Interlocked.Increment(ref leaked);
            e.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var after = new TaskCompletionSource();

            _dispatcher.Post((Action)(() => throw new InvalidOperationException($"sync failure {marker}")));
            _dispatcher.Post(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException($"async failure {marker}");
            });
            _dispatcher.Post(async () =>
            {
                await Task.Delay(50);
                after.SetResult();
            });

            await after.Task.WaitAsync(Timeout);
            await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.Equal(0, Volatile.Read(ref leaked));
            Assert.Equal(0, Volatile.Read(ref _unhandled));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Fact]
    public async Task Errors_AreLogged_WithTheCallersName()
    {
        // A synchronous error inside InvokeAsync is swallowed by WPF without a trace, so Post's log line is the
        // only record of it. A logger writing to a temporary folder takes the place of the app's for this test.
        using var testLog = new TestLog();
        {
            var marker = Guid.NewGuid().ToString("N");
            var after = new TaskCompletionSource();

            // A cast, because a lambda that only throws would pick the Func<Task> overload.
            _dispatcher.Post((Action)(() => throw new InvalidOperationException($"plain-failure {marker}")));
            _dispatcher.Post(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException($"awaited-failure {marker}");
            });
            _dispatcher.Post(async () =>
            {
                await Task.Delay(50);
                after.SetResult();
            });
            await after.Task.WaitAsync(Timeout);

            var log = testLog.Text;
            Assert.Contains($"plain-failure {marker}", log);
            Assert.Contains($"awaited-failure {marker}", log);
            Assert.Contains($"caller={nameof(Errors_AreLogged_WithTheCallersName)}", log);
        }
    }

    [Fact]
    public void WorkPostedAfterShutdownStarts_IsDropped_WithoutAnError_AndSaidSo()
    {
        // After shutdown nothing could run anyway; what Post decides is whether that is an error. It isn't: the work
        // is dropped on purpose and the log says so, once per call.
        using var testLog = new TestLog();
        _dispatcher.InvokeShutdown();
        _thread.Join(Timeout);

        var error = Record.Exception(() =>
        {
            _dispatcher.Post(() => { });
            _dispatcher.Post(() => Task.CompletedTask);
        });

        Assert.Null(error);
        var dropped = Regex.Matches(testLog.Text, Regex.Escape("UI work dropped: the app is shutting down.")).Count;
        Assert.Equal(2, dropped);
        Assert.DoesNotContain("UI work failed", testLog.Text);
    }

    /// <summary><c>Dispatcher.Invoke(async …)</c> in any spelling: a field or local named dispatcher, spaces, a cast.</summary>
    private static readonly Regex BlockingInvokeWithAsyncWork =
        new(@"dispatcher\s*\.\s*Invoke\s*\(\s*(\([^)]*\)\s*)?async\b", RegexOptions.IgnoreCase);

    [Fact]
    public void TheBlockingInvokeCheck_FindsEverySpelling()
    {
        Assert.Matches(BlockingInvokeWithAsyncWork, "Dispatcher.Invoke(async () => await X());");
        Assert.Matches(BlockingInvokeWithAsyncWork, "_dispatcher.Invoke( async () => {});");
        Assert.Matches(BlockingInvokeWithAsyncWork, "Dispatcher.Invoke((Func<Task>)async () => {});");
        Assert.DoesNotMatch(BlockingInvokeWithAsyncWork, "Dispatcher.InvokeAsync(async () => {});");
        Assert.DoesNotMatch(BlockingInvokeWithAsyncWork, "Dispatcher.Invoke(() => Refresh());");
    }

    [Fact]
    public void NoAsyncWorkIsPassedToTheBlockingInvoke()
    {
        var wpf = Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF");
        var offenders = Directory.EnumerateFiles(wpf, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => BlockingInvokeWithAsyncWork.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }
}
