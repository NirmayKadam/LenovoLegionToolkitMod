using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autofac;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// <see cref="MainThreadDispatcher"/> on a real WPF dispatcher running on its own STA thread, the way the app's UI
/// thread runs.
/// </summary>
public sealed class MainThreadDispatcherTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;

    public MainThreadDispatcherTests()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(Timeout);
        _dispatcher = dispatcher!;
    }

    // Without waiting: when a test finds the UI thread frozen, InvokeShutdown would wait for it forever. The frozen
    // thread is a background thread, so it ends with the test run.
    public void Dispose()
    {
        if (!_dispatcher.HasShutdownStarted)
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread.Join(Timeout);
    }

    /// <summary>
    /// The app builds its container on the UI thread, and Autofac waits there for each auto-started listener's
    /// StartAsync. Listeners that create windows start through DispatchAsync, so it must not wait for the UI thread
    /// it is already on (2.36.15.0 froze at start-up with trace logging off this way).
    /// </summary>
    [Fact]
    public async Task AListenerStartedWhileTheContainerIsBuiltOnTheUiThread_DoesntFreezeIt()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new MainThreadDispatcher(_dispatcher)).As<IMainThreadDispatcher>();
        builder.Register<WindowCreatingListener>().AutoActivateListener();

        var build = _dispatcher.InvokeAsync(() =>
        {
            using var container = builder.Build();
            return container.Resolve<WindowCreatingListener>().StartedOn;
        }).Task;

        var finished = await Task.WhenAny(build, Task.Delay(Timeout));
        Assert.True(finished == build, "Building the container on the UI thread froze it.");
        Assert.Equal(_dispatcher, await build);
    }

    /// <summary>From another thread, it returns only when the async work has finished, and runs it on the UI thread.</summary>
    [Fact]
    public async Task FromAnotherThread_ItCompletesWhenTheAsyncWorkHasFinished_OnTheUiThread()
    {
        var dispatcher = new MainThreadDispatcher(_dispatcher);
        Dispatcher? ranOn = null;
        var finished = false;

        await Task.Run(() => dispatcher.DispatchAsync(async () =>
        {
            ranOn = Dispatcher.CurrentDispatcher;
            await Task.Delay(50);
            finished = true;
        })).WaitAsync(Timeout);

        Assert.True(finished);
        Assert.Equal(_dispatcher, ranOn);
    }

    private sealed class WindowCreatingListener(IMainThreadDispatcher mainThreadDispatcher) : IListener<EventArgs>
    {
        public event EventHandler<EventArgs>? Changed;

        public Dispatcher? StartedOn { get; private set; }

        public Task StartAsync() => mainThreadDispatcher.DispatchAsync(() =>
        {
            StartedOn = Dispatcher.CurrentDispatcher;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });

        public Task StopAsync() => Task.CompletedTask;
    }
}
