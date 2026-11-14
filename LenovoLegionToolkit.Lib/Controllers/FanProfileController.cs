using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

/// <summary>Reading and writing the laptop's active fan table.</summary>
public interface IFanTableAccess
{
    /// <summary>Whether the table stays within the laptop's minimum and the firmware's maximum.</summary>
    Task<bool> IsAllowedAsync(ushort[] table);

    /// <summary>Writes the table and checks it reads back.</summary>
    Task<FanTableWriteOutcome> WriteVerifiedAsync(ushort[] table);

    /// <summary>The table the firmware reports right now.</summary>
    Task<ushort[]> ReadAsync();
}

/// <summary>What a fan table write came to.</summary>
public enum FanTableWriteOutcome
{
    /// <summary>The table was written and read back unchanged.</summary>
    Kept,

    /// <summary>The firmware answered, but the table it reports is not the one written.</summary>
    NotKept,

    /// <summary>The call itself failed (firmware busy, laptop going to sleep, …). Says nothing about support.</summary>
    Error,
}

/// <summary>The real fan table, through Lenovo's WMI fan methods and the God Mode controller's checks.</summary>
public class GodModeFanTableAccess(GodModeController godModeController) : IFanTableAccess
{
    public Task<bool> IsAllowedAsync(ushort[] table) => table.Length == 10
        ? godModeController.IsValidFanTableAsync(new FanTable(table))
        : Task.FromResult(false);

    public async Task<FanTableWriteOutcome> WriteVerifiedAsync(ushort[] table)
    {
        try
        {
            return await godModeController.SetFanTableWithVerifyAsync(new FanTable(table)).ConfigureAwait(false)
                ? FanTableWriteOutcome.Kept
                : FanTableWriteOutcome.NotKept;
        }
        catch (FanTableNotKeptException ex)
        {
            Log.Instance.Trace($"The firmware did not keep the fan table.", ex);
            return FanTableWriteOutcome.NotKept;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Writing the fan table failed.", ex);
            return FanTableWriteOutcome.Error;
        }
    }

    public async Task<ushort[]> ReadAsync()
    {
        var raw = await WMI.LenovoFanMethod.FanGetTableAsync(1, 1).ConfigureAwait(false);
        return raw.Select(v => (ushort)v).ToArray();
    }
}

/// <summary>What happened when a mode's curve was due.</summary>
public enum FanCurveApplyResult
{
    /// <summary>No curve for this mode (or the feature is off, or it's Custom mode): the firmware's curve stays.</summary>
    NotApplicable,

    /// <summary>The curve was written and was still in place after the re-check.</summary>
    Applied,

    /// <summary>The firmware reverted or refused the curve.</summary>
    Rejected,

    /// <summary>Skipped because this laptop is known to reject curves outside Custom mode.</summary>
    SkippedUnsupported,

    /// <summary>The curve is below the laptop's minimum or above the maximum; not written.</summary>
    Invalid,

    /// <summary>The power mode changed (or a newer apply started) before this one finished; it stopped writing.</summary>
    Superseded,

    /// <summary>The firmware call failed. Says nothing about whether the firmware supports curves.</summary>
    Failed,
}

/// <summary>What the user-started test came to.</summary>
/// <param name="Result">Whether the firmware kept the test curve (<see cref="FanCurveApplyResult.Applied"/>) or put its own back (<see cref="FanCurveApplyResult.Rejected"/>), or why there's no answer.</param>
/// <param name="NeedsModeReapply">
/// The fans couldn't be put back on the table they had before the test (it couldn't be read or written back, or
/// it is below LLT's minimum curve), so the caller should re-apply the power mode to get the firmware's curve back.
/// </param>
public sealed record FanCurveProbeResult(FanCurveApplyResult Result, bool NeedsModeReapply);

