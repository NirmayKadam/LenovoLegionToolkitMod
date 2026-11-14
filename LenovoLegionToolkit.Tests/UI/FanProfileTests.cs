using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

// The fan table access is a test double that records what the controller writes and answers reads from a script,
// so these tests cover the controller's decisions. Whether the real firmware keeps a curve is on the hardware checklist.
public sealed class FanProfileTests : IDisposable
{
    private static readonly ushort[] Curve = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static readonly ushort[] FirmwareCurve = [1, 1, 2, 3, 4, 5, 6, 7, 8, 10];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-fan-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private sealed class ScriptedFanTable : IFanTableAccess
    {
        public bool Allowed { get; set; } = true;
        public FanTableWriteOutcome WriteOutcome { get; set; } = FanTableWriteOutcome.Kept;

        /// <summary>What each read returns, in order; the last one repeats.</summary>
        public List<ushort[]> Reads { get; } = [];

        public List<ushort[]> Writes { get; } = [];
        public int ReadCount { get; private set; }

        /// <summary>Per-table answer to "is this allowed"; when unset, <see cref="Allowed"/> answers for every table.</summary>
        public Func<ushort[], bool>? IsAllowed { get; set; }

        public Task<bool> IsAllowedAsync(ushort[] table) => Task.FromResult(IsAllowed?.Invoke(table) ?? Allowed);

        public Task<FanTableWriteOutcome> WriteVerifiedAsync(ushort[] table)
        {
            Writes.Add(table);
            return Task.FromResult(WriteOutcome);
        }

        /// <summary>Awaited at the start of each read (argument: how many reads came before), to hold one open.</summary>
        public Func<int, Task>? BeforeRead { get; set; }

        public async Task<ushort[]> ReadAsync()
        {
            var index = ReadCount++;
            if (BeforeRead is not null)
                await BeforeRead(index);

            // With no script, the table behaves like firmware that keeps what was written last.
            return Reads.Count == 0 ? Writes[^1] : Reads[Math.Min(index, Reads.Count - 1)];
        }
    }

    // ---- Two mode changes close together ----

    [Fact]
    public async Task Apply_ForAnEarlierMode_WritesNothingOnceTheModeHasChanged()
    {
        ushort[] quietCurve = [1, 1, 1, 2, 3, 4, 5, 6, 8, 10];
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Performance);
        settings.Store.Curves[PowerModeState.Quiet] = quietCurve;

        // Quiet's re-check read is held open while the laptop moves on to Performance.
        var holdQuietRecheck = new TaskCompletionSource();
        var access = new ScriptedFanTable { BeforeRead = index => index == 0 ? holdQuietRecheck.Task : Task.CompletedTask };
        var controller = NewController(settings, access);

        _laptopMode = PowerModeState.Quiet;
        var quiet = controller.ApplyForModeAsync(PowerModeState.Quiet);
        _laptopMode = PowerModeState.Performance;
        var performance = controller.ApplyForModeAsync(PowerModeState.Performance);
        holdQuietRecheck.SetResult();
        await Task.WhenAll(quiet, performance);
        await controller.PendingCheck;

        Assert.Equal(Curve, access.Writes[^1]);                       // the table ends on Performance's curve
        Assert.Single(access.Writes, w => w.SequenceEqual(quietCurve)); // Quiet's curve was not written again
        Assert.Equal(FanCurveApplyResult.Applied, await quiet);        // Quiet's write itself went through; its check stopped
        Assert.Equal(FanCurveApplyResult.Applied, await performance);
        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Apply_NewerApply_WaitsForTheEarlierCheckToStop_BeforeWriting()
    {
        // Two applies never overlap: the newer one's write comes only after the earlier background check ended.
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var holdRecheck = new TaskCompletionSource();
        var access = new ScriptedFanTable { BeforeRead = index => index == 0 ? holdRecheck.Task : Task.CompletedTask };
        var controller = NewController(settings, access);

        await controller.ApplyForModeAsync(PowerModeState.Balance);
        var second = controller.ApplyForModeAsync(PowerModeState.Balance);

        Assert.False(second.IsCompleted);
        Assert.Single(access.Writes);

        holdRecheck.SetResult();
        Assert.Equal(FanCurveApplyResult.Applied, await second);
        Assert.Equal(2, access.Writes.Count);
    }

