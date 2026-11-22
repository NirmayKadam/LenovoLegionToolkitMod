using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace LenovoLegionToolkit.Lib.Controllers;

/// <summary>
/// Which modifier keys are held, from the keyboard hook's key-down and key-up events. Left and right keys are tracked
/// separately: releasing one side must not clear a modifier that is still held on the other side.
/// </summary>
internal sealed class ModifierKeyState
{
    private bool _leftCtrl;
    private bool _rightCtrl;
    private bool _leftShift;
    private bool _rightShift;
    private bool _leftAlt;
    private bool _rightAlt;

    /// <summary>Notes a key (its virtual-key code, as the hook reports it) going down or up. Other keys are ignored.</summary>
    public void Update(uint vkCode, bool isDown)
    {
        // Low-level hooks report the left/right variants; a generic code is treated as the left key.
        switch ((VIRTUAL_KEY)vkCode)
        {
            case VIRTUAL_KEY.VK_LCONTROL or VIRTUAL_KEY.VK_CONTROL:
                _leftCtrl = isDown;
                break;
            case VIRTUAL_KEY.VK_RCONTROL:
                _rightCtrl = isDown;
                break;
            case VIRTUAL_KEY.VK_LSHIFT or VIRTUAL_KEY.VK_SHIFT:
                _leftShift = isDown;
                break;
            case VIRTUAL_KEY.VK_RSHIFT:
                _rightShift = isDown;
                break;
            case VIRTUAL_KEY.VK_LMENU or VIRTUAL_KEY.VK_MENU:
                _leftAlt = isDown;
                break;
            case VIRTUAL_KEY.VK_RMENU:
                _rightAlt = isDown;
                break;
        }
    }

    /// <summary>Whether one of the modifiers in <paramref name="flags"/> is held.</summary>
    public bool IsActive(ModifierKey flags) =>
        (flags.HasFlag(ModifierKey.Ctrl) && (_leftCtrl || _rightCtrl))
        || (flags.HasFlag(ModifierKey.Shift) && (_leftShift || _rightShift))
        || (flags.HasFlag(ModifierKey.Alt) && (_leftAlt || _rightAlt));

    /// <summary>
    /// Forgets every held key. Used when key events stop arriving (the hook was removed), so a key whose release was
    /// never seen doesn't count as held from then on.
    /// </summary>
    public void Clear()
    {
        _leftCtrl = _rightCtrl = false;
        _leftShift = _rightShift = false;
        _leftAlt = _rightAlt = false;
    }
}