/// <summary>
/// Applies the user's fan curve for each built-in power mode, right after every mode change (Fn+Q, the
/// dashboard, automations). Lenovo firmware usually loads its own curve on a mode change and may only honour
/// custom curves in Custom mode, so every write is read back immediately and again after a few seconds.
/// <para>
/// An apply reports as soon as the immediate read-back matches. The check a few seconds later runs in the
/// background; only if the firmware puts its own curve back does it write once more, and if that is undone too it
/// raises <see cref="Applied"/> again with <see cref="FanCurveApplyResult.Rejected"/>.
/// </para>
/// <para>
/// Only one apply runs at a time. A newer mode change cancels the one in progress, and before every write the
/// controller confirms the laptop is still in the mode the curve belongs to, so a curve is never written for a
/// mode that is no longer active.
/// </para>
/// <para>
/// Whether the firmware supports curves outside Custom mode
/// (<see cref="FanProfileSettings.FanProfileSettingsStore.FirmwareSupport"/>) is recorded only by the user-started
/// test (<see cref="ProbeAsync"/>). A normal apply never records it: a failure there may just be a mode change
/// still settling or the laptop going to sleep.
/// </para>
/// </summary>
public class FanProfileController
{
    private readonly FanProfileSettings settings;
    private readonly IFanTableAccess access;
    private readonly Func<Task<PowerModeState>> currentMode;

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private CancellationTokenSource? _running;

    public FanProfileController(FanProfileSettings settings, IFanTableAccess access, PowerModeFeature powerModeFeature)
        : this(settings, access, powerModeFeature.GetStateAsync) { }

    /// <summary>Takes the "which power mode is the laptop in" source directly, so tests can script it.</summary>
    internal FanProfileController(FanProfileSettings settings, IFanTableAccess access, Func<Task<PowerModeState>> currentMode)
    {
        this.settings = settings;
        this.access = access;
        this.currentMode = currentMode;
    }

    /// <summary>How long to wait before checking the firmware didn't put its own curve back.</summary>
    public TimeSpan RecheckDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long after resume from sleep the current mode's curve is applied again.</summary>
    public TimeSpan ResumeDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Raised after each attempt with the mode and the result, and once more from the background check if the
    /// firmware undid a curve that was first reported as applied (<see cref="FanCurveApplyResult.Rejected"/>).
    /// May be raised on any thread.
    /// </summary>
    public event EventHandler<(PowerModeState Mode, FanCurveApplyResult Result)>? Applied;

