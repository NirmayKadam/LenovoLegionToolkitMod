using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// Smart Fn Lock's record of held modifier keys. When game-safe mode or shutdown removed the keyboard hook while a
/// key was held, its release was never seen and the key stayed "held"; <see cref="ModifierKeyState.Clear"/> now
/// forgets it. Virtual-key codes as Windows reports them.
/// </summary>
public class ModifierKeyStateTests
{
    private const uint VK_SHIFT = 0x10;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_LSHIFT = 0xA0;
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_RMENU = 0xA5;
    private const uint VK_A = 0x41;

    private const ModifierKey All = ModifierKey.Ctrl | ModifierKey.Shift | ModifierKey.Alt;

    [Fact]
    public void ReleasingOneSide_KeepsTheOtherSideHeld()
    {
        var keys = new ModifierKeyState();
        keys.Update(VK_LCONTROL, isDown: true);
        keys.Update(VK_RCONTROL, isDown: true);

        keys.Update(VK_LCONTROL, isDown: false);
        Assert.True(keys.IsActive(ModifierKey.Ctrl));

        keys.Update(VK_RCONTROL, isDown: false);
        Assert.False(keys.IsActive(ModifierKey.Ctrl));
    }

    [Fact]
    public void AGenericCode_CountsAsTheLeftKey()
    {
        var keys = new ModifierKeyState();
        keys.Update(VK_CONTROL, isDown: true);
        keys.Update(VK_SHIFT, isDown: true);

        keys.Update(VK_LCONTROL, isDown: false);
        keys.Update(VK_LSHIFT, isDown: false);

        Assert.False(keys.IsActive(All));
    }

    [Fact]
    public void OnlyTheChosenModifiersCount()
    {
        var keys = new ModifierKeyState();
        keys.Update(VK_RMENU, isDown: true);

        Assert.False(keys.IsActive(ModifierKey.Ctrl | ModifierKey.Shift));
        Assert.True(keys.IsActive(ModifierKey.Alt));
        Assert.False(keys.IsActive(ModifierKey.None));
    }

    [Fact]
    public void OtherKeys_AreIgnored()
    {
        var keys = new ModifierKeyState();
        keys.Update(VK_A, isDown: true);

        Assert.False(keys.IsActive(All));
    }

    [Fact]
    public void AKeyWhoseReleaseWasNeverSeen_IsForgottenWhenCleared()
    {
        var keys = new ModifierKeyState();
        keys.Update(VK_LCONTROL, isDown: true);
        keys.Update(VK_LSHIFT, isDown: true);
        keys.Update(VK_RMENU, isDown: true);

        keys.Clear();

        Assert.False(keys.IsActive(All));
    }
}
