using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// Undo of a God Mode Save over a real godmode.json in a temporary folder. The power mode, switching it and the
/// firmware apply are supplied by the test and recorded, so these tests cover the decisions; what the firmware does
/// with the values is on the hardware checklist.
/// </summary>
public sealed class GodModeUndoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-godmode-undo-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _presetId = Guid.NewGuid();

    private PowerModeState _laptopMode = PowerModeState.GodMode;
    private bool _modeUnreadable;
    private readonly List<PowerModeState> _modeSwitches = [];
    private int _applies;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private string FilePath => Path.Combine(_dir, "godmode.json");

    /// <summary>A saved preset like a real one: a name, Windows power overrides and the fan full-speed switch.</summary>
    private GodModeSettings SavedSettings()
    {
        var settings = new GodModeSettings(_dir);
        settings.Store.ActivePresetId = _presetId;
        settings.Store.Presets = new()
        {
            [_presetId] = new GodModeSettings.GodModeSettingsStore.Preset
            {
                Name = "Default",
                FanFullSpeed = false,
                Overrides = new() { [PowerOverrideKey.PowerPlan] = "plan-a" },
            },
        };
        settings.SynchronizeStore();
        return settings;
    }

    /// <summary>What the window's Save does to the settings: a new presets dictionary with the new values.</summary>
    private static void Save(GodModeSettings settings)
    {
        var id = settings.Store.ActivePresetId;
        settings.Store.Presets = new()
        {
            [id] = new GodModeSettings.GodModeSettingsStore.Preset { Name = "Default", FanFullSpeed = true, MaxValueOffset = 10 },
        };
        settings.SynchronizeStore();
    }

    private GodModeUndo Capture(GodModeSettings settings, PowerModeState? switchedFrom) => new(
        settings,
        switchedFrom,
        () => _modeUnreadable ? throw new InvalidOperationException("no answer") : Task.FromResult(_laptopMode),
        mode => { _modeSwitches.Add(mode); return Task.CompletedTask; },
        () => { _applies++; return Task.CompletedTask; });

    [Fact]
    public async Task Undo_PutsBackExactlyWhatWasSaved()
    {
        var settings = SavedSettings();
        var original = await File.ReadAllBytesAsync(FilePath);
        var undo = Capture(settings, null);
        Save(settings);

        Assert.True(await undo.RestoreAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
        Assert.Equal("plan-a", new GodModeSettings(_dir).Store.Presets[_presetId].Overrides[PowerOverrideKey.PowerPlan]);
    }

    [Fact]
    public async Task Undo_InCustomMode_SendsTheEarlierPresetAgain()
    {
        var settings = SavedSettings();
        var undo = Capture(settings, null);
        Save(settings);

        await undo.RestoreAsync();

        Assert.Equal(1, _applies);
        Assert.Empty(_modeSwitches);
    }

    [Fact]
    public async Task Undo_WhenTheSaveSwitchedToCustom_GoesBackToTheEarlierMode_InsteadOfApplying()
    {
        var settings = SavedSettings();
        var undo = Capture(settings, PowerModeState.Balance);
        Save(settings);

        await undo.RestoreAsync();

        Assert.Equal([PowerModeState.Balance], _modeSwitches);
        Assert.Equal(0, _applies);
    }

    [Fact]
    public async Task Undo_AfterTheUserLeftCustomMode_ChangesNoMode_AndSendsNothing()
    {
        var settings = SavedSettings();
        var undo = Capture(settings, PowerModeState.Balance);
        Save(settings);
        _laptopMode = PowerModeState.Performance;   // Fn+Q since the Save

        Assert.True(await undo.RestoreAsync());

        Assert.Empty(_modeSwitches);
        Assert.Equal(0, _applies);
        Assert.False(new GodModeSettings(_dir).Store.Presets[_presetId].FanFullSpeed);   // the settings are still back
    }

    [Fact]
    public async Task Undo_WhenTheModeCannotBeRead_PutsTheSettingsBack_AndSaysNotEverythingWasApplied()
    {
        var settings = SavedSettings();
        var undo = Capture(settings, null);
        Save(settings);
        _modeUnreadable = true;

        Assert.False(await undo.RestoreAsync());

        Assert.False(new GodModeSettings(_dir).Store.Presets[_presetId].FanFullSpeed);
        Assert.Equal(0, _applies);
        Assert.Empty(_modeSwitches);
    }

    [Fact]
    public async Task Undo_TheCopyIsDeep_AnEditInPlaceAfterCapturingDoesNotReachIt()
    {
        var settings = SavedSettings();
        var undo = Capture(settings, null);

        // Something edits the live preset's overrides in place (the Windows power plan windows build new
        // dictionaries, but nothing stops an in-place edit).
        settings.Store.Presets[_presetId].Overrides[PowerOverrideKey.PowerPlan] = "plan-b";

        await undo.RestoreAsync();

        Assert.Equal("plan-a", settings.Store.Presets[_presetId].Overrides[PowerOverrideKey.PowerPlan]);
    }
}
