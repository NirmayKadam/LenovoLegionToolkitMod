using System.Collections.Generic;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Utils;
using Newtonsoft.Json;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

public class OsdToggleTests
{
    // ---- Hotkey capture ----

    [Fact]
    public void FromKey_CtrlShiftO_IsValid()
    {
        var binding = HotkeyGesture.FromKey(Key.O, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.NotNull(binding);
        Assert.Equal(HotkeyBinding.Control | HotkeyBinding.Shift, binding!.Modifiers);
        Assert.Equal((uint)KeyInterop.VirtualKeyFromKey(Key.O), binding.VirtualKey);
        Assert.Equal(0x4Fu, binding.VirtualKey); // VK_O
    }

    [Theory]
    [InlineData(Key.O, ModifierKeys.None)]                 // would steal typing
    [InlineData(Key.O, ModifierKeys.Shift)]                // Shift alone is still typing
    [InlineData(Key.LeftCtrl, ModifierKeys.Control)]       // just a modifier held down
    [InlineData(Key.LeftShift, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.LWin, ModifierKeys.Windows)]
    public void FromKey_RejectsCombinationsThatCantBeGlobalShortcuts(Key key, ModifierKeys modifiers) =>
        Assert.Null(HotkeyGesture.FromKey(key, modifiers));

    [Theory]
    [InlineData(Key.F9, ModifierKeys.Alt)]
    [InlineData(Key.D1, ModifierKeys.Windows | ModifierKeys.Shift)]
    [InlineData(Key.NumPad5, ModifierKeys.Control | ModifierKeys.Alt)]
    public void FromKey_AcceptsAnyKeyWithCtrlAltOrWin(Key key, ModifierKeys modifiers) =>
        Assert.NotNull(HotkeyGesture.FromKey(key, modifiers));

    [Theory]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+O")]
    [InlineData(Key.F9, ModifierKeys.Alt, "Alt+F9")]
    [InlineData(Key.D1, ModifierKeys.Windows | ModifierKeys.Shift, "Shift+Win+1")]
    [InlineData(Key.NumPad5, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+Num 5")]
    public void Format_ReadsLikeWindowsShortcuts(Key key, ModifierKeys modifiers, string expected) =>
        Assert.Equal(expected, HotkeyGesture.Format(HotkeyGesture.FromKey(key, modifiers)!));

    [Fact]
    public void ToggleHotkey_SurvivesTheSettingsFileRoundTrip()
    {
        var settings = AbstractSettings<OsdSettings.OsdSettingsStore>.CreateJsonSerializerSettings();
        var store = new OsdSettings.OsdSettingsStore { ToggleHotkey = new HotkeyBinding { Modifiers = HotkeyBinding.Control | HotkeyBinding.Shift, VirtualKey = 0x4F } };

        var json = JsonConvert.SerializeObject(store, settings);
        var back = JsonConvert.DeserializeObject<OsdSettings.OsdSettingsStore>(json, settings)!;

        Assert.Equal(store.ToggleHotkey, back.ToggleHotkey);
    }

    [Fact]
    public void NoHotkey_IsTheDefault() => Assert.Null(new OsdSettings.OsdSettingsStore().ToggleHotkey);

    // ---- Quick action for special keys ----

    [Fact]
    public void OsdToggleQuickAction_HasNoTrigger_AndOneToggleStep()
    {
        var pipeline = QuickActionTemplates.CreateOsdToggle("Toggle OSD");

        Assert.Equal("Toggle OSD", pipeline.Name);
        Assert.Null(pipeline.Trigger);
        var step = Assert.IsType<OsdAutomationStep>(Assert.Single(pipeline.Steps));
        Assert.Equal(ToggleState.Toggle, step.State);
    }

    [Fact]
    public void FindOsdToggle_FindsAnExistingOne_SoNoDuplicateIsCreated()
    {
        var existing = QuickActionTemplates.CreateOsdToggle("My OSD key");
        var pipelines = new List<AutomationPipeline> { new("Other"), existing };

        Assert.Same(existing, QuickActionTemplates.FindOsdToggle(pipelines));
    }

    [Fact]
    public void FindOsdToggle_IgnoresPipelinesThatDoMoreOrHaveATrigger()
    {
        var withTrigger = QuickActionTemplates.CreateOsdToggle("A");
        withTrigger.Trigger = new OnStartupAutomationPipelineTrigger();

        var withExtraStep = QuickActionTemplates.CreateOsdToggle("B");
        withExtraStep.Steps.Add(new OsdAutomationStep(ToggleState.On));

        var onlyOn = new AutomationPipeline("C") { Steps = { new OsdAutomationStep(ToggleState.On) } };

        Assert.Null(QuickActionTemplates.FindOsdToggle([withTrigger, withExtraStep, onlyOn]));
    }
}
