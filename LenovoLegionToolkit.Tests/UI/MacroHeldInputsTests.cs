using System.Linq;
using LenovoLegionToolkit.Lib.Macro;
using LenovoLegionToolkit.Lib.Macro.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

// What a macro that is stopped half-way has to release, so no key or mouse button stays held down. This is the
// bookkeeping the player uses; sending the input itself (SendInput) is not done in tests.
public class MacroHeldInputsTests
{
    private const uint A = 0x41;
    private const uint B = 0x42;
    private const uint LeftButton = 1;

    private static MacroEvent Key(MacroDirection direction, uint key) => new() { Source = MacroSource.Keyboard, Direction = direction, Key = key };

    private static MacroEvent Mouse(MacroDirection direction, uint button) => new() { Source = MacroSource.Mouse, Direction = direction, Key = button };

    [Fact]
    public void StoppedAfterPressingTwoKeysAndReleasingOne_ReleasesTheOther()
    {
        var held = new MacroPlayer.HeldInputs();
        held.Track(Key(MacroDirection.Down, A));
        held.Track(Key(MacroDirection.Down, B));
        held.Track(Key(MacroDirection.Up, A));

        var release = Assert.Single(held.Releases());
        Assert.Equal((MacroSource.Keyboard, B, MacroDirection.Up), (release.Source, release.Key, release.Direction));
    }

    [Fact]
    public void CompleteSequence_ReleasesNothing()
    {
        var held = new MacroPlayer.HeldInputs();
        foreach (var e in new[] { Key(MacroDirection.Down, A), Key(MacroDirection.Up, A), Mouse(MacroDirection.Down, LeftButton), Mouse(MacroDirection.Up, LeftButton) })
            held.Track(e);

        Assert.Empty(held.Releases());
    }

    [Fact]
    public void HeldMouseButton_IsReleased_AndWheelOrMoveHoldNothing()
    {
        var held = new MacroPlayer.HeldInputs();
        held.Track(Mouse(MacroDirection.Down, LeftButton));
        held.Track(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Wheel, Key = 120 });
        held.Track(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Move });

        var release = Assert.Single(held.Releases());
        Assert.Equal((MacroSource.Mouse, LeftButton, MacroDirection.Up), (release.Source, release.Key, release.Direction));
    }

    [Fact]
    public void KeyAndMouseWithTheSameNumber_AreTrackedSeparately_LatestReleasedFirst()
    {
        var held = new MacroPlayer.HeldInputs();
        held.Track(Mouse(MacroDirection.Down, 1));
        held.Track(Key(MacroDirection.Down, 1));
        held.Track(Key(MacroDirection.Down, 1));   // repeated "down" (auto-repeat) is still one held key

        Assert.Equal([MacroSource.Keyboard, MacroSource.Mouse], held.Releases().Select(r => r.Source));
    }

    [Fact]
    public void ReleaseForAKeyNeverPressed_IsIgnored()
    {
        var held = new MacroPlayer.HeldInputs();
        held.Track(Key(MacroDirection.Up, A));

        Assert.Empty(held.Releases());
    }
}