    // ---- Quick result, check afterwards ----

    [Fact]
    public async Task Apply_ReportsApplied_WithoutWaitingForTheLaterCheck()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable();
        var controller = NewController(settings, access);
        controller.RecheckDelay = TimeSpan.FromHours(1);

        var result = await controller.ApplyForModeAsync(PowerModeState.Balance).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(FanCurveApplyResult.Applied, result);
        Assert.Equal(0, access.ReadCount);          // the later check hasn't happened yet
        Assert.False(controller.PendingCheck.IsCompleted);

        await controller.ApplyForModeAsync(PowerModeState.Quiet);   // no curve for Quiet; still stops the waiting check
        await controller.PendingCheck.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Check_FirmwarePutsItsCurveBackOnce_RewritesWithoutComplaining()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve, Curve } };
        var controller = NewController(settings, access);
        var events = new List<FanCurveApplyResult>();
        controller.Applied += (_, e) => events.Add(e.Result);

        await controller.ApplyForModeAsync(PowerModeState.Balance);
        await controller.PendingCheck;

        Assert.Equal(2, access.Writes.Count);
        Assert.Equal([FanCurveApplyResult.Applied], events);
    }

    [Fact]
    public async Task Check_FirmwarePutsItsCurveBackTwice_ReportsRejectedLater()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };
        var controller = NewController(settings, access);
        var events = new List<FanCurveApplyResult>();
        controller.Applied += (_, e) => events.Add(e.Result);

        await controller.ApplyForModeAsync(PowerModeState.Balance);
        await controller.PendingCheck;

        Assert.Equal([FanCurveApplyResult.Applied, FanCurveApplyResult.Rejected], events);
        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Apply_WhenTheLaptopAlreadyLeftTheMode_WritesNothing()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Quiet);
        var access = new ScriptedFanTable();
        _laptopMode = PowerModeState.Performance;

        var result = await NewController(settings, access).ApplyForModeAsync(PowerModeState.Quiet);

        Assert.Equal(FanCurveApplyResult.Superseded, result);
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task Apply_ModeChangesBetweenWriteAndRecheck_IsNotARejection_AndIsNotRetried()
    {
        // After a mode change the firmware reports the new mode's curve. That is not the firmware refusing ours.
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };
        access.BeforeRead = _ => { _laptopMode = PowerModeState.Quiet; return Task.CompletedTask; };
        var controller = NewController(settings, access);
        var events = new List<FanCurveApplyResult>();
        controller.Applied += (_, e) => events.Add(e.Result);

        await controller.ApplyForModeAsync(PowerModeState.Balance);
        await controller.PendingCheck;

        Assert.Single(access.Writes);
        Assert.DoesNotContain(FanCurveApplyResult.Rejected, events);
    }

    [Fact]
    public async Task Apply_WhenTheCurrentModeCannotBeRead_WritesNothing()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable();
        var controller = new FanProfileController(settings, access, () => throw new InvalidOperationException("no answer")) { RecheckDelay = TimeSpan.Zero };

        var result = await controller.ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveApplyResult.Failed, result);
        Assert.Empty(access.Writes);
    }

    [Theory]
    [InlineData(FanTableWriteOutcome.NotKept)]
    [InlineData(FanTableWriteOutcome.Error)]
    public async Task Apply_NeverChangesWhatIsRememberedAboutTheFirmware(FanTableWriteOutcome outcome)
    {
        // A write that fails or isn't kept during a normal mode change says nothing reliable about the firmware.
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { WriteOutcome = outcome, Reads = { Curve } };

        await NewController(settings, access).ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
        Assert.Equal(FanCurveFirmwareSupport.Unknown, new FanProfileSettings(_dir).Store.FirmwareSupport);
    }

    [Fact]
    public async Task Apply_FirmwareCallFails_ReportsFailed_NotRejected()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { WriteOutcome = FanTableWriteOutcome.Error, Reads = { Curve } };

        var result = await NewController(settings, access).ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveApplyResult.Failed, result);
        Assert.Single(access.Writes);
    }

    private FanProfileSettings NewSettings(bool enabled = true, FanCurveFirmwareSupport support = FanCurveFirmwareSupport.Unknown, params PowerModeState[] modesWithCurve)
    {
        var settings = new FanProfileSettings(_dir);
        settings.Store.Enabled = enabled;
        settings.Store.FirmwareSupport = support;
        foreach (var mode in modesWithCurve)
            settings.Store.Curves[mode] = Curve;
        return settings;
    }

    /// <summary>The power mode the scripted laptop is in. Tests set it to say "the laptop is now in X".</summary>
    private PowerModeState _laptopMode = PowerModeState.Balance;

    private FanProfileController NewController(FanProfileSettings settings, IFanTableAccess access) =>
        new(settings, access, () => Task.FromResult(_laptopMode)) { RecheckDelay = TimeSpan.Zero };

    // ---- Which curve applies ----

    [Fact]
    public void CurveFor_ReturnsTheSavedCurve_OnlyWhenEnabledAndNotCustom()
    {
        var store = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance, PowerModeState.GodMode).Store;

        Assert.Equal(Curve, FanProfileController.CurveFor(store, PowerModeState.Balance));
        Assert.Null(FanProfileController.CurveFor(store, PowerModeState.Quiet));     // no curve set
        Assert.Null(FanProfileController.CurveFor(store, PowerModeState.GodMode));   // Custom keeps its preset's curve

        store.Enabled = false;
        Assert.Null(FanProfileController.CurveFor(store, PowerModeState.Balance));
    }

    [Fact]
    public void CurveFor_IgnoresACurveWithTheWrongNumberOfPoints()
    {
        var store = NewSettings().Store;
        store.Curves[PowerModeState.Performance] = [1, 2, 3];

        Assert.Null(FanProfileController.CurveFor(store, PowerModeState.Performance));
    }

    [Fact]
    public void EffectiveCurve_IsNull_WhenTheFirmwareRejectsCurves()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Rejects, PowerModeState.Balance);
        var controller = NewController(settings, new ScriptedFanTable());

        Assert.Null(controller.EffectiveCurve(PowerModeState.Balance));

        settings.Store.FirmwareSupport = FanCurveFirmwareSupport.Accepts;
        Assert.Equal(Curve, controller.EffectiveCurve(PowerModeState.Balance));
    }

    // ---- Whether a configured curve is in use (dashboard) ----

    [Theory]
    [InlineData(null, FanCurveSource.UserCurvePending)]                     // not written yet in this run
    [InlineData(FanCurveApplyResult.Applied, FanCurveSource.UserCurve)]
    [InlineData(FanCurveApplyResult.Rejected, FanCurveSource.UserCurveNotKept)]
    [InlineData(FanCurveApplyResult.Failed, FanCurveSource.UserCurveNotKept)]
    [InlineData(FanCurveApplyResult.Invalid, FanCurveSource.UserCurveNotKept)]
    public void CurveState_SaysWhetherTheCurveIsInUse(FanCurveApplyResult? last, FanCurveSource expected)
    {
        var (curve, source) = FanProfileController.CurveState(Curve, last);

        Assert.Equal(expected, source);
        Assert.Equal(expected == FanCurveSource.UserCurveNotKept ? null : Curve, curve);   // no bars for a curve not in use
    }

    [Fact]
    public void CurveState_WithoutAConfiguredCurve_IsTheFirmwares()
    {
        Assert.Equal((null, FanCurveSource.Firmware), FanProfileController.CurveState(null, FanCurveApplyResult.Applied));
    }

    [Fact]
    public async Task CurveInUse_FollowsTheLatestResult_AndIgnoresTestsAndApplyNow()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };
        var controller = NewController(settings, access);

        Assert.Equal(FanCurveSource.UserCurvePending, controller.CurveInUse(PowerModeState.Balance).Source);

        await controller.ApplyForModeAsync(PowerModeState.Balance);
        await controller.PendingCheck;   // the firmware puts its own curve back twice
        Assert.Equal(FanCurveSource.UserCurveNotKept, controller.CurveInUse(PowerModeState.Balance).Source);

        await controller.PreviewAsync(PowerModeState.Balance, [2, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        Assert.Equal(FanCurveSource.UserCurveNotKept, controller.CurveInUse(PowerModeState.Balance).Source);   // Apply now writes another curve
    }

    // ---- Applying on a mode change ----

    [Fact]
    public async Task Apply_WithoutACurve_WritesNothing()
    {
        var access = new ScriptedFanTable();
        var result = await NewController(NewSettings(), access).ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveApplyResult.NotApplicable, result);
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task Apply_CurveStays_ReportsApplied_WithoutRecordingSupport()
    {
        // Recording what the firmware supports is the user-started test's job (see the Probe tests below).
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { Curve } };

        var result = await NewController(settings, access).ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveApplyResult.Applied, result);
        Assert.Single(access.Writes);
        Assert.Equal(Curve, access.Writes[0]);
        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Apply_FirmwarePutsItsCurveBackTwice_ReportsRejected_WithoutRecordingSupport()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Performance);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };
        _laptopMode = PowerModeState.Performance;
        var controller = NewController(settings, access);
        var events = new List<FanCurveApplyResult>();
        controller.Applied += (_, e) => events.Add(e.Result);

        await controller.ApplyForModeAsync(PowerModeState.Performance);
        await controller.PendingCheck;

        Assert.Equal(FanCurveApplyResult.Rejected, events[^1]);   // found by the later check
        Assert.Equal(2, access.Writes.Count);                       // one retry
        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Apply_RevertedOnceThenKept_ReportsApplied()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Quiet);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve, Curve } };
        _laptopMode = PowerModeState.Quiet;
        var controller = NewController(settings, access);
        var events = new List<FanCurveApplyResult>();
        controller.Applied += (_, e) => events.Add(e.Result);

        var result = await controller.ApplyForModeAsync(PowerModeState.Quiet);
        await controller.PendingCheck;

        Assert.Equal(FanCurveApplyResult.Applied, result);
        Assert.Equal(2, access.Writes.Count);
        Assert.DoesNotContain(FanCurveApplyResult.Rejected, events);
    }

    [Fact]
    public async Task Apply_WriteNotConfirmed_ReportsRejected_WithoutRetrying()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { WriteOutcome = FanTableWriteOutcome.NotKept, Reads = { Curve } };

        var result = await NewController(settings, access).ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveApplyResult.Rejected, result);
        Assert.Single(access.Writes);
        Assert.Equal(0, access.ReadCount);
    }

    [Fact]
    public async Task Apply_CurveOutsideTheAllowedRange_IsNotWritten_AndSupportStaysUnknown()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { Allowed = false, Reads = { Curve } };

        var result = await NewController(settings, access).ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveApplyResult.Invalid, result);
        Assert.Empty(access.Writes);
        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Apply_OnALaptopKnownToReject_StopsTrying()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Rejects, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { Curve } };

        var result = await NewController(settings, access).ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal(FanCurveApplyResult.SkippedUnsupported, result);
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task Apply_DoesNotOverwriteAnEarlierAnswer()
    {
        // A revert during a normal apply (e.g. a mode change still settling) doesn't flip what the test recorded.
        var settings = NewSettings(true, FanCurveFirmwareSupport.Accepts, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };
        var controller = NewController(settings, access);
        var events = new List<FanCurveApplyResult>();
        controller.Applied += (_, e) => events.Add(e.Result);

        await controller.ApplyForModeAsync(PowerModeState.Balance);
        await controller.PendingCheck;

        Assert.Equal(FanCurveApplyResult.Rejected, events[^1]);
        Assert.Equal(FanCurveFirmwareSupport.Accepts, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Apply_RaisesAppliedWithModeAndResult()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var controller = NewController(settings, new ScriptedFanTable { Reads = { Curve } });
        (PowerModeState Mode, FanCurveApplyResult Result)? seen = null;
        controller.Applied += (_, e) => seen = e;

        await controller.ApplyForModeAsync(PowerModeState.Balance);

        Assert.Equal((PowerModeState.Balance, FanCurveApplyResult.Applied), seen);
    }

    // ---- The user-started test ----

    [Fact]
    public async Task Probe_InCustomMode_Throws_AndWritesNothing()
    {
        var access = new ScriptedFanTable { Reads = { Curve } };
        var controller = NewController(NewSettings(), access);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ProbeAsync(PowerModeState.GodMode, Curve));
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task Probe_ReplacesAnEarlierAnswer()
    {
        // The test is the user asking again, so its answer wins.
        var settings = NewSettings(true, FanCurveFirmwareSupport.Accepts);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };

        var result = (await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve)).Result;

        Assert.Equal(FanCurveApplyResult.Rejected, result);
        Assert.Equal(FanCurveFirmwareSupport.Rejects, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Probe_ChecksOnce_WithoutASecondWriteOfTheTestCurve()
    {
        // No mode change happens during a test, so a revert is the answer, not something to retry.
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };

        await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve);

        Assert.Single(access.Writes, w => w.SequenceEqual(Curve));
    }

    [Fact]
    public async Task Probe_PutsTheEarlierTableBack_WithoutAModeChange()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);   // no saved curve: the test curve must not stay
        var access = new ScriptedFanTable { Reads = { FirmwareCurve, Curve } };

        var probe = await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.Applied, probe.Result);
        Assert.False(probe.NeedsModeReapply);
        Assert.Equal(FirmwareCurve, access.Writes[^1]);   // what was there before the test
    }

    [Fact]
    public async Task Probe_KeepsTheCurve_WhenItIsTheSavedCurveForThisMode()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown, PowerModeState.Balance);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve, Curve } };

        var probe = await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.Applied, probe.Result);
        Assert.False(probe.NeedsModeReapply);
        Assert.Equal(Curve, access.Writes[^1]);
        Assert.Single(access.Writes);
    }

    [Fact]
    public async Task Probe_EarlierTableUnreadable_AsksForAModeReapply()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);
        var access = new ScriptedFanTable { BeforeRead = index => index == 0 ? throw new InvalidOperationException("no answer") : Task.CompletedTask };

        var probe = await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.Applied, probe.Result);
        Assert.True(probe.NeedsModeReapply);
        Assert.Single(access.Writes);   // nothing guessed in its place
    }

    [Fact]
    public async Task Probe_EarlierTableBelowTheMinimum_IsNotWrittenBack()
    {
        ushort[] belowMinimum = [0, 0, 0, 0, 0, 0, 0, 1, 3, 5];
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);
        var access = new ScriptedFanTable { Reads = { belowMinimum, Curve }, IsAllowed = t => !t.SequenceEqual(belowMinimum) };

        var probe = await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve);

        Assert.True(probe.NeedsModeReapply);
        Assert.DoesNotContain(access.Writes, w => w.SequenceEqual(belowMinimum));
    }

    // ---- "Apply now": try a curve without saving ----

    [Fact]
    public async Task Preview_WritesTheCurve_WithoutSavingIt()
    {
        ushort[] edited = [2, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var settings = NewSettings(true, FanCurveFirmwareSupport.Accepts, PowerModeState.Balance);
        var access = new ScriptedFanTable();

        var result = await NewController(settings, access).PreviewAsync(PowerModeState.Balance, edited);

        Assert.Equal(FanCurveApplyResult.Applied, result);
        Assert.Equal(edited, access.Writes[^1]);
        Assert.Equal(Curve, settings.Store.Curves[PowerModeState.Balance]);
        Assert.Equal(Curve, new FanProfileSettings(_dir).Store.Curves.GetValueOrDefault(PowerModeState.Balance, Curve));
    }

    [Fact]
    public async Task Preview_ForAModeTheLaptopIsNotIn_WritesNothing()
    {
        var access = new ScriptedFanTable();
        _laptopMode = PowerModeState.Quiet;

        var result = await NewController(NewSettings(), access).PreviewAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.Superseded, result);
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task Preview_OnALaptopKnownToReject_WritesNothing()
    {
        var access = new ScriptedFanTable();

        var result = await NewController(NewSettings(true, FanCurveFirmwareSupport.Rejects), access).PreviewAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.SkippedUnsupported, result);
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task Preview_BelowTheMinimum_WritesNothing()
    {
        var access = new ScriptedFanTable { Allowed = false };

        var result = await NewController(NewSettings(), access).PreviewAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.Invalid, result);
        Assert.Empty(access.Writes);
    }

    [Fact]
    public async Task Probe_CurveStays_RemembersAccepts_OnDisk()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);
        var access = new ScriptedFanTable { Reads = { Curve } };

        var result = (await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve)).Result;

        Assert.Equal(FanCurveApplyResult.Applied, result);
        Assert.Equal(FanCurveFirmwareSupport.Accepts, settings.Store.FirmwareSupport);
        Assert.Equal(FanCurveFirmwareSupport.Accepts, new FanProfileSettings(_dir).Store.FirmwareSupport);
    }

    [Fact]
    public async Task Probe_WriteNotKept_RemembersRejects()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);
        var access = new ScriptedFanTable { WriteOutcome = FanTableWriteOutcome.NotKept };

        var result = (await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve)).Result;

        Assert.Equal(FanCurveApplyResult.Rejected, result);
        Assert.Equal(FanCurveFirmwareSupport.Rejects, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Probe_FirmwareCallFails_LeavesTheAnswerAlone()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Accepts);
        var access = new ScriptedFanTable { WriteOutcome = FanTableWriteOutcome.Error };

        var result = (await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve)).Result;

        Assert.Equal(FanCurveApplyResult.Failed, result);
        Assert.Equal(FanCurveFirmwareSupport.Accepts, settings.Store.FirmwareSupport);
    }

    [Fact]
    public async Task Probe_ModeChangesDuringTheTest_LeavesTheAnswerAlone()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);
        var access = new ScriptedFanTable { Reads = { FirmwareCurve } };
        // Read 0 is the table from before the test; read 1 is the check, by which time the mode has changed.
        access.BeforeRead = index => { if (index == 1) _laptopMode = PowerModeState.Quiet; return Task.CompletedTask; };

        var probe = await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.Superseded, probe.Result);
        Assert.Single(access.Writes);   // the test curve was written, then the laptop moved on
        Assert.False(probe.NeedsModeReapply);   // the new mode brings its own curve
        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
    }

    // ---- The Custom preset made for a mode: one per mode, not one per click ----

    [Fact]
    public void UpsertFallbackPreset_CreatedTwice_LeavesOnePreset_WithTheNewerContent()
    {
        var existing = Guid.NewGuid();
        var presets = new Dictionary<Guid, GodModePreset> { [existing] = Template() };
        var ids = new Dictionary<PowerModeState, Guid>();

        var first = FanProfileController.UpsertFallbackPreset(presets, ids, PowerModeState.Balance, Template() with { Name = "first" });
        var second = FanProfileController.UpsertFallbackPreset(presets, ids, PowerModeState.Balance, Template() with { Name = "second" });

        Assert.Equal(first, second);
        Assert.Equal(2, presets.Count);                 // the user's own preset and one fallback
        Assert.Equal("second", presets[second].Name);
        Assert.Equal("Template", presets[existing].Name);
    }

    [Fact]
    public void UpsertFallbackPreset_KeepsOnePresetPerMode()
    {
        var presets = new Dictionary<Guid, GodModePreset>();
        var ids = new Dictionary<PowerModeState, Guid>();

        var balance = FanProfileController.UpsertFallbackPreset(presets, ids, PowerModeState.Balance, Template());
        var quiet = FanProfileController.UpsertFallbackPreset(presets, ids, PowerModeState.Quiet, Template());

        Assert.NotEqual(balance, quiet);
        Assert.Equal(2, presets.Count);
    }

    [Fact]
    public void UpsertFallbackPreset_AfterTheUserDeletedIt_CreatesItAgain()
    {
        var presets = new Dictionary<Guid, GodModePreset>();
        var ids = new Dictionary<PowerModeState, Guid>();
        var first = FanProfileController.UpsertFallbackPreset(presets, ids, PowerModeState.Balance, Template());
        presets.Remove(first);

        var second = FanProfileController.UpsertFallbackPreset(presets, ids, PowerModeState.Balance, Template());

        Assert.Single(presets);
        Assert.Equal(second, ids[PowerModeState.Balance]);
    }

    [Fact]
    public void FallbackPresetIds_SurviveARestart()
    {
        var id = Guid.NewGuid();
        var settings = NewSettings();
        settings.Store.FallbackPresetIds[PowerModeState.Performance] = id;
        settings.SynchronizeStore();

        Assert.Equal(id, new FanProfileSettings(_dir).Store.FallbackPresetIds[PowerModeState.Performance]);
    }

    [Fact]
    public async Task Probe_Invalid_LeavesTheAnswerAlone()
    {
        var settings = NewSettings(true, FanCurveFirmwareSupport.Unknown);
        var access = new ScriptedFanTable { Allowed = false, Reads = { Curve } };

        var probe = await NewController(settings, access).ProbeAsync(PowerModeState.Balance, Curve);

        Assert.Equal(FanCurveApplyResult.Invalid, probe.Result);
        Assert.Empty(access.Writes);
        Assert.False(probe.NeedsModeReapply);   // nothing was written, so nothing to undo
        Assert.Equal(FanCurveFirmwareSupport.Unknown, settings.Store.FirmwareSupport);
    }

    // ---- Custom-mode fallback preset ----

    private static StepperValue Step(int value, int min = 0, int max = 200) => new(value, min, max, 1, [], null);

    private static GodModePreset Template() => new()
    {
        Name = "Template",
        CPULongTermPowerLimit = Step(65, 5, 100),
        CPUShortTermPowerLimit = Step(95, 5, 120),
        CPUPeakPowerLimit = Step(130, 5, 150),
        CPUTemperatureLimit = Step(95, 70, 100),
        GPUConfigurableTGP = Step(90, 35, 100),
        GPUPowerBoost = Step(10, 0, 25),
        GPUTemperatureLimit = null,
        EnableOverclocking = true,
        EnableAllCoreCurveOptimizer = true,
        FanFullSpeed = true,
        FanTableInfo = new FanTableInfo(
            [new FanTableData(FanTableType.CPU, 1, 1, [1000, 2000], [50, 60])],
            new FanTable(FirmwareCurve)),
    };

    [Fact]
    public void BuildCustomPreset_TakesTheModesFirmwareLimitsAndTheCurve()
    {
        var defaults = new GodModeDefaults { CPULongTermPowerLimit = 55, CPUShortTermPowerLimit = 90, CPUTemperatureLimit = 90, GPUConfigurableTGP = 80 };

        var preset = FanProfileController.BuildCustomPreset(Template(), defaults, Curve, "Balance + my fan curve");

        Assert.Equal("Balance + my fan curve", preset.Name);
        Assert.Equal(55, preset.CPULongTermPowerLimit!.Value.Value);
        Assert.Equal(90, preset.CPUShortTermPowerLimit!.Value.Value);
        Assert.Equal(90, preset.CPUTemperatureLimit!.Value.Value);
        Assert.Equal(80, preset.GPUConfigurableTGP!.Value.Value);
        Assert.Equal(Curve, preset.FanTableInfo!.Value.Table.GetTable());
        Assert.Single(preset.FanTableInfo!.Value.Data);   // the firmware's temperature steps are kept
    }

    [Fact]
    public void BuildCustomPreset_KeepsTheTemplateValue_WhenTheFirmwareDoesNotReportOne()
    {
        var defaults = new GodModeDefaults { CPUPeakPowerLimit = 0, GPUPowerBoost = null, GPUTemperatureLimit = 87 };

        var preset = FanProfileController.BuildCustomPreset(Template(), defaults, Curve, "x");

        Assert.Equal(130, preset.CPUPeakPowerLimit!.Value.Value);   // 0 = not provided
        Assert.Equal(10, preset.GPUPowerBoost!.Value.Value);        // null = not provided
        Assert.Null(preset.GPUTemperatureLimit);                    // not adjustable on this laptop: stays absent
    }

    [Fact]
    public void BuildCustomPreset_TakesAReportedZero_WhereZeroIsAllowed()
    {
        // GPU power boost 0 means "off" and is inside its range (0–25); a CPU limit of 0 is not (minimum 5).
        var defaults = new GodModeDefaults { GPUPowerBoost = 0, CPULongTermPowerLimit = 0 };

        var preset = FanProfileController.BuildCustomPreset(Template(), defaults, Curve, "x");

        Assert.Equal(0, preset.GPUPowerBoost!.Value.Value);
        Assert.Equal(65, preset.CPULongTermPowerLimit!.Value.Value);
    }

    [Fact]
    public void BuildCustomPreset_SettingWithFixedSteps_LandsOnAStep()
    {
        // Step-list settings carry no min/max (both 0), so clamping to that range would turn every value into 0.
        var template = Template() with { GPUConfigurableTGP = new StepperValue(100, 0, 0, 0, [80, 100, 115], null) };
        var defaults = new GodModeDefaults { GPUConfigurableTGP = 112 };

        var preset = FanProfileController.BuildCustomPreset(template, defaults, Curve, "x");

        Assert.Equal(115, preset.GPUConfigurableTGP!.Value.Value);
    }

    [Fact]
    public void BuildCustomPreset_DoesNotCopyTheTemplatesWindowsPowerChoices()
    {
        var template = Template() with { Overrides = new Dictionary<PowerOverrideKey, string> { [PowerOverrideKey.PowerPlan] = "some-plan" } };

        var preset = FanProfileController.BuildCustomPreset(template, new GodModeDefaults(), Curve, "x");

        Assert.Empty(preset.Overrides!);
    }

    [Fact]
    public void BuildCustomPreset_KeepsValuesWithinTheAllowedRange()
    {
        var defaults = new GodModeDefaults { CPULongTermPowerLimit = 140, GPUConfigurableTGP = 20 };

        var preset = FanProfileController.BuildCustomPreset(Template(), defaults, Curve, "x");

        Assert.Equal(100, preset.CPULongTermPowerLimit!.Value.Value);   // max 100
        Assert.Equal(35, preset.GPUConfigurableTGP!.Value.Value);       // min 35
    }

    [Fact]
    public void BuildCustomPreset_TurnsOffFullSpeedAndOverclocking()
    {
        var preset = FanProfileController.BuildCustomPreset(Template(), new GodModeDefaults(), Curve, "x");

        Assert.False(preset.FanFullSpeed);
        Assert.False(preset.EnableOverclocking);
        Assert.False(preset.EnableAllCoreCurveOptimizer);
    }

    [Fact]
    public void BuildCustomPreset_WithoutFanData_HasNoFanTable()
    {
        var template = Template() with { FanTableInfo = null };

        var preset = FanProfileController.BuildCustomPreset(template, new GodModeDefaults(), Curve, "x");

        Assert.Null(preset.FanTableInfo);
    }
}
