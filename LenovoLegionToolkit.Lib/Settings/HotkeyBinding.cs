namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>
/// A global keyboard shortcut as Windows' <c>RegisterHotKey</c> takes it: modifier flags (Alt = 1, Ctrl = 2,
/// Shift = 4, Win = 8) and a virtual-key code.
/// </summary>
public class HotkeyBinding
{
    public const uint Alt = 0x1;
    public const uint Control = 0x2;
    public const uint Shift = 0x4;
    public const uint Win = 0x8;

    public uint Modifiers { get; set; }

    public uint VirtualKey { get; set; }

    public override bool Equals(object? obj) => obj is HotkeyBinding other && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

    public override int GetHashCode() => (int)(Modifiers * 397 ^ VirtualKey);
}
