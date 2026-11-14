using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

// The rule every God Mode value goes through before it is shown in the window and, since the safety fixes, before
// it is sent to the firmware. "Firmware" below is the range the firmware reported for the setting; "saved" is the
// number in the settings file. Sending real values needs the laptop and is on the hardware checklist.
public class GodModeRangeTests
{
    private static StepperValue FirmwareRange(int min, int max, int step, int? defaultValue = null) => new(min, min, max, step, [], defaultValue);

    private static StepperValue FirmwareSteps(int[] steps, int? defaultValue = null) => new(steps[0], 0, 0, 0, steps, defaultValue);

    private static StepperValue Saved(int value) => new(value, 0, 0, 0, [], null);

    private static int Fit(StepperValue firmware, int saved, int? minOffset = 0, int? maxOffset = 0) =>
        GodModeController.CreateStepperValue(firmware, Saved(saved), minOffset, maxOffset)!.Value.Value;

    [Fact]
    public void SavedValueInsideTheRange_IsKept()
    {
        Assert.Equal(80, Fit(FirmwareRange(35, 115, 1, 65), 80));
    }

    [Theory]
    [InlineData(200)]   // e.g. a backup from a laptop with a bigger GPU
    [InlineData(10)]
    [InlineData(-5)]
    public void SavedValueOutsideTheRange_BecomesTheFirmwaresDefault(int saved)
    {
        Assert.Equal(65, Fit(FirmwareRange(35, 115, 1, 65), saved));
    }

    [Theory]
    [InlineData(200, 115)]
    [InlineData(10, 35)]
    public void SavedValueOutsideTheRange_WithNoDefault_GoesToTheNearestEnd(int saved, int expected)
    {
        Assert.Equal(expected, Fit(FirmwareRange(35, 115, 1), saved));
    }

    [Fact]
    public void SavedValueOffTheStepGrid_MovesToTheNearestStep()
    {
        Assert.Equal(85, Fit(FirmwareRange(35, 115, 5, 65), 83));
    }

    [Theory]
    [InlineData(112, 115)]
    [InlineData(500, 115)]   // far outside: still one of the firmware's own steps
    [InlineData(0, 80)]
    public void SettingWithFixedSteps_AlwaysLandsOnOneOfThem(int saved, int expected)
    {
        Assert.Equal(expected, Fit(FirmwareSteps([80, 100, 115]), saved));
    }

    [Fact]
    public void SettingTheFirmwareReportsNoRangeFor_GivesNothingToSend()
    {
        Assert.Null(GodModeController.CreateStepperValue(null, Saved(90)));
        Assert.Null(GodModeController.CreateStepperValue(new StepperValue(0, 0, 0, 0, [], null), Saved(90)));
    }

    // ---- The preset's min/max offsets can narrow the range, never widen it ----

    [Fact]
    public void MaxOffset_CannotRaiseTheFirmwaresMaximum()
    {
        var fitted = GodModeController.CreateStepperValue(FirmwareRange(35, 115, 1, 65), Saved(140), 0, maxValueOffset: 50)!.Value;

        Assert.Equal(115, fitted.Max);
        Assert.Equal(65, fitted.Value);   // 140 is outside the real range, so it is not sent
    }

    [Fact]
    public void MinOffset_CannotLowerTheFirmwaresMinimum()
    {
        var fitted = GodModeController.CreateStepperValue(FirmwareRange(35, 115, 1, 65), Saved(20), minValueOffset: -30, 0)!.Value;

        Assert.Equal(35, fitted.Min);
        Assert.Equal(65, fitted.Value);
    }

    [Fact]
    public void Offsets_CanStillNarrowTheRange()
    {
        var fitted = GodModeController.CreateStepperValue(FirmwareRange(35, 115, 1, 65), Saved(110), minValueOffset: 10, maxValueOffset: -15)!.Value;

        Assert.Equal(45, fitted.Min);
        Assert.Equal(100, fitted.Max);
        Assert.Equal(65, fitted.Value);   // 110 is above the narrowed maximum
    }

    [Fact]
    public void OffsetsThatLeaveNoRoom_AreIgnored()
    {
        var fitted = GodModeController.CreateStepperValue(FirmwareRange(35, 115, 1, 65), Saved(80), minValueOffset: 100, maxValueOffset: -100)!.Value;

        Assert.Equal(35, fitted.Min);
        Assert.Equal(115, fitted.Max);
        Assert.Equal(80, fitted.Value);
    }

    // ---- Only what the preset saves is sent ----

    [Fact]
    public void SettingThePresetDoesNotSave_IsNotSent_EvenThoughTheCheckedPresetHasAValueForIt()
    {
        // The checked preset fills an unsaved setting with the firmware's read-back value; that must not be sent.
        var checkedValue = GodModeController.CreateStepperValue(FirmwareRange(35, 115, 1, 65), store: null);

        Assert.NotNull(checkedValue);
        Assert.Null(GodModeController.OnlyIfSaved(saved: null, checkedValue));
        Assert.Equal(checkedValue, GodModeController.OnlyIfSaved(Saved(80), checkedValue));
    }

    [Fact]
    public void MissingOffsets_MeanNoOffset()
    {
        Assert.Equal(80, Fit(FirmwareRange(35, 115, 1, 65), 80, minOffset: null, maxOffset: null));
    }
}
