using System.Linq;
using LenovoLegionToolkit.WPF.Utils.Skins;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

public class RetroTextTests
{
    [Theory]
    [InlineData(0.0, "░░░░░░░░░░")]
    [InlineData(0.5, "█████░░░░░")]
    [InlineData(1.0, "██████████")]
    [InlineData(0.04, "░░░░░░░░░░")]   // 0.4 of a cell rounds down
    [InlineData(0.05, "█░░░░░░░░░")]   // half a cell rounds up
    [InlineData(0.62, "██████░░░░")]
    [InlineData(1.5, "██████████")]    // clamped
    [InlineData(-0.2, "░░░░░░░░░░")]
    [InlineData(double.NaN, "░░░░░░░░░░")]
    public void BlockBar_RoundsToCells(double fraction, string expected) => Assert.Equal(expected, RetroText.BlockBar(fraction));

    [Fact]
    public void BlockBar_UnknownValue_IsEmpty() => Assert.Equal("░░░░░░░░░░", RetroText.BlockBar(null));

    [Fact]
    public void BlockBar_AlwaysHasTheRequestedWidth()
    {
        foreach (var cells in new[] { 1, 5, 10, 16 })
        foreach (var f in new[] { 0.0, 0.33, 0.5, 0.99, 1.0 })
            Assert.Equal(cells, RetroText.BlockBar(f, cells).Length);
    }

    [Fact]
    public void SegmentLabel_IsBracketedUppercase() => Assert.Equal("[ PERFORMANCE ]", RetroText.SegmentLabel("Performance"));

    [Fact]
    public void ConsoleLines_StateModeAndOutcome()
    {
        Assert.Equal("POWER MODE = BALANCE", RetroText.ModeLine("Balance"));
        Assert.Equal("POWER MODE = PERFORMANCE. OK", RetroText.ChangeLine("Performance", true));
        Assert.Equal("POWER MODE = PERFORMANCE. FAILED", RetroText.ChangeLine("Performance", false));
    }

    [Fact]
    public void TypingFrames_RevealOneCharacterAtATime()
    {
        Assert.Equal(["", "O", "OK"], RetroText.TypingFrames("OK").ToArray());
        Assert.Equal([""], RetroText.TypingFrames("").ToArray());

        var frames = RetroText.TypingFrames("POWER MODE").ToArray();
        Assert.Equal(11, frames.Length);
        Assert.Equal("POWER MODE", frames[^1]);
        for (var i = 1; i < frames.Length; i++)
            Assert.StartsWith(frames[i - 1], frames[i]);
    }
}
