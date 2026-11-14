using System;
using System.IO;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

// A real gpu_oc.json in a temp folder. "The next run" is a new settings object and a new guard reading that same
// file, which is what a restarted app does. The NVIDIA driver itself can't be driven from a test; applying real
// values is on the hardware checklist.
public sealed class OverclockApplyGuardTests : IDisposable
{
    private static readonly GPUOverclockInfo Values = new(150, 300);
    private static readonly GPUOverclockInfo OtherValues = new(200, 400);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-gpuoc-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>A run of the app: its own settings object and guard over the shared file.</summary>
    private (GPUOverclockSettings Settings, OverclockApplyGuard Guard) NewRun()
    {
        var settings = new GPUOverclockSettings(_dir);
        return (settings, new OverclockApplyGuard(settings));
    }

    private (GPUOverclockSettings Settings, OverclockApplyGuard Guard) FirstRunWithOverclockOn(GPUOverclockInfo info)
    {
        var run = NewRun();
        run.Settings.Store.Enabled = true;
        run.Settings.Store.Info = info;
        run.Settings.SynchronizeStore();
        return run;
    }

    [Fact]
    public void Begin_SavesTheNoteToDisk_BeforeAnythingIsApplied()
    {
        var (_, guard) = FirstRunWithOverclockOn(Values);

        Assert.True(guard.Begin(Values));

        Assert.Equal(Values, new GPUOverclockSettings(_dir).Store.PendingInfo);
    }

    [Fact]
    public void NextRun_AfterAnApplyThatWasNeverConfirmed_TurnsOverclockOff_AndKeepsTheValues()
    {
        var (_, crashed) = FirstRunWithOverclockOn(Values);
        crashed.Begin(Values);
        // ... the laptop freezes here: no confirmation, no clean exit.

        var (settings, guard) = NewRun();
        var recovered = guard.RecoverFromInterruptedApply();

        Assert.True(recovered);
        Assert.False(settings.Store.Enabled);
        Assert.Equal(Values, settings.Store.Info);              // kept, so the user can see and lower them
        Assert.Null(settings.Store.PendingInfo);
        Assert.True(guard.DisabledAfterInterruptedApply);

        // And it is on disk, not just in memory.
        var reread = new GPUOverclockSettings(_dir).Store;
        Assert.False(reread.Enabled);
        Assert.Null(reread.PendingInfo);
        Assert.True(reread.DisabledAfterInterruptedApply);
    }

    [Fact]
    public void Recovery_HappensOncePerRun_AndNotForThisRunsOwnApply()
    {
        var (_, guard) = FirstRunWithOverclockOn(Values);

        Assert.False(guard.RecoverFromInterruptedApply());   // nothing left over
        guard.Begin(Values);                                  // this run's own apply, still unconfirmed
        Assert.False(guard.RecoverFromInterruptedApply());   // must not be mistaken for a previous run's
    }

    [Fact]
    public void NextRun_AfterTheValuesWereConfirmedStable_KeepsOverclockOn()
    {
        var (_, first) = FirstRunWithOverclockOn(Values);
        first.Begin(Values);
        first.ConfirmStable(Values);

        var (settings, guard) = NewRun();

        Assert.False(guard.RecoverFromInterruptedApply());
        Assert.True(settings.Store.Enabled);
        Assert.Equal(Values, settings.Store.StableInfo);
        Assert.False(guard.DisabledAfterInterruptedApply);
    }

    [Fact]
    public void ApplyingStableValuesAgain_SavesNoNote()
    {
        var (settings, guard) = FirstRunWithOverclockOn(Values);
        guard.Begin(Values);
        guard.ConfirmStable(Values);

        Assert.False(guard.Begin(Values));   // e.g. re-applied after every power mode change
        Assert.Null(settings.Store.PendingInfo);
        Assert.Null(new GPUOverclockSettings(_dir).Store.PendingInfo);
    }

    [Fact]
    public void NewValues_NeedTheirOwnConfirmation_EvenAfterOthersProvedStable()
    {
        var (settings, guard) = FirstRunWithOverclockOn(Values);
        guard.Begin(Values);
        guard.ConfirmStable(Values);

        Assert.True(guard.Begin(OtherValues));
        Assert.Equal(OtherValues, settings.Store.PendingInfo);
    }

    [Fact]
    public void ConfirmationForOlderValues_DoesNotConfirmNewerOnes()
    {
        var (settings, guard) = FirstRunWithOverclockOn(Values);
        guard.Begin(Values);
        guard.Begin(OtherValues);      // changed again before the first ones proved stable

        guard.ConfirmStable(Values);   // the first timer fires

        Assert.Equal(OtherValues, settings.Store.PendingInfo);
        Assert.Null(settings.Store.StableInfo);
    }

    [Fact]
    public void CleanExit_RemovesTheNote_WithoutCallingTheValuesStable()
    {
        var (_, first) = FirstRunWithOverclockOn(Values);
        first.Begin(Values);
        first.Abandon();   // normal exit or Windows shutdown

        var (settings, guard) = NewRun();

        Assert.False(guard.RecoverFromInterruptedApply());
        Assert.True(settings.Store.Enabled);
        Assert.Null(settings.Store.StableInfo);
        Assert.True(guard.Begin(Values));   // still unproven, so the next apply is watched again
    }

