using System;
using System.IO;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// While MSI Afterburner runs it manages the GPU's clocks, so LLT must not send its own overclock, and above all
/// must not "switch off" by writing zero offsets over Afterburner's undervolt. Whether Afterburner runs is supplied
/// by the test. The check comes before anything reaches the GPU driver, so these tests never touch the real GPU.
/// Settings live in a test folder.
/// </summary>
public sealed class GPUOverclockAfterburnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-oc-afterburner-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private GPUOverclockController Create(bool enabled, GPUOverclockInfo info, out GPUOverclockSettings settings)
    {
        settings = new GPUOverclockSettings(_dir);
        settings.Store.Enabled = enabled;
        settings.Store.Info = info;
        settings.SynchronizeStore();
        return new GPUOverclockController(settings, new OverclockApplyGuard(settings),
            new VantageDisabler(), new LegionSpaceDisabler(), new LegionZoneDisabler(), () => true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // the dashboard toggle and the automation step
    public async Task WhileAfterburnerRuns_NothingIsSent_AndTheSettingsStay(bool force)
    {
        var values = new GPUOverclockInfo(100, 300, 0, 0);
        var controller = Create(true, values, out _);

        var result = await controller.ApplyStateAsync(force);

        Assert.Equal(GPUOverclockApplyResult.SkippedForAfterburner, result);
        var saved = new GPUOverclockSettings(_dir).Store;
        Assert.True(saved.Enabled);
        Assert.Equal(values, saved.Info);
        Assert.Null(saved.PendingInfo);   // the crash guard never began an apply
    }

    /// <summary>
    /// LLT's P-state lock is a clock change too. With Afterburner running it returns before NVIDIA's API is touched
    /// (the GPU controller's steps are supplied by the test and never run).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]   // back to dynamic
    public async Task PStateLock_WhileAfterburnerRuns_IsNotSent(int pState)
    {
        var gpu = new GPUController(() => Task.CompletedTask, _ => Task.CompletedTask, (_, _) => { }, () => 1000, 0, TimeSpan.FromSeconds(1), () => true);

        Assert.Equal(PStateApplyResult.Skipped, await gpu.ApplyPStateAsync(pState));
    }

    /// <summary>Switching LLT's overclock off sends zero offsets; with Afterburner running that would undo its undervolt.</summary>
    [Fact]
    public async Task SwitchingOff_WhileAfterburnerRuns_DoesNotSendZeroOffsets()
    {
        var controller = Create(false, GPUOverclockInfo.Zero, out _);

        Assert.Equal(GPUOverclockApplyResult.SkippedForAfterburner, await controller.ApplyStateAsync(force: true));
    }
}
