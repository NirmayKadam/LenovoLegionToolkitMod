using System;
using System.Diagnostics;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Tests.Performance;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>
/// Memory Windows hands back must be returned. Runs with no other test at the same time, since other tests' work
/// would move the numbers.
/// </summary>
[Collection(BudgetCollection.Name)]
public class NativeLeakTests
{
    [Fact]
    public void ReadingTheActivePowerPlanOften_ReturnsWindowsMemory()
    {
        // The power listener reads it every 500 ms; each read used to leave Windows' copy of the GUID behind.
        for (var i = 0; i < 1_000; i++)
            _ = PowerPlanExtensions.GetActiveSchemeGuid();

        using var process = Process.GetCurrentProcess();

        // The garbage collector gives memory back at its own moments (seen: rounds of -2 MB); a full collection before
        // and after keeps that out of the number. Measured with the leak put back on purpose: about 28 bytes per read.
        const int reads = 300_000;
        var before = PrivateMemoryAfterCollecting(process);

        for (var i = 0; i < reads; i++)
            _ = PowerPlanExtensions.GetActiveSchemeGuid();

        var grown = PrivateMemoryAfterCollecting(process) - before;

        Assert.True(grown < 3 * 1024 * 1024, $"private memory grew by {grown / 1024} KB over {reads} reads");
    }

    private static long PrivateMemoryAfterCollecting(Process process)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        return process.PrivateMemorySize64;
    }
}
