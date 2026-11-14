using System;
using System.Linq;
using LenovoLegionToolkit.Lib.System;
using Xunit;
using Step = LenovoLegionToolkit.Lib.System.BootLogo.BootLogoStep;

namespace LenovoLegionToolkit.Tests.System;

// The order in which turning the custom boot logo on or off changes the UEFI flag, the checksum and the image
// file. The order is what decides what the laptop shows after an interruption; the UEFI writes themselves need the
// laptop and are on the hardware checklist.
public class BootLogoStepTests
{
    private static readonly Step[] FileSteps = [Step.DeleteFiles, Step.CopyImage];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Enable_SetsTheFlagLast_AfterTheImageAndChecksum(bool currentlyEnabled)
    {
        var steps = BootLogo.EnableSteps(currentlyEnabled);

        Assert.Equal(Step.EnableFlag, steps[^1]);
        Assert.True(Index(steps, Step.CopyImage) < Index(steps, Step.WriteChecksum));
        Assert.True(Index(steps, Step.WriteChecksum) < Index(steps, Step.EnableFlag));
    }

    [Fact]
    public void Enable_WhenALogoIsAlreadyOn_TurnsTheFlagOffBeforeTouchingAnyFile()
    {
        var steps = BootLogo.EnableSteps(currentlyEnabled: true);

        Assert.Equal(Step.DisableFlag, steps[0]);
        Assert.All(FileSteps, s => Assert.True(Index(steps, s) > 0));
    }

    [Fact]
    public void Enable_WhenNoLogoIsOn_DoesNotWriteTheFlagTwice()
    {
        Assert.DoesNotContain(Step.DisableFlag, BootLogo.EnableSteps(currentlyEnabled: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Enable_FlagIsOff_AtEveryPointWhereAFileChanges(bool currentlyEnabled)
    {
        // Walk the steps and track the flag: whenever a file step runs, the flag must be off.
        var steps = BootLogo.EnableSteps(currentlyEnabled);
        var flagOn = currentlyEnabled;
        foreach (var step in steps)
        {
            if (step == Step.DisableFlag) flagOn = false;
            if (step == Step.EnableFlag) flagOn = true;
            if (FileSteps.Contains(step) || step == Step.WriteChecksum)
                Assert.False(flagOn, $"flag on during {step}");
        }
        Assert.True(flagOn);
    }

    [Fact]
    public void Disable_TurnsTheFlagOffFirst_ThenChecksum_ThenFiles()
    {
        Assert.Equal([Step.DisableFlag, Step.ClearChecksum, Step.DeleteFiles], BootLogo.DisableSteps());
    }

    private static int Index(global::System.Collections.Generic.IReadOnlyList<Step> steps, Step step)
    {
        var index = steps.ToList().IndexOf(step);
        if (index < 0)
            throw new InvalidOperationException($"{step} missing");
        return index;
    }
}