    /// <summary>The background check of the latest apply (completed when none is running). For tests.</summary>
    internal Task PendingCheck { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Applies curves on every power-mode change and again after resume from sleep (the firmware may load its
    /// own curve while asleep). Call once at startup.
    /// </summary>
    public void Start(PowerModeListener listener, PowerStateListener powerStateListener)
    {
        listener.Changed += async (_, e) =>
        {
            try
            {
                await ApplyForModeAsync(e.State).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Applying the fan curve for {e.State} failed.", ex);
            }
        };

        powerStateListener.Changed += async (_, e) =>
        {
            if (e.PowerStateEvent != PowerStateEvent.Resume)
                return;

            await Task.Delay(ResumeDelay).ConfigureAwait(false);
            await ApplyForCurrentModeAsync().ConfigureAwait(false);
        };
    }

    /// <summary>Applies the curve of the mode the laptop is in right now (at startup, after resume, when the feature is switched on).</summary>
    public async Task<FanCurveApplyResult> ApplyForCurrentModeAsync()
    {
        try
        {
            var mode = await currentMode().ConfigureAwait(false);
            return await ApplyForModeAsync(mode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Applying the fan curve for the current mode failed.", ex);
            return FanCurveApplyResult.Failed;
        }
    }

    /// <summary>The user's curve for a mode, or null when the firmware's curve should be used.</summary>
    public static ushort[]? CurveFor(FanProfileSettings.FanProfileSettingsStore store, PowerModeState mode)
    {
        if (!store.Enabled || mode == PowerModeState.GodMode)
            return null;
        return store.Curves.TryGetValue(mode, out var curve) && curve is { Length: 10 } ? curve : null;
    }

    /// <summary>The curve the dashboard should show for a mode (null when the firmware's curve is in use).</summary>
    public ushort[]? EffectiveCurve(PowerModeState mode) =>
        settings.Store.FirmwareSupport == FanCurveFirmwareSupport.Rejects ? null : CurveFor(settings.Store, mode);

    /// <summary>The last result for each mode in this run (only results that say whether the curve is in place).</summary>
    private readonly ConcurrentDictionary<PowerModeState, FanCurveApplyResult> _lastOutcome = new();

    /// <summary>The user's curve for a mode and whether it is actually in use, for the dashboard.</summary>
    public (ushort[]? Curve, FanCurveSource Source) CurveInUse(PowerModeState mode) =>
        CurveState(EffectiveCurve(mode), _lastOutcome.TryGetValue(mode, out var last) ? last : null);

    /// <summary>
    /// Whether a configured curve is in use: yes after a kept write, "not yet" before the first write in this run,
    /// and no after the firmware didn't keep it or the write failed (the firmware's curve is then in use).
    /// </summary>
    public static (ushort[]? Curve, FanCurveSource Source) CurveState(ushort[]? configured, FanCurveApplyResult? last) => configured switch
    {
        null => (null, FanCurveSource.Firmware),
        _ => last switch
        {
            null => (configured, FanCurveSource.UserCurvePending),
            FanCurveApplyResult.Applied => (configured, FanCurveSource.UserCurve),
            _ => (null, FanCurveSource.UserCurveNotKept),
        },
    };

    public async Task<FanCurveApplyResult> ApplyForModeAsync(PowerModeState mode)
    {
        // Whatever was being applied belongs to an earlier mode change: stop it, even when this mode has no curve.
        var superseder = Supersede();

        var curve = CurveFor(settings.Store, mode);
        if (curve is null)
            return Report(mode, FanCurveApplyResult.NotApplicable);

        if (settings.Store.FirmwareSupport == FanCurveFirmwareSupport.Rejects)
            return Report(mode, FanCurveApplyResult.SkippedUnsupported);

        return await ApplyAndWatchAsync(mode, curve, superseder).ConfigureAwait(false);
    }

    /// <summary>
    /// "Apply now": writes <paramref name="curve"/> for <paramref name="mode"/> the same way as a normal apply
    /// (minimum check, only while the laptop is in that mode, background check), without saving anything.
    /// </summary>
    public async Task<FanCurveApplyResult> PreviewAsync(PowerModeState mode, ushort[] curve)
    {
        var superseder = Supersede();

        if (mode == PowerModeState.GodMode)
            return Report(mode, FanCurveApplyResult.NotApplicable);

        if (settings.Store.FirmwareSupport == FanCurveFirmwareSupport.Rejects)
            return Report(mode, FanCurveApplyResult.SkippedUnsupported);

        if (curve is not { Length: 10 })
            return Report(mode, FanCurveApplyResult.Invalid, ofSavedCurve: false);

        return await ApplyAndWatchAsync(mode, curve, superseder, ofSavedCurve: false).ConfigureAwait(false);
    }

    /// <summary>Cancels the apply in progress (if any) and returns the token for the new one.</summary>
    private CancellationTokenSource Supersede()
    {
        var mine = new CancellationTokenSource();
        Interlocked.Exchange(ref _running, mine)?.Cancel();
        return mine;
    }

    /// <summary>
    /// Writes the curve and reports as soon as the immediate read-back matches. The later check then runs in the
    /// background and keeps the one-at-a-time lock until it ends (a newer apply cancels it), so applies never
    /// overlap. Reports every result itself, so the quick result is always raised before anything the
    /// background check finds.
    /// </summary>
    private async Task<FanCurveApplyResult> ApplyAndWatchAsync(PowerModeState mode, ushort[] curve, CancellationTokenSource mine, bool ofSavedCurve = true)
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        var lockPassedOn = false;
        try
        {
            var result = await WriteOnceAsync(mode, curve, mine.Token).ConfigureAwait(false);
            Report(mode, result, ofSavedCurve);

            if (result == FanCurveApplyResult.Applied)
            {
                lockPassedOn = true;
                PendingCheck = WatchAsync(mode, curve, mine.Token, ofSavedCurve);
            }

            return result;
        }
        finally
        {
            if (!lockPassedOn)
                _oneAtATime.Release();
        }
    }

    /// <summary>
    /// The check after an apply. If the firmware put its own curve back (the mode change may still have been
    /// settling), writes once more and checks again; if that is undone too, reports
    /// <see cref="FanCurveApplyResult.Rejected"/>. Stops quietly when the mode changes or a newer apply starts:
    /// that one reports for itself. Releases the lock it was handed.
    /// </summary>
    private async Task WatchAsync(PowerModeState mode, ushort[] curve, CancellationToken token, bool ofSavedCurve)
    {
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var verdict = await RecheckAsync(mode, curve, token).ConfigureAwait(false);
                if (verdict != FanCurveApplyResult.Rejected)
                {
                    Log.Instance.Trace($"Fan curve for {mode}, background check: {verdict}");
                    return;
                }

                Log.Instance.Trace($"Fan table was reverted by the firmware (attempt {attempt}).");
                if (attempt == 2)
                {
                    Report(mode, FanCurveApplyResult.Rejected, ofSavedCurve);
                    return;
                }

                if (await StopReasonAsync(mode, token).ConfigureAwait(false) is not null)
                    return;

                var outcome = await access.WriteVerifiedAsync(curve).ConfigureAwait(false);
                if (outcome == FanTableWriteOutcome.Kept)
                    continue;

                if (outcome == FanTableWriteOutcome.NotKept && await StopReasonAsync(mode, token).ConfigureAwait(false) is null)
                    Report(mode, FanCurveApplyResult.Rejected, ofSavedCurve);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"The background fan curve check failed.", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Tests whether this laptop keeps a custom curve outside Custom mode, by writing <paramref name="testCurve"/>
    /// while <paramref name="testMode"/> is active and checking once, <see cref="RecheckDelay"/> later. This is
    /// the only place the answer is saved, and only when the firmware gave a definite one: an error, or the power
    /// mode changing during the test, saves nothing.
    /// <para>
    /// Afterwards the fans go back to the table they had before the test, unless the test curve is the saved curve
    /// for this mode and the firmware kept it. When that table can't be put back, the result says so and the
    /// caller re-applies the power mode instead.
    /// </para>
    /// </summary>
    public async Task<FanCurveProbeResult> ProbeAsync(PowerModeState testMode, ushort[] testCurve)
    {
        if (testMode == PowerModeState.GodMode)
            throw new InvalidOperationException("Switch to a built-in power mode before testing.");

        var mine = Supersede();
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            ushort[]? earlier = null;
            try
            {
                earlier = await access.ReadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't read the fan table before the test.", ex);
            }

            // No mode change happens during a test, so there is nothing to settle and no second write.
            var result = await WriteOnceAsync(testMode, testCurve, mine.Token).ConfigureAwait(false);
            if (result == FanCurveApplyResult.Applied)
                result = await RecheckAsync(testMode, testCurve, mine.Token).ConfigureAwait(false);

            if (result is FanCurveApplyResult.Applied or FanCurveApplyResult.Rejected)
            {
                settings.Store.FirmwareSupport = result == FanCurveApplyResult.Applied ? FanCurveFirmwareSupport.Accepts : FanCurveFirmwareSupport.Rejects;
                settings.SynchronizeStore();
            }

            var keep = result == FanCurveApplyResult.Applied
                       && CurveFor(settings.Store, testMode) is { } saved
                       && saved.SequenceEqual(testCurve);

            // Nothing was written (curve refused) or the laptop moved on to another mode, which sets its own curve.
            var nothingToUndo = result is FanCurveApplyResult.Invalid or FanCurveApplyResult.Superseded;

            var needsModeReapply = !keep && !nothingToUndo && !await RestoreAsync(testMode, earlier, mine.Token).ConfigureAwait(false);

            Report(testMode, result, ofSavedCurve: false);
            return new FanCurveProbeResult(result, needsModeReapply);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Puts back the table the fans had before a test. False when that isn't possible (it couldn't be read, it
    /// is below LLT's minimum curve, or the write wasn't kept); true when it was put back or when the laptop has
    /// already left the mode (the new mode brings its own curve).
    /// </summary>
    private async Task<bool> RestoreAsync(PowerModeState mode, ushort[]? earlier, CancellationToken token)
    {
        if (earlier is not { Length: 10 })
            return false;

        // LLT only ever writes tables that pass its own minimum, even the firmware's own.
        if (!await access.IsAllowedAsync(earlier).ConfigureAwait(false))
        {
            Log.Instance.Trace($"The fan table from before the test is below the minimum; the power mode will be re-applied instead.");
            return false;
        }

        if (await StopReasonAsync(mode, token).ConfigureAwait(false) is { } stop)
            return stop == FanCurveApplyResult.Superseded;

        return await access.WriteVerifiedAsync(earlier).ConfigureAwait(false) == FanTableWriteOutcome.Kept;
    }

    /// <summary>
    /// Checks the curve is allowed and the laptop is still in <paramref name="mode"/>, then writes it once with
    /// the immediate read-back. <see cref="FanCurveApplyResult.Applied"/> means the read-back matched.
    /// </summary>
    private async Task<FanCurveApplyResult> WriteOnceAsync(PowerModeState mode, ushort[] curve, CancellationToken token)
    {
        if (!await access.IsAllowedAsync(curve).ConfigureAwait(false))
            return FanCurveApplyResult.Invalid;

        if (await StopReasonAsync(mode, token).ConfigureAwait(false) is { } stopBeforeWrite)
            return stopBeforeWrite;

        return await access.WriteVerifiedAsync(curve).ConfigureAwait(false) switch
        {
            FanTableWriteOutcome.Kept => FanCurveApplyResult.Applied,
            FanTableWriteOutcome.Error => FanCurveApplyResult.Failed,
            _ => await StopReasonAsync(mode, token).ConfigureAwait(false) ?? FanCurveApplyResult.Rejected,
        };
    }

    /// <summary>
    /// Waits <see cref="RecheckDelay"/> and reads the table again: <see cref="FanCurveApplyResult.Applied"/> if
    /// the curve is still there, <see cref="FanCurveApplyResult.Rejected"/> if the firmware put another one back
    /// while the laptop stayed in <paramref name="mode"/>.
    /// </summary>
    private async Task<FanCurveApplyResult> RecheckAsync(PowerModeState mode, ushort[] curve, CancellationToken token)
    {
        try
        {
            await Task.Delay(RecheckDelay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return FanCurveApplyResult.Superseded;
        }

        ushort[] now;
        try
        {
            now = await access.ReadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Fan table re-check failed.", ex);
            return FanCurveApplyResult.Failed;
        }

        // A different table after a mode change is the new mode's curve, not a refusal.
        if (await StopReasonAsync(mode, token).ConfigureAwait(false) is { } stopBeforeVerdict)
            return stopBeforeVerdict;

        return now.SequenceEqual(curve) ? FanCurveApplyResult.Applied : FanCurveApplyResult.Rejected;
    }

    /// <summary>
    /// Null to carry on. Otherwise why this apply must stop: a newer one started or the laptop left
    /// <paramref name="mode"/> (<see cref="FanCurveApplyResult.Superseded"/>), or the current mode couldn't be
    /// read, in which case nothing is written either (<see cref="FanCurveApplyResult.Failed"/>).
    /// </summary>
    private async Task<FanCurveApplyResult?> StopReasonAsync(PowerModeState mode, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return FanCurveApplyResult.Superseded;

        try
        {
            return await currentMode().ConfigureAwait(false) == mode ? null : FanCurveApplyResult.Superseded;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the current power mode; not writing the fan curve.", ex);
            return FanCurveApplyResult.Failed;
        }
    }

    /// <summary>
    /// Puts the Custom preset made for <paramref name="mode"/> into <paramref name="presets"/>: it replaces the
    /// one made earlier for that mode while that still exists, and only otherwise adds a new one. Returns its id.
    /// </summary>
    public static Guid UpsertFallbackPreset(IDictionary<Guid, GodModePreset> presets, IDictionary<PowerModeState, Guid> knownIds, PowerModeState mode, GodModePreset preset)
    {
        var id = knownIds.TryGetValue(mode, out var known) && presets.ContainsKey(known) ? known : Guid.NewGuid();
        presets[id] = preset;
        knownIds[mode] = id;
        return id;
    }

    /// <param name="ofSavedCurve">
    /// True when the curve written is the saved one for the mode, so the result says whether that curve is in use
    /// (see <see cref="CurveInUse"/>). The test and Apply now write other curves and don't count.
    /// </param>
    private FanCurveApplyResult Report(PowerModeState mode, FanCurveApplyResult result, bool ofSavedCurve = true)
    {
        if (result != FanCurveApplyResult.NotApplicable)
            Log.Instance.Trace($"Fan curve for {mode}: {result}");

        if (ofSavedCurve && result is FanCurveApplyResult.Applied or FanCurveApplyResult.Rejected or FanCurveApplyResult.Failed or FanCurveApplyResult.Invalid)
            _lastOutcome[mode] = result;

        Applied?.Invoke(this, (mode, result));
        return result;
    }

    /// <summary>
    /// The Custom-mode method for laptops whose firmware rejects curves outside Custom mode: a Custom preset with
    /// the firmware's own limits for <paramref name="defaults"/>' mode and the user's curve. Values the firmware
    /// doesn't report keep the template's value; each value stays within the template's allowed range (or on one
    /// of its allowed steps). A reported 0 counts as a value only where 0 is allowed (e.g. a boost that is off).
    /// The template's Windows power plan/mode choices are not copied: they belong to that preset.
    /// </summary>
    public static GodModePreset BuildCustomPreset(GodModePreset template, GodModeDefaults defaults, ushort[] curve, string name)
    {
        static StepperValue? Take(StepperValue? current, int? value)
        {
            if (current is not { } c || value is not { } v || v < 0)
                return current;

            if (c.Steps is { Length: > 0 } steps)
                return v == 0 && !steps.Contains(0) ? current : c.WithValue(steps.MinBy(s => Math.Abs((long)s - v)));

            return v == 0 && c.Min > 0 ? current : c.WithValue(Math.Clamp(v, c.Min, c.Max));
        }

        return new GodModePreset
        {
            Name = name,
            CPULongTermPowerLimit = Take(template.CPULongTermPowerLimit, defaults.CPULongTermPowerLimit),
            CPUShortTermPowerLimit = Take(template.CPUShortTermPowerLimit, defaults.CPUShortTermPowerLimit),
            CPUPeakPowerLimit = Take(template.CPUPeakPowerLimit, defaults.CPUPeakPowerLimit),
            CPUCrossLoadingPowerLimit = Take(template.CPUCrossLoadingPowerLimit, defaults.CPUCrossLoadingPowerLimit),
            CPUPL2Tau = Take(template.CPUPL2Tau, defaults.CPUPL2Tau),
            APUsPPTPowerLimit = Take(template.APUsPPTPowerLimit, defaults.APUsPPTPowerLimit),
            CPUTemperatureLimit = Take(template.CPUTemperatureLimit, defaults.CPUTemperatureLimit),
            PrecisionBoostOverdriveScaler = template.PrecisionBoostOverdriveScaler,
            PrecisionBoostOverdriveBoostFrequency = template.PrecisionBoostOverdriveBoostFrequency,
            AllCoreCurveOptimizer = template.AllCoreCurveOptimizer,
            EnableAllCoreCurveOptimizer = false,
            EnableOverclocking = false,
            GPUPowerBoost = Take(template.GPUPowerBoost, defaults.GPUPowerBoost),
            GPUConfigurableTGP = Take(template.GPUConfigurableTGP, defaults.GPUConfigurableTGP),
            GPUTemperatureLimit = Take(template.GPUTemperatureLimit, defaults.GPUTemperatureLimit),
            GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = Take(template.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, defaults.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline),
            GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI = template.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI,
            GPUToCPUDynamicBoost = Take(template.GPUToCPUDynamicBoost, defaults.GPUToCPUDynamicBoost),
            FanTableInfo = template.FanTableInfo is { } info ? new FanTableInfo(info.Data, new FanTable(curve)) : null,
            FanFullSpeed = false,
            MinValueOffset = template.MinValueOffset,
            MaxValueOffset = template.MaxValueOffset,
            Overrides = [],
        };
    }
}
