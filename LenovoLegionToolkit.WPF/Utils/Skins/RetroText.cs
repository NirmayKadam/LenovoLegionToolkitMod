using System;
using System.Collections.Generic;
using System.Globalization;

namespace LenovoLegionToolkit.WPF.Utils.Skins;

/// <summary>
/// Terminal-style text used by the Retro skin on the dashboard's own controls.
/// </summary>
public static class RetroText
{
    public const char Full = '█';
    public const char Empty = '░';
    public const string Prompt = "C:\\LLT>";

    /// <summary>
    /// A bar drawn with block characters, e.g. 0.5 over 10 cells is "█████░░░░░". Rounds to the nearest cell;
    /// an unknown value is an empty bar.
    /// </summary>
    public static string BlockBar(double? fraction, int cells = 10)
    {
        cells = Math.Max(1, cells);
        var value = fraction is { } f && !double.IsNaN(f) ? Math.Clamp(f, 0, 1) : 0;
        var filled = (int)Math.Round(value * cells, MidpointRounding.AwayFromZero);
        return new string(Full, filled) + new string(Empty, cells - filled);
    }

    /// <summary>A selector segment label: "Performance" becomes "[ PERFORMANCE ]".</summary>
    public static string SegmentLabel(string name) => $"[ {name.ToUpper(CultureInfo.CurrentCulture)} ]";

    /// <summary>The console line for the current power mode, before any change was made here.</summary>
    public static string ModeLine(string modeName) => $"POWER MODE = {modeName.ToUpper(CultureInfo.CurrentCulture)}";

    /// <summary>The console line after a change: the new mode and whether it worked.</summary>
    public static string ChangeLine(string modeName, bool succeeded) =>
        $"{ModeLine(modeName)}. {(succeeded ? "OK" : "FAILED")}";

    /// <summary>
    /// What a typewriter effect shows, one more character per frame: "", "P", "PO", … up to the full text.
    /// </summary>
    public static IEnumerable<string> TypingFrames(string text)
    {
        for (var i = 0; i <= text.Length; i++)
            yield return text[..i];
    }
}
