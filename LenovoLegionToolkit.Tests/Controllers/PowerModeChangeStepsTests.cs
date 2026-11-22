using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Listeners;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// The steps after a power-mode change (God Mode, Windows power mode and plan, overclocking). One that fails, such as
/// the firmware refusing a Custom-mode limit, used to skip every step after it, so the Windows power plan stayed on
/// the old mode.
/// </summary>
public class PowerModeChangeStepsTests
{
    [Fact]
    public async Task AFailedStep_DoesNotSkipTheOnesAfterIt()
    {
        var ran = new List<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => PowerModeListener.RunInOrderAsync(
            ("God Mode", () => throw new InvalidOperationException("The firmware refused a limit.")),
            ("Windows power mode", () => { ran.Add("power mode"); return Task.CompletedTask; }),
            ("Windows power plan", async () => { await Task.Yield(); ran.Add("power plan"); })));

        Assert.Equal(["power mode", "power plan"], ran);
    }

    [Fact]
    public async Task WhenSeveralFail_TheFirstFailureIsTheOneReported()
    {
        var first = new InvalidOperationException("first");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => PowerModeListener.RunInOrderAsync(
            ("one", () => Task.FromException(first)),
            ("two", () => Task.FromException(new InvalidOperationException("second")))));

        Assert.Same(first, thrown);
    }

    [Fact]
    public async Task WhenEveryStepWorks_NothingIsThrown_AndTheyRunInOrder()
    {
        var ran = new List<int>();

        await PowerModeListener.RunInOrderAsync(
            ("one", () => { ran.Add(1); return Task.CompletedTask; }),
            ("two", async () => { await Task.Yield(); ran.Add(2); }),
            ("three", () => { ran.Add(3); return Task.CompletedTask; }));

        Assert.Equal([1, 2, 3], ran);
    }
}