    [Fact]
    public void StockValues_NeedNoNote_AndClearOne()
    {
        var (settings, guard) = FirstRunWithOverclockOn(Values);
        guard.Begin(Values);

        Assert.False(guard.Begin(GPUOverclockInfo.Zero));   // switching the overclock off
        Assert.Null(settings.Store.PendingInfo);
    }

    [Fact]
    public void TurningOverclockOnAgain_ClearsTheSafetyFlag()
    {
        var (_, crashed) = FirstRunWithOverclockOn(Values);
        crashed.Begin(Values);
        var (settings, guard) = NewRun();
        guard.RecoverFromInterruptedApply();

        guard.AcknowledgeDisabled();

        Assert.False(guard.DisabledAfterInterruptedApply);
        Assert.False(new GPUOverclockSettings(_dir).Store.DisabledAfterInterruptedApply);
        Assert.False(settings.Store.Enabled);   // acknowledging alone doesn't switch it on
    }

    [Fact]
    public void NoteRemovedWhenOverclockingTurnsOutUnsupported_NextRunShowsNoSafetyWarning()
    {
        // IsSupportedAsync clears the settings when the laptop doesn't support GPU overclocking; it now also
        // removes a pending note, so the next start doesn't claim a crash.
        var (_, first) = FirstRunWithOverclockOn(Values);
        first.Begin(Values);
        first.Abandon();

        var (_, guard) = NewRun();

        Assert.False(guard.RecoverFromInterruptedApply());
        Assert.False(guard.DisabledAfterInterruptedApply);
    }

    // ---- Limits on what is sent to the driver ----

    [Theory]
    [InlineData(150, 150)]     // inside both ranges
    [InlineData(450, 250)]     // above the driver's maximum
    [InlineData(-450, -100)]   // below the driver's minimum
    public void ClampDelta_StaysInsideTheDriversRange(int requested, int expected)
    {
        Assert.Equal(expected, OverclockApplyGuard.ClampDeltaMhz(requested, -500, 500, driverMinKhz: -100_000, driverMaxKhz: 250_000));
    }

    [Fact]
    public void ClampDelta_RoundsInwards_WhenTheDriversRangeIsNotWholeMhz()
    {
        Assert.Equal(250, OverclockApplyGuard.ClampDeltaMhz(400, -500, 500, driverMinKhz: -100_500, driverMaxKhz: 250_900));
        Assert.Equal(-100, OverclockApplyGuard.ClampDeltaMhz(-400, -500, 500, driverMinKhz: -100_500, driverMaxKhz: 250_900));
    }

    [Fact]
    public void ClampDelta_NeverGoesPastLltsOwnLimits_EvenIfTheDriverAllowsMore()
    {
        Assert.Equal(500, OverclockApplyGuard.ClampDeltaMhz(900, -500, 500, driverMinKhz: -1_000_000, driverMaxKhz: 1_000_000));
    }

    [Fact]
    public void ClampDelta_WithoutADriverRange_UsesLltsOwnLimits()
    {
        Assert.Equal(500, OverclockApplyGuard.ClampDeltaMhz(900, -500, 500, null, null));
        Assert.Equal(120, OverclockApplyGuard.ClampDeltaMhz(120, -500, 500, null, null));
    }

    [Theory]
    [InlineData(900)]
    [InlineData(-300)]
    [InlineData(0)]
    public void ClampDelta_DriverAllowsOnlyZero_SendsZero(int requested)
    {
        // A range of 0 to 0 is what a GPU with locked overclocking reports: no offset is allowed.
        Assert.Equal(0, OverclockApplyGuard.ClampDeltaMhz(requested, -500, 500, driverMinKhz: 0, driverMaxKhz: 0));
    }

    [Theory]
    [InlineData(900, 900)]      // below the GPU's top voltage: as asked
    [InlineData(1150, 1000)]    // above it: capped at the GPU's own top voltage
    [InlineData(600, 700)]      // below LLT's minimum
    public void ClampVoltageLock_NeverExceedsTheGpusOwnTopVoltage(int requested, int expected)
    {
        Assert.Equal(expected, OverclockApplyGuard.ClampVoltageLockMv(requested, 700, 1200, curveMaxMv: 1000));
    }

    [Fact]
    public void ClampVoltageLock_NeverExceedsLltsOwnLimit()
    {
        Assert.Equal(1200, OverclockApplyGuard.ClampVoltageLockMv(1300, 700, 1200, curveMaxMv: 1400));
    }

    [Theory]
    [InlineData(null)]   // the curve couldn't be read
    [InlineData(0)]
    [InlineData(650)]    // the GPU's top voltage is below anything LLT would lock to
    public void ClampVoltageLock_SendsNothing_WhenNoSafeValueExists(int? curveMaxMv)
    {
        Assert.Null(OverclockApplyGuard.ClampVoltageLockMv(900, 700, 1200, curveMaxMv));
    }
}
