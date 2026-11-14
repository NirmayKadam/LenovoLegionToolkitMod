using System.Collections.Generic;
using System.Windows.Input;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Turns a key press into a <see cref="HotkeyBinding"/> and back into text like "Ctrl+Shift+O".
/// </summary>
public static class HotkeyGesture
{
    /// <summary>
    /// The binding for a key pressed with modifiers, or null when it can't be a good global shortcut:
    /// a modifier key on its own, or no Ctrl/Alt/Win (Shift alone, or nothing, would steal ordinary typing).
    /// </summary>
    public static HotkeyBinding? FromKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierKey(key) || key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed)
            return null;

        var flags = ToFlags(modifiers);
        if ((flags & (HotkeyBinding.Control | HotkeyBinding.Alt | HotkeyBinding.Win)) == 0)
            return null;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk == 0 ? null : new HotkeyBinding { Modifiers = flags, VirtualKey = vk };
    }

    /// <summary>"Ctrl+Shift+O"-style text, modifiers in Windows' usual order.</summary>
    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>();
        if ((binding.Modifiers & HotkeyBinding.Control) != 0) parts.Add("Ctrl");
        if ((binding.Modifiers & HotkeyBinding.Alt) != 0) parts.Add("Alt");
        if ((binding.Modifiers & HotkeyBinding.Shift) != 0) parts.Add("Shift");
        if ((binding.Modifiers & HotkeyBinding.Win) != 0) parts.Add("Win");
        parts.Add(KeyName(KeyInterop.KeyFromVirtualKey((int)binding.VirtualKey)));
        return string.Join("+", parts);
    }

    public static uint ToFlags(ModifierKeys modifiers)
    {
        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= HotkeyBinding.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= HotkeyBinding.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= HotkeyBinding.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) flags |= HotkeyBinding.Win;
        return flags;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (char)('0' + (key - Key.NumPad0)),
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString(),
    };
}
