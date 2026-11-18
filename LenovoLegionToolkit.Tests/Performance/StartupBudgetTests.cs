using System;
using System.Diagnostics;
using Autofac;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;
using Xunit.Abstractions;

namespace LenovoLegionToolkit.Tests.Performance;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BudgetCollection
{
    public const string Name = "Budget";
}

/// <summary>
/// A budget for building the app's dependency-injection container from its four real modules. It catches a change
/// that makes that part of start-up much slower or much heavier (for example a registration that scans or builds a
/// lot). It is NOT the app's real start-up: that is mostly WMI, driver and UI work that can't run without the
/// laptop. The container is built with start-up components skipped, so none of the app's listeners (WMI, driver,
/// keyboard hook) or UI managers start, and no hardware is touched.
/// </summary>
[Collection(BudgetCollection.Name)]
public class StartupBudgetTests(ITestOutputHelper output)
{
    /// <summary>
    /// Measured on the development laptop (Lenovo LOQ 15IRH8) on 2026-11-18: best of 3 warm builds 1.0 ms and
    /// 0.62 MB allocated. The allocation budget is about 3× that. The time budget is 10×, because a 1 ms
    /// measurement on a shared CI machine can easily triple from scheduling noise alone.
    /// </summary>
    private static readonly TimeSpan TimeBudget = TimeSpan.FromMilliseconds(BudgetMilliseconds);
    private const double BudgetMilliseconds = 10;
    private const long AllocationBudgetBytes = 2L * 1024 * 1024;

    /// <summary>Set when the marker component is created; it must not be.</summary>
    private static bool _markerActivated;

    private sealed class AutoActivateMarker
    {
        public AutoActivateMarker() => _markerActivated = true;
    }

    private static IContainer BuildAppContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule(new Lib.IoCModule());
        builder.RegisterModule(new Lib.Automation.IoCModule());
        builder.RegisterModule(new Lib.Macro.IoCModule());
        builder.RegisterModule(new WPF.IoCModule());
        builder.RegisterType<AutoActivateMarker>().AutoActivate();

        // Skips IStartable and AutoActivate components: the listeners and UI managers are registered but not created.
        return builder.Build(Autofac.Builder.ContainerBuildOptions.IgnoreStartableComponents);
    }

    [Fact]
    public void BuildingTheContainer_StartsNothing()
    {
        _markerActivated = false;
        using var container = BuildAppContainer();

        Assert.False(_markerActivated);
        Assert.True(container.IsRegistered<UndoSlot>());   // the real modules really were registered
    }

    [Fact]
    public void ContainerBuild_StaysWithinItsBudget()
    {
        using (BuildAppContainer()) { }   // loads and compiles the code once, so the measurement is of the build itself

        var best = TimeSpan.MaxValue;
        long allocated = long.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();

            using (var container = BuildAppContainer())
            {
                // A couple of plain singletons, to include resolving. Settings classes are left out on purpose:
                // creating one finds the real settings folder.
                container.Resolve<UndoSlot>();
                container.Resolve<AntiCheatMonitor>();
            }

            stopwatch.Stop();
            best = stopwatch.Elapsed < best ? stopwatch.Elapsed : best;
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"Container build: best {best.TotalMilliseconds:0.0} ms of 3 (budget {TimeBudget.TotalMilliseconds} ms), {allocated / 1024.0 / 1024.0:0.00} MB allocated (budget {AllocationBudgetBytes / 1024.0 / 1024.0:0.##} MB)");

        Assert.True(best <= TimeBudget, $"Building the container took {best.TotalMilliseconds:0.0} ms; budget {TimeBudget.TotalMilliseconds} ms.");
        Assert.True(allocated <= AllocationBudgetBytes, $"Building the container allocated {allocated} bytes; budget {AllocationBudgetBytes}.");
    }
}
